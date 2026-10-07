using System.Buffers.Binary;
using SAE.J2534;

namespace LotusECMLogger.Services.Ccp;

/// <summary>Owns one raw-CAN channel/device. The future CcpMaster shares this transport between consumers.</summary>
public sealed class J2534CcpTransport : ICcpTransport
{
    private readonly object _ioLock = new();
    private readonly J2534Session _session;
    private readonly J2534Channel _channel;
    private bool _disposed;
    private int _reading;
    public CcpTarget Target { get; }

    private J2534CcpTransport(CcpTarget target, J2534Session session, J2534Channel channel)
        => (Target, _session, _channel) = (target, session, channel);

    public static J2534CcpTransport Open(CcpTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var session = J2534Session.Open();
        try
        {
            var channel = session.OpenCan();
            var pattern = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(pattern, target.DtoId);
            channel.StartMessageFilter(new MessageFilter
            {
                FilterType = Filter.PASS_FILTER,
                Mask = [0x00, 0x00, 0x07, 0xFF],
                Pattern = pattern,
            }).ThrowIfError();
            return new(target, session, channel);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Send(ReadOnlySpan<byte> command)
    {
        if (command.Length != CcpFrame.Length) throw new ArgumentException("CRO must have DLC 8.", nameof(command));
        Span<byte> message = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(message, Target.CroId);
        command.CopyTo(message[4..]);
        lock (_ioLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _channel.SendMessage(message).ThrowIfError();
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
            GetMessagesResult result;
            lock (_ioLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                result = _channel.ReadMessages(32, timeoutMilliseconds);
            }
            // TIMEOUT may still carry a partial batch: preserve it. Other failures are not silent timeouts.
            if (!result.IsSuccess && !result.IsTimeout && !result.IsBufferEmpty)
                throw new IOException($"J2534 CCP receive failed: {result.Status}.");
            var frames = new List<CcpReceivedFrame>(result.Messages.Length);
            foreach (var message in result.Messages)
            {
                if ((message.RxStatus & (RxFlag.TX_MSG_TYPE | RxFlag.TX_INDICATION | RxFlag.CAN_29BIT_ID)) != 0)
                    continue;
                if (message.Data is not { Length: 12 } data ||
                    BinaryPrimitives.ReadUInt32BigEndian(data) != Target.DtoId) continue;
                frames.Add(new(data.AsMemory(4).ToArray(), message.Timestamp));
            }
            // Once consumed, return the batch even if cancellation arrived during the native read.
            // The owner can route these frames before ending the receive loop.
            return frames;
        }
        finally { Volatile.Write(ref _reading, 0); }
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
