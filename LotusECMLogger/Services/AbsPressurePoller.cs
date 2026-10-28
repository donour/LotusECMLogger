namespace LotusECMLogger.Services;

/// <summary>Called only from the engine logger's existing receive thread; opens no second device.</summary>
internal sealed partial class AbsPressurePoller
{
    private readonly Action<byte[]> send;
    private readonly bool raw;
    private readonly Func<long> clock;
    private readonly Func<bool> stopping;
    private readonly AbsPressureProtocol protocol;

    internal AbsPressurePoller(Action<byte[]> send, bool raw, Func<long>? clock = null, Func<bool>? stopping = null)
    {
        this.send = send;
        this.raw = raw;
        this.clock = clock ?? (() => Environment.TickCount64);
        this.stopping = stopping ?? (() => false);
        protocol = new(this.clock());
    }

    internal AbsPressureSnapshot Snapshot() => protocol.Snapshot(clock());

    internal void Tick()
    {
        if (stopping()) return;
        if (protocol.Tick(clock()) is { } request)
            Send(raw ? [2, .. request, 0, 0, 0, 0, 0] : request);
    }

    /// <returns>True for ABS traffic, false when the caller should route to the engine/AEM decoder.</returns>
    internal bool Receive(uint id, ReadOnlySpan<byte> data, bool isData = true)
    {
        if (id != AbsPressureProtocol.RequestId && id != AbsPressureProtocol.ResponseId) return false;
        if (!isData || stopping()) return true;
        if (id == AbsPressureProtocol.RequestId)
            protocol.Fail("Another ABS diagnostic request observed");
        else if (raw)
        {
            if (protocol.ReceiveCan(data, clock()) is { } flow) Send(flow);
        }
        else protocol.ReceivePayload(data, clock());
        return true;
    }

    private void Send(byte[] payload)
    {
        if (stopping()) return;
        try { send([.. Header(AbsPressureProtocol.RequestId), .. payload]); }
        catch (Exception ex) { protocol.Fail("ABS transmit failed: " + ex.Message); }
    }

    internal static byte[] Header(uint id) => [(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id];
}
