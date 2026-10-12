namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// Raw CAN transport for one target. All payloads exclude the four-byte J2534 CAN header.
/// Besides the CCP CRO/DTO pair, the transport can carry auxiliary traffic (e.g. the AEM wideband's
/// OBD poll/reply) that shares the adapter and receive loop but is never interpreted as CCP.
/// </summary>
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

    /// <summary>Also admit an 11-bit receive ID besides the target's DTO. Idempotent; never removed.</summary>
    void AddReceiveId(uint canId);
    /// <summary>Send a 1–8 byte frame to an 11-bit ID other than the target's CRO/DTO.</summary>
    void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload);
}

public readonly record struct CcpReceivedFrame(ReadOnlyMemory<byte> Payload, uint TimestampMicroseconds, uint CanId);

public static class CcpTransportRules
{
    public const uint MaxStandardId = 0x7FF;

    public static void ValidateReceiveId(CcpTarget target, uint canId)
    {
        if (canId > MaxStandardId) throw new ArgumentOutOfRangeException(nameof(canId), "Only 11-bit IDs are supported.");
        if (canId == target.CroId) throw new ArgumentException("The CRO ID carries commands, not received frames.", nameof(canId));
    }

    public static void ValidateAuxiliary(CcpTarget target, uint canId, ReadOnlySpan<byte> payload)
    {
        if (canId > MaxStandardId) throw new ArgumentOutOfRangeException(nameof(canId), "Only 11-bit IDs are supported.");
        // CCP commands must go through Send so the session can match their replies.
        if (canId == target.CroId || canId == target.DtoId)
            throw new ArgumentException("Auxiliary frames must not use the CCP CRO/DTO IDs.", nameof(canId));
        if (payload.Length is < 1 or > 8) throw new ArgumentException("A CAN payload has 1–8 bytes.", nameof(payload));
    }
}
