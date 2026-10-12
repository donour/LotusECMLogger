using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpTraceTests : IDisposable
{
    private readonly FakeCcpSlave _slave = new();
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;

    public CcpTraceTests()
    {
        _script = new ScriptedTransport(new SimulatedCcpTransport(_slave, speedFactor: 50));
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
    }

    public void Dispose()
    {
        _master.Dispose();
        _slave.Dispose();
    }

    [Fact]
    public async Task TracesCommandsRepliesAndStateWithLatency()
    {
        await _master.ConnectAsync();
        var trace = _master.Trace.Snapshot();
        Assert.Equal([CcpTraceKind.Command, CcpTraceKind.Reply, CcpTraceKind.Command, CcpTraceKind.Reply, CcpTraceKind.State],
            trace.Select(e => e.Kind));
        Assert.Equal("#0 CONNECT station 0", CcpTrace.Describe(trace[0]));
        Assert.Equal("CRM #0 ok  FE00000000", CcpTrace.Describe(trace[1]));
        Assert.Equal("#1 GET_CCP_VERSION 2.1", CcpTrace.Describe(trace[2]));
        Assert.Equal("session Connected", CcpTrace.Describe(trace[4]));
        Assert.All(trace.Where(e => e.Kind == CcpTraceKind.Reply), e => Assert.True(e.LatencyMs >= 0));
        Assert.Equal(trace.Select(e => e.Sequence).Order(), trace.Select(e => e.Sequence));

        var stats = _master.GetStatistics();
        Assert.Equal((2L, 2L, 0L), (stats.Commands, stats.Replies, stats.Timeouts));
        Assert.NotNull(stats.MeanLatencyMs);
        Assert.True(stats.MaxLatencyMs >= stats.P95LatencyMs);
        Assert.Empty(_master.Trace.Snapshot(trace[^1].Sequence)); // nothing newer yet
    }

    [Fact]
    public async Task TracesErrorsTimeoutsAndStrays()
    {
        await _master.ConnectAsync();
        long mark = _master.Trace.Snapshot()[^1].Sequence;

        _script.Reply = cro => cro[0] == (byte)CcpCommandCode.GetCcpVersion ? ScriptedTransport.Crm(cro[1], 0x31) : null;
        await Assert.ThrowsAsync<CcpException>(() => _master.GetVersionAsync());
        _script.Reply = null;
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.ExchangeId;
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.ExchangeIdAsync());
        _script.Inject(ScriptedTransport.Crm(0x77));
        for (int i = 0; i < 200 && _master.StrayReplies == 0; i++) await Task.Delay(10);

        var described = _master.Trace.Snapshot(mark).Select(e => (e.Kind, CcpTrace.Describe(e))).ToList();
        Assert.Contains((CcpTraceKind.ErrorReply, "CRM #2 error 0x31 (command syntax)"), described);
        Assert.Contains(described, d => d.Kind == CcpTraceKind.Timeout && d.Item2.StartsWith("no reply to ExchangeId #3"));
        Assert.Contains(described, d => d.Kind == CcpTraceKind.State && d.Item2.StartsWith("session Uncertain"));
        Assert.Contains((CcpTraceKind.StrayReply, "stray CRM #119 (no command waiting for it)"), described);

        var stats = _master.GetStatistics();
        Assert.Equal((1L, 1L, 1L, 1L), (stats.ErrorReplies, stats.Timeouts, stats.StrayReplies, stats.UncertainTransitions));
    }

    [Fact]
    public void TheRingKeepsTheNewestEntries()
    {
        var trace = new CcpTrace(capacity: 4);
        for (int i = 0; i < 10; i++) trace.Add(CcpTraceKind.State, default, note: $"n{i}");
        Assert.Equal(["n6", "n7", "n8", "n9"], trace.Snapshot().Select(e => e.Note));
        Assert.Equal(["n9"], trace.Snapshot(9).Select(e => e.Note));
    }

    [Theory]
    [InlineData("#5 SET_MTA0 0x40008654")]
    [InlineData("#5 UPLOAD 5")]
    [InlineData("#5 SHORT_UP 2 @ 0x40001764")]
    [InlineData("#5 DNLOAD 2: 012C")]
    [InlineData("#5 DNLOAD_6: 010203040506")]
    [InlineData("#5 BUILD_CHKSUM 1024 bytes")]
    [InlineData("#5 GET_DAQ_SIZE list 3")]
    [InlineData("#5 SET_DAQ_PTR list 3 odt 2 element 1")]
    [InlineData("#5 WRITE_DAQ 2 @ 0x400017DA")]
    [InlineData("#5 START_STOP Prepare list 3 last odt 1 event 6 prescaler 2")]
    [InlineData("#5 START_STOP_ALL start")]
    [InlineData("#5 DISCONNECT end session")]
    public void DescribesEveryCommandTheMasterSends(string expected)
    {
        byte[] cro = expected.Split(' ')[1] switch
        {
            "SET_MTA0" => CcpFrame.SetMta(5, 0x40008654),
            "UPLOAD" => CcpFrame.Upload(5, 5),
            "SHORT_UP" => CcpFrame.ShortUpload(5, 0x40001764, 2),
            "DNLOAD" => CcpFrame.Download(5, [0x01, 0x2C]),
            "DNLOAD_6:" => CcpFrame.Download6(5, [1, 2, 3, 4, 5, 6]),
            "BUILD_CHKSUM" => CcpFrame.BuildChecksum(5, 1024),
            "GET_DAQ_SIZE" => CcpFrame.GetDaqSize(5, CcpTarget.T6, 3),
            "SET_DAQ_PTR" => CcpFrame.SetDaqPointer(5, CcpTarget.T6, 3, 2, 1),
            "WRITE_DAQ" => CcpFrame.WriteDaq(5, 0x400017DA, 2),
            "START_STOP" => CcpFrame.StartStop(5, CcpTarget.T6, CcpStartStopMode.Prepare, 3, 1, 6, 2),
            "START_STOP_ALL" => CcpFrame.StartStopAll(5, true),
            _ => CcpFrame.Disconnect(5),
        };
        Assert.Equal(expected, CcpTrace.Describe(new CcpTraceEntry(1, DateTime.Now, CcpTraceKind.Command, cro, null, null)));
    }

    [Fact]
    public async Task ReportsDaqAllocationsWithOwnersAndTraffic()
    {
        await _master.ConnectAsync();
        var logger = await _master.StartDaqAsync([new CcpDaqListRequest(0, 1, [[new CcpDaqElement(0x40001000, 2)]])], "High-Speed Log");
        await _master.StartDaqAsync([new CcpDaqListRequest(6, 2, [[new CcpDaqElement(0x40001004, 1)], [new CcpDaqElement(0x40001008, 4)]])], "Variable watch");
        for (int i = 0; i < 300 && logger.SamplesFor(0) < 5; i++) await Task.Delay(10);

        var allocations = _master.GetDaqAllocations();
        Assert.Equal([(0, "High-Speed Log", 0, 1, 1, (byte)0), (1, "Variable watch", 6, 2, 2, (byte)12)],
            allocations.Select(a => (a.SlaveList, a.Owner, a.EventChannel, a.Prescaler, a.Odts, a.FirstPid)));
        Assert.True(allocations[0].Samples >= 5);
        Assert.Equal(1, allocations[0].Gaps);
        Assert.True(_master.GetStatistics().DaqFrames >= 5);

        await logger.StopAsync();
        Assert.Equal(["Variable watch"], _master.GetDaqAllocations().Select(a => a.Owner));
    }
}
