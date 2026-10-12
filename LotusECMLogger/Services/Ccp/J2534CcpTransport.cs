using System.Buffers.Binary;
using SAE.J2534;

namespace LotusECMLogger.Services.Ccp;

/// <summary>Owns one raw-CAN channel/device. CcpMaster shares this transport between consumers.</summary>
public sealed class J2534CcpTransport : ICcpTransport
{
    private readonly object _ioLock = new();
    private readonly J2534Session _session;
    private readonly J2534Channel _channel;
    private readonly HashSet<uint> _receiveIds;
    private bool _disposed;
    private int _reading;
    private int _sendsWaiting;
    public CcpTarget Target { get; }

    private J2534CcpTransport(CcpTarget target, J2534Session session, J2534Channel channel)
        => (Target, _session, _channel, _receiveIds) = (target, session, channel, [target.DtoId]);

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

    // Senders announce themselves so the receive loop yields the lock between reads instead of
    // re-acquiring it immediately (Monitor is not fair) and delaying commands by a full read timeout.
    private void Locked(Action action)
    {
        Interlocked.Increment(ref _sendsWaiting);
        try
        {
            lock (_ioLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                action();
            }
        }
        finally { Interlocked.Decrement(ref _sendsWaiting); }
    }

    public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        if (timeoutMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
            throw new InvalidOperationException("CCP requires a single receive loop.");
        try
        {
            var spin = new SpinWait();
            while (Volatile.Read(ref _sendsWaiting) > 0) spin.SpinOnce();
            var frames = new List<CcpReceivedFrame>();
            lock (_ioLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Wait for one message (J2534 returns as soon as NumMsgs arrive), then drain without waiting,
                // so a reply is seen as soon as it arrives rather than when the timeout expires.
                if (Collect(_channel.ReadMessages(1, timeoutMilliseconds), frames))
                    Collect(_channel.ReadMessages(32, 0), frames);
            }
            // Once consumed, return the batch even if cancellation arrived during the native read.
            // The owner can route these frames before ending the receive loop.
            return frames;
        }
        finally { Volatile.Write(ref _reading, 0); }
    }

    private bool Collect(GetMessagesResult result, List<CcpReceivedFrame> frames)
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
        return result.Messages.Length > 0;
    }

    public void Dispose()
    {
        lock (_ioLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
