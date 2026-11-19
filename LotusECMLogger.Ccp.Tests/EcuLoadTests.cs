using System.Buffers.Binary;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>Phase 6g: the ECU's own load statistics, by build, streamed and summarised on the Tuning page.</summary>
public sealed class EcuLoadTests
{
    private static readonly EcuLoadMap Gt430 = EcuLoadMap.For("C132E0278")!;

    /// <summary>A source holding fixed raw values by address, as a recording or the watch would give them.</summary>
    private sealed class Values : IRawValueSource
    {
        private readonly Dictionary<(uint, int), ulong> values = [];
        public Values Set(uint address, int size, ulong bits) { values[(address, size)] = bits; return this; }
        public ulong? Bits(uint address, int size) => values.TryGetValue((address, size), out var bits) ? bits : null;
    }

    public static TheoryData<string> Builds() => [.. EcuLoadMap.Known.Select(m => m.Build)];

    /// <summary>Every address lies in a catalog symbol of the build, of the size read there (catches typos and drift).</summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void EveryAddressIsASymbolOfTheBuildsCatalog(string build)
    {
        var map = EcuLoadMap.For(build)!;
        var catalog = SymbolCatalogLoader.LoadByName(build);
        Assert.NotNull(catalog);
        var reads = map.Channels().Select(c => (c.Channel.Name, c.Channel.Address, (int)c.Channel.Size))
            .Append(("reset request", map.StatsResetRequest, 1));
        foreach (var (name, address, size) in reads)
        {
            var symbol = catalog.Entries.FirstOrDefault(e =>
                e.Address <= address && (ulong)address + (ulong)size <= e.Address + (ulong)Math.Max(1, e.Type.Size) * (ulong)Math.Max(1, e.Type.ArrayLength));
            Assert.True(symbol is not null, $"{build}: {name} at 0x{address:X8} is in no catalog symbol");
            Assert.True(symbol.Type.Size == size, $"{build}: {name} reads {size} byte(s) at 0x{address:X8}, but {symbol.Name} is {symbol.Type.Size}");
        }
    }

    [Fact]
    public void TheChannelsFitTwoDaqLists()
    {
        var plan = HighSpeedLogPlanner.Plan(Gt430.Channels().Select(c => (c.Channel, c.RateHz)).ToList(), CcpTarget.T6);
        Assert.Equal([(EcuLoadMap.FastRateHz, 8), (EcuLoadMap.SlowRateHz, 12)], plan.Groups.Select(g => (g.RateHz, g.Frames.Count)));
    }

    [Fact]
    public void ReadsTimesInMicrosecondsAndTheStackFromItsScanPointer()
    {
        var source = new Values()
            .Set(Gt430.MainLoopLast, 4, 19_200).Set(Gt430.MainLoopMax, 4, 34_100)
            .Set(Gt430.SlotLastAddress(0), 4, 1_180).Set(Gt430.SlotMaxAddress(0), 4, 2_860)
            .Set(Gt430.SlotMaxAddress(EcuLoadMap.SlotCount), 4, 5_410).Set(Gt430.IsrPeriodLast, 4, 5_000)
            .Set(Gt430.StackPeakAddress, 4, 0x4000FB30).Set(Gt430.StackPeakFound, 1, 1)
            .Set(Gt430.CcpTxStalls, 1, 3).Set(Gt430.SyncLosses, 1, 0);
        var sample = EcuLoadSample.Read(source, Gt430);

        Assert.Equal(1920, sample.MainLoopUs!.Value, 6);
        Assert.Equal(3410, sample.MainLoopMaxUs!.Value, 6);
        Assert.Equal(118, sample.SlotLastUs[0]!.Value, 6);
        Assert.Equal(286, sample.IsrMaxUs!.Value, 6);
        Assert.Equal(541, sample.IsrPeriodMaxUs!.Value, 6);
        Assert.Null(sample.SlotLastUs[1]); // not streamed
        Assert.Equal(0x4D0, sample.StackUsedBytes); // 0x40010000 - 0x4000FB30
        Assert.True(sample.HasAny);
    }

    [Fact]
    public void TheStackIsNotShownUntilTheScanHasFoundIt()
    {
        var source = new Values().Set(Gt430.StackPeakAddress, 4, 0x4000F230).Set(Gt430.StackPeakFound, 1, 0);
        var sample = EcuLoadSample.Read(source, Gt430);
        Assert.Null(sample.StackUsedBytes);
        Assert.False(sample.HasAny);
        Assert.Contains("stack: measuring", EcuLoadText.Segments(sample, (null, null)).Select(s => s.Text));
    }

    [Theory]
    [InlineData(200, EcuLoadLevel.Normal)]
    [InlineData(251, EcuLoadLevel.Warning)]
    [InlineData(500, EcuLoadLevel.Fault)]
    public void InterruptTimeIsJudgedAgainstItsBudget(double maxUs, EcuLoadLevel level) =>
        Assert.Equal(level, EcuLoadSample.IsrLevel(maxUs));

