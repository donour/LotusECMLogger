using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpMasterDaqTests : IDisposable
{
    private const uint Rpm = 0x400017DA, Coolant = 0x400018AE;
    private readonly FakeCcpSlave _slave = new();
    private readonly SimulatedCcpTransport _simulation;
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;

    public CcpMasterDaqTests()
    {
        _simulation = new SimulatedCcpTransport(_slave, speedFactor: 20);
        _script = new ScriptedTransport(_simulation);
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
        _simulation.With(s => { s.Load(Rpm, [0x12, 0x34]); s.Load(Coolant, [0x80]); });
    }

    public void Dispose()
    {
        _master.Dispose();
        _slave.Dispose();
    }

    private static CcpDaqElement E(uint address, int size) => new(address, size);

    private static CcpDaqListRequest List(int eventChannel = 0, int prescaler = 1, params CcpDaqElement[][] odts) =>
        new(eventChannel, prescaler, odts);

    private static readonly CcpDaqListRequest RpmAndCoolant = List(0, 1, [E(Rpm, 2), E(Coolant, 1)]);

    private static async Task<CcpDaqSample> NextAsync(CcpDaqSubscription subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await subscription.Stream.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>Reads to the end: Reader.Completion only completes once buffered samples are consumed.</summary>
    private static async Task DrainAsync(CcpDaqSubscription subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var _ in subscription.Stream.Reader.ReadAllAsync(timeout.Token)) { }
    }

    private static async Task<List<CcpDaqSample>> TakeAsync(CcpDaqSubscription subscription, int count)
    {
        var samples = new List<CcpDaqSample>();
        while (samples.Count < count) samples.Add(await NextAsync(subscription));
        return samples;
    }

    [Fact]
    public async Task ReplaysTheExampleSessionByteForByte()
    {
        await _master.ConnectAsync();
        var subscription = await _master.StartDaqAsync([RpmAndCoolant]);
        var sample = await NextAsync(subscription);
        Assert.Equal((0, 0, false), (sample.List, sample.Odt, sample.Overrun));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x80 }, sample.Data[..3].ToArray());
        await subscription.StopAsync();
        await _master.DisconnectAsync();

        // CCP-support.md §10, zero-padded to DLC 8. Stopping uses START_STOP on this subscription's list
        // instead of the example's START_STOP_ALL, so other consumers' lists keep running.
        Assert.Equal([
            "0100000000000000", "1B01020100000000",
            "1402000000000351",
            "1503000000000000", "16040200400017DA",
            "1505000001000000", "16060100400018AE",
            "0607020000000001", "0808010000000000",
            "0609000000000001", "070A010000000000",
        ], _script.SentHex);
        Assert.Equal(new[] { 0 }, subscription.SlaveLists);
        Assert.Equal(new byte[] { 0 }, subscription.FirstPids);
        Assert.False(subscription.IsActive);
        Assert.Equal(0, _master.UnroutedDaqFrames);
    }

    [Fact]
    public async Task MemoryOperationsRunWhileDaqStreams()
    {
        var block = Enumerable.Range(0, 2000).Select(i => (byte)i).ToArray();
        _simulation.With(s => s.Load(0x40003000, block));
        await _master.ConnectAsync();
        var subscription = await _master.StartDaqAsync([RpmAndCoolant]);
        await NextAsync(subscription);

        var read = _master.ReadAsync(0x40003000, block.Length);
        var checksum = _master.ChecksumAsync(0x40003000, block.Length);
        var identity = _master.ExchangeIdAsync();
        Assert.Equal(block, await read);
        Assert.Equal(CcpFrame.AdditiveChecksum(block), await checksum);
        Assert.Equal("Ccp_T6", (await identity).SlaveId);

        var samples = await TakeAsync(subscription, 20);
        Assert.All(samples, s => Assert.Equal(new byte[] { 0x12, 0x34, 0x80 }, s.Data[..3].ToArray()));
        Assert.Equal(CcpSessionState.Connected, _master.State);
        Assert.Equal((0L, 0L, 0L, 0L), (_master.StrayReplies, _master.MalformedFrames, _master.UnroutedDaqFrames, subscription.Stream.Dropped));
    }

    [Fact]
    public async Task ConsumersGetSeparateListsAndStoppingOneLeavesTheOtherRunning()
    {
        await _master.ConnectAsync();
        var first = await _master.StartDaqAsync([List(0, 1, [E(Rpm, 2)])]);
        var second = await _master.StartDaqAsync([List(2, 1, [E(Coolant, 1)]), List(0, 2, [E(Rpm, 2)], [E(Coolant, 1)])]);
        Assert.Equal(new[] { 0 }, first.SlaveLists);
        Assert.Equal(new[] { 1, 2 }, second.SlaveLists);
        Assert.Equal(new byte[] { 12, 24 }, second.FirstPids);
        Assert.Equal(1, first.Gaps); // second's GET_DAQ_SIZE reset the shared transmit queue
        Assert.Equal(0, second.Gaps);

        _script.ClearSent();
        await first.StopAsync();
        Assert.Equal(["06" + _script.Sent[0][1].ToString("X2") + "000000000001"], _script.SentHex);
        await DrainAsync(first);

        var samples = await TakeAsync(second, 12);
        Assert.Contains(samples, s => s is { List: 0, Odt: 0 });
        Assert.Contains(samples, s => s is { List: 1, Odt: 1 });
        Assert.All(samples.Where(s => s.List == 1 && s.Odt == 0), s => Assert.Equal(new byte[] { 0x12, 0x34 }, s.Data[..2].ToArray()));
        Assert.All(samples.Where(s => s.List == 1 && s.Odt == 1), s => Assert.Equal((byte)0x80, s.Data.Span[0]));

        var reused = await _master.StartDaqAsync([List(0, 1, [E(Rpm, 2)])]);
        Assert.Equal(new[] { 0 }, reused.SlaveLists); // freed list is reused
    }

    [Fact]
    public async Task AllocationFailsWhenListsRunOut()
    {
        await _master.ConnectAsync();
        await _master.StartDaqAsync(Enumerable.Repeat(List(9, 1, [E(Rpm, 2)]), 9).ToArray());
        int sent = _script.Sent.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.StartDaqAsync([List(9, 1, [E(Rpm, 2)]), List(9, 1, [E(Rpm, 2)])]));
        Assert.Equal(sent, _script.Sent.Count);
        await _master.StartDaqAsync([List(9, 1, [E(Rpm, 2)])]); // the tenth list is still free
    }

    public static TheoryData<CcpDaqListRequest> InvalidLists => new()
    {
        List(10, 1, [E(Rpm, 2)]),                                         // no event channel 10
        List(0, 0, [E(Rpm, 2)]),                                          // prescaler 0
        List(0, 1),                                                         // no ODTs
        new(0, 1, Enumerable.Repeat<IReadOnlyList<CcpDaqElement>>([E(Rpm, 2)], 13).ToArray()), // 13 ODTs
        List(0, 1, [E(Rpm, 3)]),                                          // size 3
        List(0, 1, [E(0, 2)]),                                            // address 0 ends the ODT
        List(0, 1, [E(Rpm, 4), E(Rpm, 4)]),                             // eight bytes
        List(0, 1, Enumerable.Repeat(new CcpDaqElement(Coolant, 1), 8).ToArray()), // element 7 aliases the next ODT
    };

    [Theory]
    [MemberData(nameof(InvalidLists))]
    public async Task InvalidListsAreRejectedBeforeAnythingIsSent(CcpDaqListRequest request)
    {
        await _master.ConnectAsync();
        _script.ClearSent();
        Assert.Throws<ArgumentException>(() => { _ = _master.StartDaqAsync([request]); });
        Assert.Empty(_script.Sent);
    }

    [Fact]
    public async Task FailedConfigurationReleasesItsListsAndClearsThemBeforeTheNextStart()
    {
        await _master.ConnectAsync();
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.StartStopAll;
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.StartDaqAsync([RpmAndCoolant]));
        Assert.Equal(CcpSessionState.Uncertain, _master.State);

        _script.Drop = null;
        await _master.ConnectAsync();
        // List 0 was left prepared. The next start takes list 0 again (re-initialising it as it goes).
        var subscription = await _master.StartDaqAsync([RpmAndCoolant]);
        Assert.Equal(new[] { 0 }, subscription.SlaveLists);
        await NextAsync(subscription);

        // A start that fails before START_STOP_ALL, on a different list, is cleared by the next start.
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.StartStopAll;
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.StartDaqAsync([List(1, 1, [E(Coolant, 1)])]));
        _script.Drop = null;
        await _master.ConnectAsync();
        var other = await _master.StartDaqAsync([List(4, 1, [E(Rpm, 2)]), List(4, 1, [E(Rpm, 2)])]);
        Assert.Equal(new[] { 1, 2 }, other.SlaveLists);
        Assert.Equal(new[] { 0, 1, 2 }, _simulation.With(s => Enumerable.Range(0, 3).Where(l => s.DaqEntry(l, 0, 0).Address != 0).ToArray()));
    }

    [Fact]
    public async Task FailedConfigurationOnAnUnusedListIsReinitialisedBeforeStartAll()
    {
        await _master.ConnectAsync();
        var keep = await _master.StartDaqAsync([List(0, 1, [E(Rpm, 2)])]);
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.StartStopAll;
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.StartDaqAsync([List(1, 1, [E(Coolant, 1)]), List(1, 1, [E(Coolant, 1)])]));
        _script.Drop = null;
        await _master.ConnectAsync();
        _script.ClearSent();
        var single = await _master.StartDaqAsync([List(1, 1, [E(Coolant, 1)])]); // takes list 1; list 2 is still prepared
        Assert.Equal(new[] { 1 }, single.SlaveLists);
        var commands = _script.Sent.Select(c => ((CcpCommandCode)c[0], c[2])).ToArray();
        int reinit = Array.IndexOf(commands, (CcpCommandCode.GetDaqSize, (byte)2));
        Assert.True(reinit > 0 && reinit < Array.IndexOf(commands, (CcpCommandCode.StartStopAll, (byte)1)));
        Assert.Equal(0u, _simulation.With(s => s.DaqEntry(2, 0, 0).Address));
        Assert.True(keep.IsActive);
    }

    [Fact]
    public async Task OverrunFlagReachesTheSample()
    {
        await _master.ConnectAsync();
        var subscription = await _master.StartDaqAsync([List(9, 1, [E(Rpm, 2)])]);
        _script.Inject([0x80, 1, 2, 3, 4, 5, 6, 7]); // PID 0 with bit 7 set
        CcpDaqSample sample;
        do sample = await NextAsync(subscription); // a genuine 1 Hz sample may arrive first
        while (!sample.Overrun);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, sample.Data.ToArray());
    }

    [Fact]
    public async Task EndingTheSessionEndsEverySubscription()
    {
        await _master.ConnectAsync();
        var subscription = await _master.StartDaqAsync([RpmAndCoolant]);
        await _master.DisconnectAsync();
        await DrainAsync(subscription);
        Assert.False(subscription.IsActive);
        Assert.Equal("The CCP session ended.", subscription.EndReason);
        await subscription.StopAsync(); // already ended: nothing to send
    }

    [Fact]
    public async Task StoppingWhileUncertainReleasesLocally()
    {
        await _master.ConnectAsync();
        var subscription = await _master.StartDaqAsync([RpmAndCoolant]);
        _script.Inject(ScriptedTransport.Crm(0x99));
        for (int i = 0; i < 200 && _master.State != CcpSessionState.Uncertain; i++) await Task.Delay(10);
        int sent = _script.Sent.Count;
        await subscription.StopAsync();
        Assert.Equal(sent, _script.Sent.Count);
        Assert.False(subscription.IsActive);
    }
}
