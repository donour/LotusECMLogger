using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SAE.J2534;

namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// Owns one raw-CAN channel/device. CcpMaster shares this transport between consumers.
/// <para>
/// Reads never block inside the DLL: <see cref="Read"/> polls with zero-timeout reads and sleeps outside the I/O lock
/// between polls, with the Windows timer raised to 1 ms while the transport is open. Measured on a 2011 Evora
/// (2026-10-24): a blocking ReadMsgs(1, 5 ms) loop cost 32 ms per command at the default timer resolution and 20 ms at
/// 1 ms, because each send waited out the reader's wait and the reply then waited out the next one. Letting sends
/// bypass our lock made it 59 ms: that DLL serialises calls internally and holds its own lock through the wait.
/// </para>
/// </summary>
public sealed class J2534CcpTransport : ICcpTransport
{
    private const uint TimerResolutionMs = 1;
    private const int MaxMessagesPerRead = 32;

    private readonly object _ioLock = new();
    private readonly bool _timerRaised;
    private readonly J2534Session _session;
    private readonly J2534Channel _channel;
    private readonly HashSet<uint> _receiveIds;
    private bool _disposed;
    private int _reading;

    public CcpTarget Target { get; }

    private J2534CcpTransport(CcpTarget target, J2534Session session, J2534Channel channel)
    {
        (Target, _session, _channel, _receiveIds) = (target, session, channel, [target.DtoId]);
        _timerRaised = timeBeginPeriod(TimerResolutionMs) == 0;
        Debug.WriteLine($"CCP J2534: timer resolution {(_timerRaised ? "raised to 1 ms" : "unchanged")}.");
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

    public static J2534CcpTransport Open(CcpTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var session = J2534Session.Open();
        try
        {
            var channel = session.OpenCan();
            channel.StartMessageFilter(PassFilter(target.DtoId)).ThrowIfError();
            return new(target, session, channel);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    // J2534 PASS filters OR together, so each admitted ID gets its own exact-match filter.
    private static MessageFilter PassFilter(uint canId)
    {
        var pattern = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pattern, canId);
        return new MessageFilter
        {
            FilterType = Filter.PASS_FILTER,
            Mask = [0x00, 0x00, 0x07, 0xFF],
            Pattern = pattern,
        };
    }

    public void AddReceiveId(uint canId)
    {
        CcpTransportRules.ValidateReceiveId(Target, canId);
        Locked(() =>
        {
            if (_receiveIds.Contains(canId)) return;
            _channel.StartMessageFilter(PassFilter(canId)).ThrowIfError();
            _receiveIds.Add(canId);
        });
    }

    public void Send(ReadOnlySpan<byte> command)
    {
        if (command.Length != CcpFrame.Length) throw new ArgumentException("CRO must have DLC 8.", nameof(command));
        SendFrame(Target.CroId, command);
    }

    public void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload)
    {
        CcpTransportRules.ValidateAuxiliary(Target, canId, payload);
        SendFrame(canId, payload);
    }

    private void SendFrame(uint canId, ReadOnlySpan<byte> payload)
    {
        var message = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(message, canId);
        payload.CopyTo(message.AsSpan(4));
        Locked(() => _channel.SendMessage(message).ThrowIfError());
    }

    private void Locked(Action action)
    {
        lock (_ioLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            action();
        }
    }

    public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        if (timeoutMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
            throw new InvalidOperationException("CCP requires a single receive loop.");
        try
        {
            long deadline = Environment.TickCount64 + timeoutMilliseconds;
            var frames = new List<CcpReceivedFrame>();
            while (true)
            {
                // Zero-timeout reads return at once with whatever the DLL has buffered, so the lock is held only
                // briefly and a waiting send goes out within one poll.
                lock (_ioLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    while (Collect(_channel.ReadMessages(MaxMessagesPerRead, 0), frames) == MaxMessagesPerRead) { }
                }
                // Once consumed, return the batch even if cancellation arrives: the owner routes it first.
                if (frames.Count > 0 || cancellationToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
                    return frames;
                Thread.Sleep(1); // ~1 ms with the raised timer resolution; outside the lock, so sends are not delayed
            }
        }
        finally { Volatile.Write(ref _reading, 0); }
    }

    /// <summary>Adds the batch's admitted frames and returns how many messages the DLL delivered.</summary>
    private int Collect(GetMessagesResult result, List<CcpReceivedFrame> frames)
    {
        // TIMEOUT may still carry a partial batch: preserve it. Other failures are not silent timeouts.
        if (!result.IsSuccess && !result.IsTimeout && !result.IsBufferEmpty)
            throw new IOException($"J2534 CCP receive failed: {result.Status}.");
        foreach (var message in result.Messages)
        {
            if ((message.RxStatus & (RxFlag.TX_MSG_TYPE | RxFlag.TX_INDICATION | RxFlag.CAN_29BIT_ID)) != 0)
                continue;
            if (message.Data is not { Length: >= 5 and <= 12 } data) continue;
            uint canId = BinaryPrimitives.ReadUInt32BigEndian(data);
            if (!_receiveIds.Contains(canId)) continue;
            frames.Add(new(data.AsMemory(4).ToArray(), message.Timestamp, canId));
        }
        return result.Messages.Length;
    }

    public void Dispose()
    {
        lock (_ioLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            if (_timerRaised) timeEndPeriod(TimerResolutionMs);
        }
    }
}