    [Theory]
    [InlineData(540, EcuLoadLevel.Normal)]
    [InlineData(560, EcuLoadLevel.Warning)]
    [InlineData(1000, EcuLoadLevel.Fault)]
    public void ALateTickIsAWarningAndAMissedOneAFault(double maxUs, EcuLoadLevel level) =>
        Assert.Equal(level, EcuLoadSample.IsrPeriodLevel(maxUs));

    [Theory]
    [InlineData(2048, EcuLoadLevel.Normal)]
    [InlineData(3072, EcuLoadLevel.Warning)]
    [InlineData(3900, EcuLoadLevel.Fault)]
    [InlineData(4200, EcuLoadLevel.Fault)] // below the sentinel area: overflowed
    [InlineData(null, EcuLoadLevel.Normal)]
    public void StackUseIsJudgedAgainstItsArea(int? used, EcuLoadLevel level) => Assert.Equal(level, EcuLoadSample.StackLevel(used));

    [Fact]
    public void CountersAreIncreasesSinceWatchingBeganAndWrapAt256()
    {
        var counters = new EcuLoadCounters();
        EcuLoadSample With(int stalls, int sync) =>
            EcuLoadSample.Read(new Values().Set(Gt430.CcpTxStalls, 1, (ulong)stalls).Set(Gt430.SyncLosses, 1, (ulong)sync), Gt430);
        Assert.Equal((0, 0), counters.Since(With(250, 7)));
        Assert.Equal((10, 1), counters.Since(With(4, 8)));
        counters.Reset();
        Assert.Equal((0, 0), counters.Since(With(4, 8)));
    }

    [Fact]
    public void TheSummaryColoursWhatNeedsAttention()
    {
        var source = new Values()
            .Set(Gt430.MainLoopLast, 4, 19_200).Set(Gt430.MainLoopMax, 4, 34_100)
            .Set(Gt430.SlotLastAddress(0), 4, 1_180).Set(Gt430.SlotMaxAddress(0), 4, 3_000)
            .Set(Gt430.SlotMaxAddress(EcuLoadMap.SlotCount), 4, 5_410)
            .Set(Gt430.StackPeakAddress, 4, 0x4000FB30).Set(Gt430.StackPeakFound, 1, 1);
        var segments = EcuLoadText.Segments(EcuLoadSample.Read(source, Gt430), (2, 0));
        Assert.Equal(
        [
            ("loop 1.92 ms (max 3.41)", EcuLoadLevel.Normal),
            ("ISR 118 µs (max 300 of 500)", EcuLoadLevel.Warning),
            ("tick late max +41 µs", EcuLoadLevel.Normal),
            ("stack 1.2 of 4 KB", EcuLoadLevel.Normal),
            ("CCP stalls 2", EcuLoadLevel.Warning),
            ("sync losses 0", EcuLoadLevel.Normal),
        ], segments.Select(s => (s.Text.Replace(',', '.'), s.Level)));
    }

    [Fact]
    public async Task StreamsTheFiguresOverItsOwnDaqLists()
    {
        using var slave = new FakeCcpSlave();
        slave.Load(Gt430.MainLoopLast, BigEndian(19_200));
        slave.Load(Gt430.SlotMaxAddress(0), BigEndian(2_860));
        slave.Load(Gt430.StackPeakAddress, BigEndian(0x4000FB30));
        slave.Load(Gt430.StackPeakFound, [1]);
        using var master = new CcpMaster(new SimulatedCcpTransport(slave, speedFactor: 50),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromSeconds(1) });
        await master.ConnectAsync();
        await using var monitor = await EcuLoadMonitor.StartAsync(master, Gt430);

        EcuLoadSample? sample = null;
        for (int i = 0; i < 500 && sample?.IsrMaxUs is null; i++)
        {
            await Task.Delay(10);
            sample = monitor.Read();
        }
        Assert.NotNull(sample);
        Assert.Equal(1920, sample.MainLoopUs!.Value, 6);
        Assert.Equal(286, sample.IsrMaxUs!.Value, 6);
        Assert.Equal(0x4D0, sample.StackUsedBytes);
        Assert.Equal(2, master.GetDaqAllocations().Count(a => a.Owner == EcuLoadMonitor.OwnerName));
    }

    [Fact]
    public async Task ResetMaximaWritesOnlyTheRequestByte()
    {
        using var slave = new FakeCcpSlave();
        using var master = new CcpMaster(new SimulatedCcpTransport(slave, speedFactor: 50),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromSeconds(1) });
        await master.ConnectAsync();
        await EcuLoadMonitor.ResetMaximaAsync(master, Gt430);
        Assert.Equal(1, slave.Peek(Gt430.StatsResetRequest));
        Assert.Equal(0, slave.Peek(Gt430.StatsResetRequest + 1));
    }

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
