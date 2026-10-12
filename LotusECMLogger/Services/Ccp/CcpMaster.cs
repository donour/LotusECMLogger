using System.Diagnostics;
using System.Text;

namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// CCP 2.1 master session for the EFI/Vector slave. One background receive loop owns the transport's
/// reads and routes every frame: CRMs to the waiting command (matched on CTR), DAQ to subscribers, and
/// admitted auxiliary IDs to their subscribers. Commands are serialised as whole operations, because the
/// slave has a single CRM buffer, MTA0, DAQ pointer and checksum worker.
/// <para>
/// A lost, late, unexpected or cancelled reply leaves the slave's state unknown: the session becomes
/// <see cref="CcpSessionState.Uncertain"/>, post-incrementing transfers are never retried, and memory/DAQ
/// operations are refused until <see cref="ConnectAsync"/> succeeds again. DAQ reception is unaffected.
/// </para>
/// The master owns the transport and disposes it. It never sends DISCONNECT on its own: that belongs to
/// the session owner after the last consumer releases it.
/// </summary>
public sealed partial class CcpMaster : IDisposable
{
    private const int MaxSlaveIdLength = 64;

    private readonly ICcpTransport _transport;
    private readonly CcpMasterOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _receiver;
    private PendingCommand? _pending;
    private CcpSessionState _state = CcpSessionState.Disconnected;
    private string? _stateReason;
    private Exception? _fault;
    private byte _counter;
    private long _strayReplies, _eventPackets, _malformedFrames;
    private long _commands, _replies, _errorReplies, _timeouts, _cancellations, _uncertainTransitions;
    private readonly CcpLatencyStats _latency = new();

