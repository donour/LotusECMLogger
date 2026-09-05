namespace LotusECMLogger.Services;

/// <summary>The existing services open exclusive device sessions. Reject overlaps before loading a driver.</summary>
internal sealed class J2534DeviceLease : IDisposable
{
    private static int claimed;
    private int released;

    private J2534DeviceLease() { }

    public static J2534DeviceLease Acquire()
    {
        if (Interlocked.CompareExchange(ref claimed, 1, 0) != 0)
            throw new InvalidOperationException("The diagnostic device is in use. Stop the other logger or wait for its operation to finish.");
        return new J2534DeviceLease();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
            Interlocked.Exchange(ref claimed, 0);
    }
}
