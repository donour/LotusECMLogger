namespace LotusECMLogger.Services.Ccp;

/// <summary>Raw CAN transport for one target. All payloads exclude the four-byte J2534 CAN header.</summary>
public interface ICcpTransport : IDisposable
{
    CcpTarget Target { get; }
    void Send(ReadOnlySpan<byte> command);
    /// <summary>
    /// Called by the session's sole receive loop. Timeout must be 0–100 ms to bound shutdown latency.
    /// Returns owned payload copies, preserving the adapter's wrapping 32-bit microsecond timestamp.
    /// Blocking reads belong on the background receive worker, never the UI thread.
    /// </summary>
    IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default);
}

public readonly record struct CcpReceivedFrame(ReadOnlyMemory<byte> Payload, uint TimestampMicroseconds);
