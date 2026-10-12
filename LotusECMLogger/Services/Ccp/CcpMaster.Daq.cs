namespace LotusECMLogger.Services.Ccp;

public sealed partial class CcpMaster
{
    private long _unroutedDaq;

    /// <summary>DAQ frames whose PID belongs to no active subscription.</summary>
    public long UnroutedDaqFrames => Interlocked.Read(ref _unroutedDaq);

    private void RouteDaq(CcpPacket packet, uint timestampMicroseconds) => Interlocked.Increment(ref _unroutedDaq);

    private void EndAllDaq(string reason, Exception? error = null) { }
}
