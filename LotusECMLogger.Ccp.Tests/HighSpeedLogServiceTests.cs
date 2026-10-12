using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class HighSpeedLogServiceTests : IDisposable
{
    private static readonly HighSpeedChannel Rpm = new() { Name = "RPM", Address = 0x400017DA, Size = 2, Scale = 0.25, Unit = "rpm" };
    private static readonly HighSpeedChannel Coolant = new() { Name = "Coolant", Address = 0x400018AE, Size = 1, Offset = -40, Unit = "C" };
    private static readonly HighSpeedChannel Load = new() { Name = "Load", Address = 0x40001900, Size = 4, Signed = true };
    private static readonly (HighSpeedChannel, int)[] Selection = [(Rpm, 100), (Coolant, 100), (Load, 50)];
    private readonly LoggerHarness _harness = new();

    public HighSpeedLogServiceTests()
    {
        _harness.Slave.Load(Rpm.Address, [0x1F, 0x40]);          // 8000 × 0.25 = 2000 rpm
        _harness.Slave.Load(Coolant.Address, [0x80]);            // 128 − 40 = 88 °C
        _harness.Slave.Load(Load.Address, [0xFF, 0xFF, 0xFF, 0xFE]); // −2
    }

    public void Dispose() => _harness.Dispose();

    private ConcurrentQueue<HighSpeedSampleEventArgs> Collect()
    {
        var events = new ConcurrentQueue<HighSpeedSampleEventArgs>();
        _harness.Service.DataReceived += (_, e) => events.Enqueue(e);
        return events;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task StreamsDecodedChannelsToEventsAndTheCsv()
    {
        var events = Collect();
        _harness.RunUntilStreaming(Selection);
        await Eventually(() => events.Any(e => e.Label == 0) && events.Any(e => e.Label == 12));
        _harness.Service.StopLogging();

        var fast = events.First(e => e.Label == 0);
        Assert.Equal([("RPM", 2000.0, "rpm"), ("Coolant", 88.0, "C")], fast.Readings.Select(r => (r.Name, r.Value, r.Unit)));
        Assert.Equal(-2.0, Assert.Single(events.First(e => e.Label == 12).Readings).Value);
        Assert.All(events, e => Assert.False(e.Overflow));

        var (header, rows) = _harness.ReadCsv();
        Assert.Equal(["Timestamp", "RelativeTime_ms", "Label", "RPM", "Coolant", "Load"], header);
        Assert.Contains(rows, r => r[2] == "0");
        Assert.Contains(rows, r => r[2] == "12");
        var last = rows[^1];
        Assert.Equal(["2000", "88", "-2"], last[3..]);
        var times = rows.Select(r => double.Parse(r[1], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(times.Order(), times); // hardware timestamps, monotonic
        Assert.Equal(0, _harness.Service.DroppedFrames);
    }

    [Fact]
    public void StoppingStopsOnlyItsListsThenEndsTheSession()
    {
        _harness.RunUntilStreaming(Selection);
        _harness.Service.StopLogging();
        Assert.False(_harness.Service.IsLogging);
        var tail = _harness.Script!.Sent.TakeLast(3).Select(c => (c[0], c[2], c[3])).ToArray();
        Assert.Equal([(0x06, 0, 0), (0x06, 0, 1), ((byte)0x07, (byte)1, (byte)0)], tail);
    }

    [Fact]
    public async Task SharesTheSessionWithOtherLeaseHolders()
    {
        await using var ccpTab = await _harness.Host.AcquireAsync();
        _harness.RunUntilStreaming(Selection);
        Assert.Same(ccpTab.Master, _harness.Host.Master);
        Assert.Equal("Ccp_T6", (await ccpTab.Master.ExchangeIdAsync()).SlaveId); // memory access while logging
        _harness.Service.StopLogging();

        Assert.Equal(1, _harness.TransportsOpened);
        Assert.Equal(1, _harness.Script!.Sent.Count(c => c[0] == (byte)CcpCommandCode.Connect));
        Assert.DoesNotContain(_harness.Script.Sent, c => c[0] == (byte)CcpCommandCode.Disconnect); // the tab still holds it
        Assert.Equal(CcpSessionState.Connected, ccpTab.Master.State);
        Assert.Equal("Ccp_T6", _harness.Service.Identify().SlaveId); // the probe joins the open session too
        Assert.Equal(1, _harness.TransportsOpened);
    }

    [Fact]
    public async Task MergesAemWidebandReadingsIntoTheLog()
    {
        // λ = (2/65536)·0x8000 = 1.0; the gauge answers every poll.
        byte[] reply = [0x06, 0x41, 0x24, 0x80, 0x00, 0x80, 0x00, 0x00];
        _harness.Slave.AuxiliarySent += (canId, _) => { if (canId == AemWidebandSource.RequestId) _harness.Slave.InjectFrame(AemWidebandSource.ResponseId, reply); };
        var events = Collect();
        _harness.RunUntilStreaming(Selection, pollAem: true);
        await Eventually(() => events.Any(e => e.Readings.Any(r => r.Name == "AEM Lambda")));
        await Task.Delay(200); // let a few stream rows carry the AEM values
        _harness.Service.StopLogging();

        var aem = events.First(e => e.Readings.Any(r => r.Name == "AEM Lambda")).Readings;
        Assert.Equal([("AEM Lambda", 1.0), ("AEM AFR", 14.7)], aem.Select(r => (r.Name, Math.Round(r.Value, 6))));
        var (header, rows) = _harness.ReadCsv();
        Assert.Equal(["AEM Lambda", "AEM AFR"], header[^2..]);
        Assert.Equal(["1", "14.7"], rows[^1][^2..]);
        Assert.Contains("AEM UEGO: OBD Mode 01 PID 0x24 @ 0x7E1", File.ReadAllText(_harness.CsvPath));
    }

    [Fact]
    public async Task SilentEcuReportsAnErrorAndStopsCleanly()
    {
        var profile = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        _harness.Slave.Load(profile.EnableByteAddress!.Value, [2]); // CCP on the other bus
        string? error = null;
        _harness.Service.ErrorOccurred += (_, message) => error = message;
        _harness.Service.StartLogging(Selection, _harness.CsvPath);
        await Eventually(() => error is not null);
        Assert.Contains("No CCP reply to Connect", error);
        Assert.False(_harness.Service.IsLogging);
        _harness.Service.StopLogging();
        Assert.Empty(_harness.ReadCsv().Rows);
    }

    [Fact]
    public void RejectsASecondStartAndIdentifyWhileLogging()
    {
        _harness.RunUntilStreaming(Selection);
        Assert.Throws<InvalidOperationException>(() => _harness.Service.StartLogging(Selection, _harness.CsvPath));
        Assert.Throws<InvalidOperationException>(() => _harness.Service.Identify());
        _harness.Service.StopLogging();
    }

    [Fact]
    public void IdentifyReportsTheChannelLoggerAndTheBuild()
    {
        var profile = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        _harness.Slave.Load(profile.CalibrationBase, Encoding.ASCII.GetBytes("C132E0278 "));
        _harness.Slave.Load(profile.CalibrationBase + 0x59D4, Encoding.ASCII.GetBytes("LOTUS EVORA GT430 MAN MY19 ROW"));
        var result = _harness.Service.Identify();
        Assert.True(result.IsChannelLogger);
        Assert.Equal(HighSpeedIdentifyResult.ChannelLoggerMagic, result.Magic);
        Assert.Equal((6, "Ccp_T6", "2.1"), (result.FirmwareIdLength, result.SlaveId, result.CcpVersion));
        Assert.Equal(("C132E0278", "LOTUS EVORA GT430 MAN MY19 ROW"), (result.CalibrationId, result.CalibrationDescription));
        var last = _harness.Script!.Sent[^1];
        Assert.Equal(((byte)CcpCommandCode.Disconnect, (byte)1), (last[0], last[2])); // leaves the ECU as found
    }

    [Fact]
    public void IdentifyWithoutAReplyReportsNoSession()
    {
        var profile = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        _harness.Slave.Load(profile.EnableByteAddress!.Value, [0]);
        var result = _harness.Service.Identify();
        Assert.False(result.SessionOpened);
        Assert.False(result.IsChannelLogger);
    }

    [Fact]
    public void HardwareClockUnwrapsRolloverButNotReordering()
    {
        var clock = new HighSpeedLogService.HardwareClock();
        Assert.Equal(0ul, clock.At(0xFFFFFF00).ElapsedUs);
        Assert.Equal(0x110ul, clock.At(0x10).ElapsedUs);  // the 32-bit counter wrapped
        Assert.Equal(0x100ul, clock.At(0x00).ElapsedUs);  // a slightly older frame from the other stream
        Assert.Equal(0x120ul, clock.At(0x20).ElapsedUs);
    }
}
