namespace LotusECMLogger.Services.Ccp;

public enum CcpSessionState
{
    /// <summary>No session; only CONNECT and TEST may be sent.</summary>
    Disconnected,
    Connected,
    /// <summary>
    /// A reply was lost, late, unexpected or cancelled, so the slave's MTA/DAQ pointer/CRM state is unknown.
    /// DAQ reception continues; memory and DAQ configuration are refused until ConnectAsync succeeds again.
    /// </summary>
    Uncertain,
    /// <summary>The transport failed. The master must be disposed and a new one created.</summary>
    Faulted,
    Disposed,
}

public sealed class CcpMasterOptions
{
    /// <summary>Reply wait for ordinary commands; 400 ms is what the live-validated logger used.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMilliseconds(400);
    /// <summary>BUILD_CHKSUM allowance: base plus one per 256-byte main-loop pass (real pass timing needs hardware).</summary>
    public TimeSpan ChecksumBaseTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ChecksumPassTimeout { get; init; } = TimeSpan.FromMilliseconds(20);
    /// <summary>Receive-loop read timeout. Bounds shutdown latency, not reply latency.</summary>
    public int ReceivePollMilliseconds { get; init; } = 5;
    /// <summary>Per-subscriber queue bound; full queues drop (and count) rather than stall the receive loop.</summary>
    public int StreamCapacity { get; init; } = 4096;
}

public sealed class CcpTimeoutException(CcpCommandCode command, byte counter, TimeSpan timeout)
    : TimeoutException($"No CCP reply to {command} (counter {counter}) within {timeout.TotalMilliseconds:0} ms. " +
        "Check that the ECU is on the selected CAN bus and its CCP enable byte is set.")
{
    public CcpCommandCode Command { get; } = command;
    public byte Counter { get; } = counter;
}

/// <summary>The slave answered, but not as this firmware does.</summary>
public sealed class CcpProtocolException(string message) : IOException(message);

/// <summary>EXCHANGE_ID fields, the uploaded ID string and the CCP version.</summary>
public sealed record CcpSlaveIdentity(string SlaveId, CcpIdentification Fields, byte VersionMajor, byte VersionMinor);
