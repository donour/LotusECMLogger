using System.Globalization;
using System.Text;
using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;
using LotusECMLogger.Services.Logging;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>Recording a tuning session and reading it back for replay: the log format, the recorder, cell activity.</summary>
public sealed class TuningRecordingTests : IDisposable
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private const string Fan = "CAL_cooling_fan_afterrun"; // 8 × 8: X temp_engine_air, Y coolant_temp
    private const uint AirTemp = 0x4000183A, CoolantTemp = 0x40001836;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tuning-recording-" + Guid.NewGuid().ToString("N"));

    public TuningRecordingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static readonly Lazy<A2lResolvedModule> A2l = new(() => A2lLayout.Resolve(A2lParser.ParseFile(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LotusECMLogger", "config", "a2l", "C132E0278.a2l")))));

    /// <summary>
    /// The fake ECU's calibration (its CCP enable byte set) with fan table axes X 40, 60 … 180 and Y 0, 10 … 70 (raw).
    /// </summary>
    private static byte[] FanCalibration()
    {
        using var defaults = new FakeCcpSlave();
        var bytes = Enumerable.Range(0, (int)Gt430.CalibrationSize).Select(i => defaults.Peek(Gt430.CalibrationBase + (uint)i)).ToArray();
        Encoding.ASCII.GetBytes("C132E0278 test calibration").CopyTo(bytes, 0);
        void Axis(string name, Func<int, int> point)
        {
            var axis = A2l.Value.Items[name];
            for (int i = 0; i < axis.CountX; i++) bytes[axis.CellAddress(i, 0) - Gt430.CalibrationBase] = (byte)point(i);
        }
        Axis(Fan + "_X_air_temp", i => 40 + 20 * i);
        Axis(Fan + "_Y_coolant_temp", i => 10 * i);
        return bytes;
    }

    // ── The log's channel lines ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChannelLinesRoundTripAndOldOnesAreRead()
    {
        var channel = new HighSpeedChannel { Name = "coolant_temp", Address = CoolantTemp, Size = 1, Scale = 0.625, Offset = -40, Unit = "°C" };
        string note = LoggedChannel.From(channel, 20).ToNote();
        Assert.Equal("channel: coolant_temp @0x40001836 size=1 signed=0 scale=0.625 offset=-40 rate=20Hz unit=°C", note);
        Assert.Equal(LoggedChannel.From(channel, 20), LoggedChannel.Parse(note));

        var old = LoggedChannel.Parse("engine_speed: 0x40001762 size=2 100Hz unit=rpm")!;
        Assert.Equal(("engine_speed", 0x40001762u, 2, 100, "rpm"), (old.Name, old.Address, old.Size, old.RateHz, old.Unit));
        Assert.False(old.IsExact);                         // its scaling is not known
        Assert.Null(LoggedChannel.Parse("Started: 2026-11-05T10:00:00.000000-05:00"));
    }

    [Theory]
    [InlineData(1, false, 0.625, -40, -21.25, 30UL)]       // a scaled byte
    [InlineData(2, true, 1, 0, -5, 0xFFFBUL)]              // a negative word, two's complement
    [InlineData(2, false, 0.25, 0, 1234.5, 4938UL)]
    [InlineData(1, false, 1, 0, 300, null)]                // out of range for a byte
    public void ColumnValuesTurnBackIntoRawBits(int size, bool signed, double scale, double offset, double value, ulong? bits)
    {
        var channel = new LoggedChannel("x", 0x40000000, size, signed, scale, offset, 20, "");
        Assert.Equal(bits, channel.BitsOf(value));
        if (bits is ulong b) Assert.Equal(Math.Round((value - offset) / scale), RawValues.ToRaw(b, size, signed));
    }

    // ── Recording against the simulated ECU ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordsInputsWritesAndBothCalibrations()
    {
        using var slave = new FakeCcpSlave();
        var calibration = FanCalibration();
        slave.Load(Gt430.CalibrationBase, calibration);
        slave.Load(AirTemp, [90]);      // halfway between X points 2 and 3
        slave.Load(CoolantTemp, [30]);  // on Y point 3
        SimulatedCcpTransport? transport = null;
        using var host = new CcpSessionHost(() => transport = new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(500) });
        await using var lease = await host.AcquireAsync();
        var master = lease.Master;
        var image = CalibrationImage.FromBytes(calibration, Gt430);
        await using var session = await LiveTuningSession.StartAsync(master, image);
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);

        string log = Path.Combine(_directory, "run.csv");
        var recorder = await TuningRecorder.StartAsync(host, master, A2l.Value, Gt430, [], log, session);
        Assert.Equal(recorder.Channels.Count, recorder.InputCount);
        await Until(() => recorder.Bits(AirTemp, 1) == 90 && recorder.Bits(CoolantTemp, 1) == 30, "the inputs to stream");
        Assert.Contains(TuningRecorder.OwnerName, master.GetDaqAllocations().Select(a => a.Owner));

        // The recording is the cursor's source: no watch of its own.
        var fan = A2l.Value.Items[Fan];
        var inputs = TableInputs.For(A2l.Value.Module, fan);
        Assert.Equal(new OperatingPointSample(new AxisPosition(2, 0.5), new AxisPosition(3, 0), 90, 30), inputs.Sample(recorder, image));

        // A live edit is written, so it is recorded; then the ECU's input moves.
        image.SetRaw(fan, [new CellValue(5, 3, 77)]);
        await session.WhenIdleAsync();
        await Until(() => recorder.EditCount == 1, "the write to be recorded");
        transport!.With(s => s.Load(AirTemp, [140]));   // on X point 5
        await Until(() => recorder.Bits(AirTemp, 1) == 140, "the new input");
        await Task.Delay(100);

        var result = await recorder.StopAsync(master);
        Assert.Null(result.CalibrationEndProblem);
        Assert.Equal(1, result.Edits);
        Assert.DoesNotContain(TuningRecorder.OwnerName, master.GetDaqAllocations().Select(a => a.Owner));

        var recording = TuningRecording.Load(log);
        Assert.Equal("C132E0278", recording.Build);
        Assert.Equal(calibration, recording.CalibrationStart);
        int cell = (int)(fan.CellAddress(5, 3) - Gt430.CalibrationBase);
        Assert.Equal(77, recording.CalibrationEnd![cell]);
        var edit = Assert.Single(recording.Edits);
        Assert.Equal(fan.CellAddress(5, 3), edit.Address);
        Assert.Equal([0], edit.Before);
        Assert.Equal([77], edit.After);
        Assert.False(edit.Found);
        Assert.Contains($"{Fan}[5,3]", edit.Items);
        // The fake ECU stamps frames with a counter, not microseconds, so only the order is checked: after the first sample.
        Assert.True(edit.Time >= recording.SeriesOf(AirTemp, 1)!.Times[0] && edit.Time <= recording.Duration);

        var air = recording.SeriesOf(AirTemp, 1)!;
        Assert.Equal([90UL, 140UL], Enumerable.Range(0, air.Count).Select(i => air.At(air.Times[i])!.Value));
        Assert.Equal(30UL, recording.SeriesOf(CoolantTemp, 1)!.At(recording.Duration));

        // Replay: the cell holds 0 before the write and 77 after it.
        var replay = new ReplayCalibration(CalibrationImage.FromBytes(recording.CalibrationStart!, Gt430), recording.Edits);
        replay.SeekTo(edit.Time - 1e-6);
        Assert.Equal(0, replay.Image.ReadRaw(fan, 5, 3));
        replay.SeekTo(edit.Time);
        Assert.Equal(77, replay.Image.ReadRaw(fan, 5, 3));
        replay.SeekTo(0);
        Assert.Equal(0, replay.Image.ReadRaw(fan, 5, 3));

        // The fan table was read between X points 2 and 3, then at point 5, always at Y point 3.
        var activity = CellActivity.Compute(recording, inputs, recording.CalibrationStart!, Gt430, 0, recording.Duration, out var problem)!;
        Assert.Null(problem);
        Assert.True(activity.At(2, 3) > 0 && activity.At(3, 3) > 0 && activity.At(5, 3) > 0);
        Assert.Equal(activity.Covered, activity.Seconds.Cast<double>().Sum(), 9);
        Assert.Equal(activity.At(2, 3), activity.At(3, 3), 9);
        Assert.Equal(0, activity.At(4, 3));
    }

    [Fact]
    public async Task TheLiveSyncCopyStandsInForReadingTheCalibration()
    {
        using var slave = new FakeCcpSlave();
        var calibration = FanCalibration();
        slave.Load(Gt430.CalibrationBase, calibration);
        using var host = new CcpSessionHost(() => new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(500) });
        await using var lease = await host.AcquireAsync();
        var master = lease.Master;
        await using var session = await LiveTuningSession.StartAsync(master, CalibrationImage.FromBytes(calibration, Gt430));

        long before = master.GetStatistics().Commands;
        Assert.Equal(calibration, await TuningRecorder.CalibrationSnapshotAsync(master, Gt430, session));
        long withSession = master.GetStatistics().Commands - before;
        Assert.Equal(calibration, await TuningRecorder.CalibrationSnapshotAsync(master, Gt430, null));
        long withoutSession = master.GetStatistics().Commands - before - withSession;
        Assert.True(withSession * 10 < withoutSession, $"{withSession} commands with live sync's copy, {withoutSession} reading it");
    }

    // ── Cell activity from a recording written by hand ───────────────────────────────────────────────────

    [Fact]
    public void CellActivitySharesTimeAsTheLookupInterpolatesOnTheAxesOfTheMoment()
    {
        // 0–2 s at 90 °C raw (X 2.5), then 140 (X point 5), all at coolant raw 30 (Y point 3). At 3 s the X axis's
        // point 5 is raised from 140 to 150, so from then on 140 lies two thirds of the way from point 4 (120).
        string log = Path.Combine(_directory, "hand.csv");
        var origin = DateTime.Parse("2026-11-05T10:00:00.000000-05:00", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        string Row(double seconds, int air) => string.Create(CultureInfo.InvariantCulture,
            $"{origin.AddSeconds(seconds).ToString(CsvSampleSink.TimestampFormat, CultureInfo.InvariantCulture)},{seconds * 1000:F3},0,{air},-21.25");
        File.WriteAllLines(log,
        [
            "# Lotus High-Speed CAN Channel Log",
            "# build: C132E0278",
            $"# channel: temp_engine_air @0x{AirTemp:X8} size=1 signed=0 scale=1 offset=0 rate=20Hz unit=raw",
            $"# channel: coolant_temp @0x{CoolantTemp:X8} size=1 signed=0 scale=0.625 offset=-40 rate=20Hz unit=°C",
            "Timestamp,RelativeTime_ms,Label,temp_engine_air,coolant_temp",
            Row(0, 90), Row(1, 90), Row(2, 140), Row(4, 140),
        ]);
        var axisPoint5 = A2l.Value.Items[Fan + "_X_air_temp"].CellAddress(5, 0);
        using (var edits = CalibrationEditLog.Create(Path.Combine(_directory, "hand.edits.csv"), "hand.csv"))
            edits.Append(new CalibrationWrittenEventArgs(axisPoint5, [140], [150], origin.AddSeconds(3), found: false), "axis");

        var recording = TuningRecording.Load(log);
        Assert.Equal(4, recording.Duration, 9);
        Assert.Equal(3, Assert.Single(recording.Edits).Time, 6);
        var inputs = TableInputs.For(A2l.Value.Module, A2l.Value.Items[Fan]);
        var activity = CellActivity.Compute(recording, inputs, FanCalibration(), Gt430, 0, 4, out _)!;

        Assert.Equal(4, activity.Covered, 9);
        Assert.Equal(1, activity.At(2, 3), 6);              // 0–2 s, half each side of 90
        Assert.Equal(1, activity.At(3, 3), 6);
        Assert.Equal(1 + 2.0 / 3, activity.At(5, 3), 6);    // 2–3 s on point 5, then two thirds of 3–4 s
        Assert.Equal(1.0 / 3, activity.At(4, 3), 6);
        Assert.Equal(1 + 2.0 / 3, activity.Max, 6);

        // A range of the recording counts only its own time.
        var late = CellActivity.Compute(recording, inputs, FanCalibration(), Gt430, 3, 4, out _)!;
        Assert.Equal(1, late.Covered, 9);
        Assert.Equal(2.0 / 3, late.At(5, 3), 9);
        Assert.Equal(1.0 / 3, late.At(4, 3), 9);
    }

    [Fact]
    public void TablesWithoutRecordedInputsSayWhy()
    {
        string log = Path.Combine(_directory, "bare.csv");
        File.WriteAllLines(log, ["# build: C132E0278", "Timestamp,RelativeTime_ms,Label", "2026-11-05T10:00:00.000000-05:00,0.000,0"]);
        var recording = TuningRecording.Load(log);
        var inputs = TableInputs.For(A2l.Value.Module, A2l.Value.Items[Fan]);
        Assert.Null(CellActivity.Compute(recording, inputs, FanCalibration(), Gt430, 0, 1, out var problem));
        Assert.Contains("was not recorded", problem);
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting for {what}.");
            await Task.Delay(10);
        }
    }
}