    public CcpMaster(ICcpTransport transport, CcpMasterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _options = options ?? new CcpMasterOptions();
        if (_options.ReceivePollMilliseconds is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "Receive poll must be 0–100 ms.");
        if (_options.StreamCapacity < 1) throw new ArgumentOutOfRangeException(nameof(options));
        _receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "CCP receive" };
        _receiver.Start();
    }

    public CcpTarget Target => _transport.Target;
    public CcpSessionState State { get { lock (_sync) return _state; } }
    /// <summary>Why the session last became uncertain or faulted.</summary>
    public string? StateReason { get { lock (_sync) return _stateReason; } }
    /// <summary>Raised on the thread that caused the change (often the receive loop); handlers must not block.</summary>
    public event EventHandler<CcpSessionState>? StateChanged;

    /// <summary>CRMs that matched no waiting command.</summary>
    public long StrayReplies => Interlocked.Read(ref _strayReplies);
    /// <summary>0xFE event messages. This firmware signals overruns in the PID instead, so expect zero.</summary>
    public long EventPackets => Interlocked.Read(ref _eventPackets);
    /// <summary>DTOs that were not DLC 8 or carried a PID outside the target's range.</summary>
    public long MalformedFrames => Interlocked.Read(ref _malformedFrames);

    /// <summary>Recent command traffic, for the traffic monitor.</summary>
    public CcpTrace Trace { get; } = new();

    public CcpMasterStatistics GetStatistics()
    {
        var (last, mean, p95, max) = _latency.Snapshot();
        return new CcpMasterStatistics(Interlocked.Read(ref _commands), Interlocked.Read(ref _replies),
            Interlocked.Read(ref _errorReplies), Interlocked.Read(ref _timeouts), Interlocked.Read(ref _cancellations),
            StrayReplies, EventPackets, MalformedFrames, Interlocked.Read(ref _daqFrames), UnroutedDaqFrames,
            Interlocked.Read(ref _uncertainTransitions), last, mean, p95, max);
    }

    // ---- Session commands ----------------------------------------------------------------------

    /// <summary>
    /// CONNECT (station 0) and check the slave reports CCP 2.1. Also recovers an uncertain session: CONNECT
    /// while connected does not stop DAQ, and any stray reply during the exchange keeps the session uncertain.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => RunAsync(
        s => s is CcpSessionState.Disconnected or CcpSessionState.Connected or CcpSessionState.Uncertain, "connect",
        async ct =>
        {
            long strays = StrayReplies;
            await ExchangeAsync(CcpFrame.Connect, ct).ConfigureAwait(false);
            var (major, minor) = await GetVersionCoreAsync(ct).ConfigureAwait(false);
            if (major != 2 || minor != 1)
                throw new CcpProtocolException($"The slave reports CCP {major}.{minor}; this master supports 2.1.");
            if (StrayReplies != strays)
            {
                MarkUncertain("An unexpected reply arrived while connecting.", fromAnyLiveState: true);
                throw new CcpProtocolException("An unexpected CCP reply arrived while connecting; retry once the slave is idle.");
            }
            Transition(CcpSessionState.Connected, null);
            return true;
        }, cancellationToken);

    /// <summary>TEST station 0. Answered even without a session; does not change state.</summary>
    public Task TestAsync(CancellationToken cancellationToken = default) => RunAsync(
        s => s is CcpSessionState.Disconnected or CcpSessionState.Connected or CcpSessionState.Uncertain, "test",
        async ct => { await ExchangeAsync(CcpFrame.Test, ct).ConfigureAwait(false); return true; }, cancellationToken);

    public Task<(byte Major, byte Minor)> GetVersionAsync(CancellationToken cancellationToken = default) =>
        RunConnectedAsync("read the CCP version", GetVersionCoreAsync, cancellationToken);

    /// <summary>EXCHANGE_ID, then UPLOAD of the ID string it points MTA0 at, then GET_CCP_VERSION.</summary>
    public Task<CcpSlaveIdentity> ExchangeIdAsync(CancellationToken cancellationToken = default) =>
        RunConnectedAsync("exchange the slave ID", ExchangeIdCoreAsync, cancellationToken);

    /// <summary>
    /// DISCONNECT. Ending the session stops every DAQ list on the slave and ends all DAQ subscriptions;
    /// a temporary disconnect leaves DAQ running.
    /// </summary>
    public Task DisconnectAsync(bool endSession = true, CancellationToken cancellationToken = default) => RunAsync(
        s => s is CcpSessionState.Connected or CcpSessionState.Uncertain, "disconnect",
        async ct =>
        {
            await ExchangeAsync(c => CcpFrame.Disconnect(c, endSession), ct).ConfigureAwait(false);
            Transition(CcpSessionState.Disconnected, null);
            if (endSession) EndAllDaq("The CCP session ended.");
            return true;
        }, cancellationToken);

    private async Task<(byte, byte)> GetVersionCoreAsync(CancellationToken ct)
    {
        var data = (await ExchangeAsync(CcpFrame.GetVersion, ct).ConfigureAwait(false)).Data.Span;
        return (data[0], data[1]);
    }

    private async Task<CcpSlaveIdentity> ExchangeIdCoreAsync(CancellationToken ct)
    {
        var fields = CcpIdentification.Parse(await ExchangeAsync(CcpFrame.ExchangeId, ct).ConfigureAwait(false));
        if (fields.IdLength is 0 or > MaxSlaveIdLength)
            throw new CcpProtocolException($"EXCHANGE_ID reported an implausible ID length of {fields.IdLength}.");
        var bytes = await UploadSequentialAsync(fields.IdLength, null, ct).ConfigureAwait(false);
        int end = Array.IndexOf(bytes, (byte)0);
        string id = Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
        var (major, minor) = await GetVersionCoreAsync(ct).ConfigureAwait(false);
        return new(id, fields, major, minor);
    }

    // ---- Command plumbing ----------------------------------------------------------------------

    private Task<T> RunConnectedAsync<T>(string what, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        RunAsync(s => s == CcpSessionState.Connected, what, operation, cancellationToken);

    /// <summary>Holds the command gate for a whole operation, so no other command can disturb MTA/DAQ/CRM state.</summary>
    private async Task<T> RunAsync<T>(Func<CcpSessionState, bool> allowed, string what,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_state == CcpSessionState.Disposed, this);
                if (_state == CcpSessionState.Faulted)
                    throw new InvalidOperationException($"Cannot {what}: the CCP transport failed ({_stateReason}).", _fault);
                if (!allowed(_state))
                    throw new InvalidOperationException(_state == CcpSessionState.Uncertain
                        ? $"Cannot {what}: the CCP session is uncertain ({_stateReason}). Reconnect first."
                        : $"Cannot {what} while the CCP session is {_state.ToString().ToLowerInvariant()}.");
            }
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Sends one CRO and awaits its CRM. Callers hold the gate. Error replies throw <see cref="CcpException"/>.</summary>
    private async Task<CcpPacket> ExchangeAsync(Func<byte, byte[]> build, CancellationToken ct, TimeSpan? timeout = null)
    {
        byte counter = _counter++;
        byte[] cro = build(counter);
        var command = (CcpCommandCode)cro[0];
        var pending = new PendingCommand(counter, command);
        lock (_sync)
        {
            if (_fault is not null) throw new IOException("The CCP transport failed.", _fault);
            _pending = pending;
        }
        try
        {
            Trace.Add(CcpTraceKind.Command, cro);
            Interlocked.Increment(ref _commands);
            pending.Started = Stopwatch.GetTimestamp();
            try { _transport.Send(cro); }
            catch (Exception ex)
            {
                MarkUncertain($"{command} could not be sent: {ex.Message}");
                throw;
            }
            var wait = timeout ?? _options.CommandTimeout;
            CcpPacket reply;
            try { reply = await pending.Reply.Task.WaitAsync(wait, ct).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                Interlocked.Increment(ref _timeouts);
                Trace.Add(CcpTraceKind.Timeout, default, note: $"no reply to {command} #{counter} within {wait.TotalMilliseconds:0} ms");
                MarkUncertain($"no reply to {command} (counter {counter})");
                throw new CcpTimeoutException(command, counter, wait);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancellations);
                Trace.Add(CcpTraceKind.Cancelled, default, note: $"{command} #{counter} cancelled while waiting for its reply");
                MarkUncertain($"{command} was cancelled before its reply arrived");
                throw;
            }
            if (reply.ErrorCode != 0) throw new CcpException(command, counter, reply.ErrorCode);
            return reply;
        }
        finally
        {
            lock (_sync) if (ReferenceEquals(_pending, pending)) _pending = null;
        }
    }

    /// <summary>UPLOADs <paramref name="length"/> bytes from the current MTA0 in five-byte steps.</summary>
    private async Task<byte[]> UploadSequentialAsync(int length, IProgress<int>? progress, CancellationToken ct)
    {
        var bytes = new byte[length];
        for (int offset = 0; offset < length; offset += 5)
        {
            int count = Math.Min(5, length - offset);
            var reply = await ExchangeAsync(c => CcpFrame.Upload(c, count), ct).ConfigureAwait(false);
            reply.Data.Span[..count].CopyTo(bytes.AsSpan(offset));
            progress?.Report(offset + count);
        }
        return bytes;
    }

    private sealed class PendingCommand(byte counter, CcpCommandCode command)
    {
        public byte Counter { get; } = counter;
        public CcpCommandCode Command { get; } = command;
        public long Started { get; set; }
        public TaskCompletionSource<CcpPacket> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // ---- State ---------------------------------------------------------------------------------

    private void Transition(CcpSessionState state, string? reason)
    {
        lock (_sync)
        {
            if (_state is CcpSessionState.Faulted or CcpSessionState.Disposed || _state == state) return;
            _state = state;
            _stateReason = reason;
        }
        Trace.Add(CcpTraceKind.State, default, note: reason is null ? $"session {state}" : $"session {state}: {reason}");
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Connected → Uncertain. A lost reply before a session exists changes nothing: only CONNECT/TEST
    /// are possible then, and both are safe to repeat.
    /// </summary>
    private void MarkUncertain(string reason, bool fromAnyLiveState = false)
    {
        bool changed;
        lock (_sync)
        {
            changed = _state == CcpSessionState.Connected ||
                (fromAnyLiveState && _state == CcpSessionState.Disconnected);
            if (changed) _state = CcpSessionState.Uncertain;
            if (changed || _state == CcpSessionState.Uncertain) _stateReason = reason;
        }
        if (!changed) return;
        Interlocked.Increment(ref _uncertainTransitions);
        Trace.Add(CcpTraceKind.State, default, note: $"session Uncertain: {reason}");
        StateChanged?.Invoke(this, CcpSessionState.Uncertain);
    }

    // ---- Receive loop --------------------------------------------------------------------------

    private void ReceiveLoop()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                IReadOnlyList<CcpReceivedFrame> frames;
                try { frames = _transport.Read(_options.ReceivePollMilliseconds, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
                foreach (var frame in frames) Route(frame);
            }
        }
        catch (Exception ex) { Fault(ex); }
    }

    private void Route(in CcpReceivedFrame frame)
    {
        if (frame.CanId != Target.DtoId)
        {
            RouteAuxiliary(frame);
            return;
        }
        CcpPacket packet;
        try { packet = CcpFrame.ParseDto(frame.Payload.Span, Target); }
        catch (ArgumentException)
        {
            Interlocked.Increment(ref _malformedFrames);
            Trace.Add(CcpTraceKind.Malformed, frame.Payload.Span);
            return;
        }
        switch (packet.Kind)
        {
            case CcpPacketKind.CommandReturn: CompleteCommand(packet); break;
            case CcpPacketKind.Event:
                Interlocked.Increment(ref _eventPackets);
                Trace.Add(CcpTraceKind.Event, packet.Bytes.Span);
                break;
            default: RouteDaq(packet, frame.TimestampMicroseconds); break;
        }
    }

    private void CompleteCommand(CcpPacket packet)
    {
        PendingCommand? pending;
        lock (_sync)
        {
            pending = _pending;
            if (pending?.Counter == packet.Counter) _pending = null;
            else pending = null;
        }
        if (pending is not null)
        {
            double latency = CcpLatencyStats.Elapsed(pending.Started);
            _latency.Add(latency);
            bool error = packet.ErrorCode != 0;
            Interlocked.Increment(ref error ? ref _errorReplies : ref _replies);
            Trace.Add(error ? CcpTraceKind.ErrorReply : CcpTraceKind.Reply, packet.Bytes.Span, latency);
            pending.Reply.TrySetResult(packet);
            return;
        }
        // A late reply (after a timeout) or someone else's: the slave's CRM/MTA state is no longer known.
        Interlocked.Increment(ref _strayReplies);
        Trace.Add(CcpTraceKind.StrayReply, packet.Bytes.Span);
        MarkUncertain($"unexpected reply with counter {packet.Counter}");
    }

    private void Fault(Exception ex)
    {
        PendingCommand? pending;
        lock (_sync)
        {
            if (_state == CcpSessionState.Disposed) return;
            _fault = ex;
            _state = CcpSessionState.Faulted;
            _stateReason = ex.Message;
            pending = _pending;
            _pending = null;
        }
        Trace.Add(CcpTraceKind.State, default, note: $"transport failed: {ex.Message}");
        pending?.Reply.TrySetException(new IOException("The CCP transport failed.", ex));
        EndAllDaq("The CCP transport failed.", ex);
        EndAllAuxiliary(ex);
        StateChanged?.Invoke(this, CcpSessionState.Faulted);
    }

    public void Dispose()
    {
        PendingCommand? pending;
        lock (_sync)
        {
            if (_state == CcpSessionState.Disposed) return;
            _state = CcpSessionState.Disposed;
            pending = _pending;
            _pending = null;
        }
        _stop.Cancel();
        if (Thread.CurrentThread != _receiver) _receiver.Join(TimeSpan.FromSeconds(2));
        pending?.Reply.TrySetException(new ObjectDisposedException(nameof(CcpMaster)));
        EndAllDaq("The CCP master was disposed.");
        EndAllAuxiliary(null);
        _transport.Dispose();
        StateChanged?.Invoke(this, CcpSessionState.Disposed);
    }
}
