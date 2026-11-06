using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Text;
using LotusECMLogger.Controls;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Tests;

/// <summary>
/// Drives the real CCP tab against the simulated T6 inside a real message loop: connect and identify, read and
/// write memory, watch a variable, follow the traffic view, run the self-test and disconnect. Set
/// CCP_UI_RENDER_DIR to also save a PNG of each sub-tab.
/// </summary>
public class CcpControlUiTests
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");

    [Fact]
    public void TheCcpTabDrivesASimulatedSessionEndToEnd() => RunInMessageLoop(async form =>
    {
        using var slave = new FakeCcpSlave();
        uint cal = Gt430.CalibrationBase;
        slave.Load(cal, Encoding.ASCII.GetBytes("C132E0278 test calibration"));
        slave.Load(cal + 0x59D4, Encoding.ASCII.GetBytes("LOTUS EVORA GT430 MAN MY19 ROW"));
        slave.Load(Gt430.SymbolEntriesAddress!.Value - 44, Registry((0x40001762, 2), (0x40001924, 12)));
        slave.Load(0x40001924, [0xFF, 0xF8]); // ignition advance, cylinder 1: -2 degrees

        SimulatedCcpTransport? transport = null;
        using var host = new CcpSessionHost(() => transport = new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(300) });
        string backups = Path.Combine(Path.GetTempPath(), "ccp-ui-backups-" + Guid.NewGuid().ToString("N"));
        var control = new CcpControl(host) { Dock = DockStyle.Fill, BackupDirectory = backups };
        string? confirmation = null;
        control.ConfirmWrite = text => { confirmation = text; return true; };
        form.Controls.Add(control);
        var log = Named<TextBox>(control, "logTextBox");
        Task Until(Func<bool> condition, string what, int seconds = 15) =>
            WaitUntil(() => !control.IsBusy && condition(), what, log, seconds);

        // Connect and identify.
        Named<Button>(control, "connectButton").PerformClick();
        await Until(() => control.Identification?.Build is not null, "identification");
        Assert.Contains(Named<ListView>(control, "infoListView").Items.Cast<ListViewItem>(), i => i.SubItems[1].Text == "C132E0278");

        // Read calibration RAM into the hex view.
        Named<TextBox>(control, "addressTextBox").Text = $"0x{cal:X8}";
        Named<NumericUpDown>(control, "lengthUpDown").Value = 32;
        Named<Button>(control, "readButton").PerformClick();
        await Until(() => Named<TextBox>(control, "hexTextBox").Text.Contains("C132E0278"), "the memory read");

        // A guarded write, confirmed through the seam.
        Named<TextBox>(control, "addressTextBox").Text = $"0x{cal + 0x100:X8}";
        Named<TextBox>(control, "bytesTextBox").Text = "AA 55";
        Named<Button>(control, "writeValueButton").PerformClick();
        await Until(() => log.Text.Contains("Wrote and verified"), "the write");
        Assert.Contains("engine speed reads 0 rpm", confirmation);
        Assert.Single(Directory.GetFiles(backups, "ccp-backup_*.bin"));
        Directory.Delete(backups, recursive: true);
        Assert.Equal([0xAA, 0x55], transport!.With(s => new[] { s.Peek(cal + 0x100), s.Peek(cal + 0x101) }));

        // Variables: load, find, watch live.
        var tabs = Named<TabControl>(control, "subTabs");
        tabs.SelectedTab = tabs.TabPages["variablesPage"];
        ButtonWithText(control, "Load Variables").PerformClick();
        await Until(() => log.Text.Contains("Variables:"), "the variable list");
        Find<TextBox>(control, t => t.PlaceholderText.StartsWith("search")).Text = "obd_ii_ign_adv_per_cylinder[0]";
        var variables = ListViewWithColumns(control, "Name", "Address");
        Assert.Equal(1, variables.VirtualListSize);
        variables.SelectedIndices.Add(0);
        ButtonWithText(control, "Add to Watch →").PerformClick();
        ButtonWithText(control, "Start Watch").PerformClick();
        var watch = ListViewWithColumns(control, "Name", "Value");
        await Until(() => watch.Items.Count == 1 && watch.Items[0].SubItems[1].Text == "-2", "a watched value");
        ButtonWithText(control, "Stop Watch").PerformClick();
        await Until(() => log.Text.Contains("Watch stopped."), "the watch to stop");

        // Traffic: statistics and the decoded trace.
        tabs.SelectedTab = tabs.TabPages["trafficPage"];
        var trace = ListViewWithColumns(control, "#", "Time");
        await Until(() => trace.Items.Count > 0, "trace rows");
        Assert.Contains(trace.Items.Cast<ListViewItem>(), i => i.SubItems[3].Text.Contains("DNLOAD"));

        // Self-test: every check reports a row. The synthetic fake stamps DAQ with frame counts rather than time,
        // so the rate check itself is covered against the firmware model in the CCP tests.
        tabs.SelectedTab = tabs.TabPages["selfTestPage"];
        ButtonWithText(control, "Run Self-Test").PerformClick();
        var results = ListViewWithColumns(control, "Check", "Result");
        await Until(() => log.Text.Contains("Self-test:") && results.Items.Count == 9, "the self-test", seconds: 60);

        if (Environment.GetEnvironmentVariable("CCP_UI_RENDER_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                await Task.Delay(600); // let the traffic page's timer refresh once
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(dir, $"ccp_{page.Name}.png"));
            }
        }

        Named<Button>(control, "disconnectButton").PerformClick();
        await Until(() => host.LeaseCount == 0 && host.Master is null, "the session to close");
    });

    [Fact]
    public void TheTuningTabEditsATableAndKeepsTheEcuInStep() => RunInMessageLoop(async form =>
    {
        const string table = "CAL_injtip_transient_throttle_response_time"; // u8, 5 ms per count, 8 × 8
        using var slave = new FakeCcpSlave();
        uint cal = Gt430.CalibrationBase;
        slave.Load(cal, Encoding.ASCII.GetBytes("C132E0278 test calibration"));
        // CAL_cooling_fan_afterrun: X axis (air temp) 40, 60, … 180; Y axis (coolant) 0, 10, … 70, raw. The A2L binds
        // its inputs to temp_engine_air and coolant_temp, which the cursor streams over DAQ.
        slave.Load(A2lItem(Fan + "_X_air_temp").Address, Enumerable.Range(0, 8).Select(i => (byte)(40 + 20 * i)).ToArray());
        slave.Load(A2lItem(Fan + "_Y_coolant_temp").Address, Enumerable.Range(0, 8).Select(i => (byte)(10 * i)).ToArray());
        slave.Load(0x4000183A, [90]); // temp_engine_air: halfway between points 2 and 3
        slave.Load(0x40001836, [30]); // coolant_temp: on point 3
        string directory = Path.Combine(Path.GetTempPath(), "ccp-ui-tuning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stock.bin");
        File.WriteAllBytes(file, Enumerable.Range(0, (int)Gt430.CalibrationSize).Select(i => slave.Peek(cal + (uint)i)).ToArray());

        SimulatedCcpTransport? transport = null;
        using var host = new CcpSessionHost(() => transport = new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(300) });
        var control = new CcpControl(host) { Dock = DockStyle.Fill, BackupDirectory = Path.Combine(directory, "backups") };
        form.Controls.Add(control);
        var log = Named<TextBox>(control, "logTextBox");
        var panel = control.TuningPanel;
        var confirmations = new List<string>();
        panel.Confirm = text => { confirmations.Add(text); return true; };
        Task Until(Func<bool> condition, string what, int seconds = 15) =>
            WaitUntil(() => !control.IsBusy && condition(), what, log, seconds);
        byte Ecu(int x, int y) => transport!.With(s => s.Peek(0x40008A9A + (uint)(y * 8 + x)));
        // Button states follow a queued refresh, so wait for each to be enabled before clicking it.
        async Task Click(string text)
        {
            var button = ButtonWithText(control, text);
            await Until(() => button.Enabled && button.Visible, $"{text} to be shown and enabled");
            button.PerformClick();
        }

        Named<Button>(control, "connectButton").PerformClick();
        await Until(() => control.Identification?.Build is not null, "identification");
        var tabs = Named<TabControl>(control, "subTabs");
        var header = Named<Panel>(control, "connectionPanel");
        int fullHeader = header.Height;
        tabs.SelectedTab = tabs.TabPages["tuningPage"];
        // The Tuning page takes the room: a one-row header and a one-line log strip.
        Assert.True(header.Height < fullHeader / 2);
        Assert.False(Named<ListView>(control, "infoListView").Visible);
        Assert.False(log.Visible);

        // Open the file; the build's A2L comes from config/a2l.
        await panel.OpenCalibrationAsync(file);
        Assert.Contains("A2L C132E0278.a2l", log.Text);
        Assert.True(panel.SelectItem(table));
        var grid = panel.Grid!;
        Assert.Equal((9, 9), (grid.ColumnCount, grid.RowCount)); // 8 × 8 plus the axis row and column
        // The graph draws on the GPU through Direct2D (this app is x86), unless GDI+ is forced.
        var plot = panel.GraphPane.Plot;
        if (Environment.GetEnvironmentVariable("LOTUS_GDI_GRAPHS") != "1")
        {
            await Until(() => plot.LastDrawMilliseconds > 0, "a Direct2D frame");
            Assert.True(plot.UsingDirect2D);
        }

        void SelectCells(params (int X, int Y)[] cells) => grid.SelectData(cells);

        // Offline: 50 ms is raw 10; nothing reaches the ECU yet.
        SelectCells((3, 5));
        panel.ApplyValue("50");
        Assert.Equal(10, panel.Image!.ReadRaw(A2lItem(table), 3, 5));
        Assert.Equal(0, Ecu(3, 5));

        // Start live sync: the edit makes the file differ, so the user picks a side. Send the file.
        await Click("Start Live Sync");
        await Until(() => panel.Session?.Status == LiveTuningStatus.Comparing, "the comparison");
        await Click("Send File to ECU");
        await Until(() => panel.Session?.Status == LiveTuningStatus.Syncing && Ecu(3, 5) == 10, "the file to reach the ECU");
        Assert.Equal(2, confirmations.Count); // starting sync and sending the file
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "ccp-backup_*.bin"));

        // Live: an edit, its undo, and a pasted block are written as they happen.
        SelectCells((0, 0));
        panel.ApplyValue("25");
        await Until(() => Ecu(0, 0) == 5 && panel.Session!.PendingBytes == 0, "the live edit");
        panel.RunCommand(GridCommand.Undo);
        await Until(() => Ecu(0, 0) == 0, "the undo to reach the ECU");
        panel.PasteBlock("10\t20\r\n30\t40");
        await Until(() => (Ecu(0, 0), Ecu(1, 0), Ecu(0, 1), Ecu(1, 1)) == (2, 4, 6, 8), "the pasted block");
        await Until(() => Descendants(control).OfType<Label>().Any(l => l.Text == "ECU in sync."), "the sync label to settle");

        // The fine step is one raw count (5 ms), applied to the whole selection.
        SelectCells((0, 0), (1, 0));
        panel.RunCommand(GridCommand.IncrementFine);
        await Until(() => (Ecu(0, 0), Ecu(1, 0)) == (3, 5), "the fine step");

        // The live cursor: where the ECU is reading the fan table, from its streamed inputs.
        Assert.True(panel.SelectItem(Fan));
        await Until(() => panel.Grid?.OperatingPoint is { X: { } x, Y: { } y } && x == new AxisPosition(2, 0.5) && y == new AxisPosition(3, 0),
            "the live cursor");
        Assert.Contains("Live", Descendants(control).OfType<Label>().Single(l => l.Text.StartsWith("Live")).Text);

        // Save As keeps the edits in a new file; the source is untouched.
        string tuned = Path.Combine(directory, "tuned.bin");
        panel.SaveAs(tuned);
        Assert.Equal(6, File.ReadAllBytes(tuned)[0x40008A9A - (int)cal + 8]);
        Assert.Equal(0, File.ReadAllBytes(file)[0x40008A9A - (int)cal + 8]);

        if (Environment.GetEnvironmentVariable("CCP_UI_RENDER_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            await Task.Delay(200);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(dir, "ccp_tuningPage.png"));
        }

        await Click("Stop Sync");
        await Until(() => panel.Session is null, "sync to stop");
        Named<Button>(control, "disconnectButton").PerformClick();
        await Until(() => host.LeaseCount == 0, "the session to close");
        Directory.Delete(directory, recursive: true);
    });

    [Fact]
    public void TheTuningTabRecordsASessionAndReplaysIt() => RunInMessageLoop(async form =>
    {
        const uint AirTemp = 0x4000183A, CoolantTemp = 0x40001836;
        using var slave = new FakeCcpSlave();
        uint cal = Gt430.CalibrationBase;
        slave.Load(cal, Encoding.ASCII.GetBytes("C132E0278 test calibration"));
        slave.Load(A2lItem(Fan + "_X_air_temp").Address, Enumerable.Range(0, 8).Select(i => (byte)(40 + 20 * i)).ToArray());
        slave.Load(A2lItem(Fan + "_Y_coolant_temp").Address, Enumerable.Range(0, 8).Select(i => (byte)(10 * i)).ToArray());
        slave.Load(AirTemp, [90]);     // halfway between X points 2 and 3
        slave.Load(CoolantTemp, [30]); // on Y point 3
        string directory = Path.Combine(Path.GetTempPath(), "ccp-ui-recording-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stock.bin");
        File.WriteAllBytes(file, Enumerable.Range(0, (int)Gt430.CalibrationSize).Select(i => slave.Peek(cal + (uint)i)).ToArray());

        SimulatedCcpTransport? transport = null;
        using var host = new CcpSessionHost(() => transport = new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(300) });
        var control = new CcpControl(host) { Dock = DockStyle.Fill, BackupDirectory = Path.Combine(directory, "backups") };
        form.Controls.Add(control);
        var log = Named<TextBox>(control, "logTextBox");
        var panel = control.TuningPanel;
        panel.Confirm = _ => true;
        panel.RecordingDirectory = directory;
        Task Until(Func<bool> condition, string what, int seconds = 15) =>
            WaitUntil(() => !control.IsBusy && condition(), what, log, seconds);
        var fan = A2lItem(Fan);
        IEnumerable<string> Owners() => host.Master!.GetDaqAllocations().Select(a => a.Owner);

        Named<Button>(control, "connectButton").PerformClick();
        await Until(() => control.Identification?.Build is not null, "identification");
        var tabs = Named<TabControl>(control, "subTabs");
        tabs.SelectedTab = tabs.TabPages["tuningPage"];
        await panel.OpenCalibrationAsync(file);
        ButtonWithText(control, "Start Live Sync").PerformClick();
        await Until(() => panel.Session?.Status == LiveTuningStatus.Syncing, "live sync");

        // Record: every axis input streams, and the cursor reads the recording, not a watch of its own.
        string recordingPath = Path.Combine(directory, "session.csv");
        await panel.StartRecordingAsync(recordingPath);
        Assert.NotNull(panel.Recorder);
        await Until(() => panel.Recorder!.Bits(AirTemp, 1) == 90, "the recording to stream");
        Assert.True(panel.SelectItem(Fan));
        await Until(() => panel.Grid?.OperatingPoint is { X: { } x, Y: { } y } && x == new AxisPosition(2, 0.5) && y == new AxisPosition(3, 0),
            "the cursor from the recording");
        Assert.StartsWith("Live (recording)", Descendants(control).OfType<Label>().Single(l => l.Text.StartsWith("Live")).Text);
        // Switching tables reconfigures no DAQ, so the recording loses nothing.
        Assert.True(panel.SelectItem("CAL_injtip_transient_throttle_response_time"));
        Assert.True(panel.SelectItem(Fan));
        await Task.Delay(200);
        Assert.DoesNotContain(CcpVariableWatch.OwnerName, Owners());
        Assert.All(host.Master!.GetDaqAllocations().Where(a => a.Owner == TuningRecorder.OwnerName), a => Assert.Equal(0, a.Gaps));

        // A live edit is recorded as a write; then the ECU's input moves to X point 5.
        panel.Grid!.SelectData([(5, 3)]);
        panel.ApplyValue("77");
        double written = panel.Image!.ReadRaw(fan, 5, 3);
        Assert.NotEqual(0, written);
        await Until(() => panel.Recorder?.EditCount == 1, "the write to be recorded");
        transport!.With(s => s.Load(AirTemp, [140]));
        await Until(() => panel.Grid?.OperatingPoint is { X: { } x } && x == new AxisPosition(5, 0), "the cursor to follow the input");
        await panel.StopRecordingAsync(null);
        Assert.Null(panel.Recorder);
        Assert.Contains("Recording saved", log.Text);
        var files = new RecordingFiles(recordingPath);
        foreach (var path in new[] { files.Log, files.Edits, files.CalibrationStart, files.CalibrationEnd })
            Assert.True(File.Exists(path), path);
        ButtonWithText(control, "Stop Sync").PerformClick();
        await Until(() => panel.Session is null, "sync to stop");

        // Replay: the calibration as it was at each moment, and the cursor where the ECU was reading.
        await panel.OpenRecordingAsync(recordingPath);
        var recording = panel.Recording!;
        Assert.True(panel.Timeline.Parent!.Visible);
        Assert.Single(panel.Timeline.Marks);
        Assert.True(panel.SelectItem(Fan));
        panel.SeekReplay(0);
        Assert.Equal(0, panel.Image!.ReadRaw(fan, 5, 3));
        panel.SeekReplay(recording.Duration);
        Assert.Equal(written, panel.Image!.ReadRaw(fan, 5, 3));
        await Until(() => panel.Grid?.OperatingPoint is { X: { } x, Y: { } y } && x == new AxisPosition(5, 0) && y == new AxisPosition(3, 0),
            "the replayed cursor");
        Assert.StartsWith("Replay", Descendants(control).OfType<Label>().Single(l => l.Text.StartsWith("Replay ")).Text);

        // Cells read: the time at each cell, shared as the lookup interpolates.
        Find<CheckBox>(control, c => c.Text == "Cells read").Checked = true;
        await Until(() => panel.Grid?.Activity is not null, "the cell activity");
        var activity = panel.Grid!.Activity!;
        Assert.True(activity.At(2, 3) > 0 && activity.At(3, 3) > 0 && activity.At(5, 3) > 0);
        Assert.Equal(0, activity.At(0, 0));

        // A replay is not edited.
        panel.Grid.SelectData([(0, 0)]);
        panel.ApplyValue("50");
        Assert.Equal(0, panel.Image!.ReadRaw(fan, 0, 0));

        // Playing runs to the end, then stops.
        panel.SeekReplay(0);
        panel.Play();
        await Until(() => !panel.IsPlaying && panel.ReplayTime == recording.Duration, "playback to finish");

        if (Environment.GetEnvironmentVariable("CCP_UI_RENDER_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            panel.SeekReplay(recording.Duration / 2);
            await Task.Delay(200);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(dir, "ccp_tuningReplay.png"));
        }

        // Closing returns to the calibration that was open before.
        panel.CloseRecording();
        Assert.Null(panel.Recording);
        Assert.Equal(Path.GetFullPath(file), panel.Image!.SourcePath);
        Named<Button>(control, "disconnectButton").PerformClick();
        await Until(() => host.LeaseCount == 0, "the session to close");
        Directory.Delete(directory, recursive: true);
    });

    [Fact]
    public void TheSnapshotsTabDownloadsFlashOverCcp() => RunInMessageLoop(async form =>
    {
        using var slave = new FakeCcpSlave();
        var region = T6FlashRegion.LearnedData;
        var flash = new byte[region.Length];
        new Random(91).NextBytes(flash);
        slave.Load(region.Address, flash);
        string directory = Path.Combine(Path.GetTempPath(), "ccp-ui-snapshots-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "learned.bin");
        using var host = new CcpSessionHost(() => new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(300) });
        var control = new SnapshotsControl(host) { Dock = DockStyle.Fill };
        form.Controls.Add(control);
        string? suggested = null;
        var messages = new List<(string Text, MessageBoxIcon Icon)>();
        control.ChooseFile = name => { suggested = name; return file; };
        control.Notify = (text, _, icon) => messages.Add((text, icon));
        var log = new TextBox();

        Named<Button>(control, "downloadLearnedDataButton").PerformClick();
        await WaitUntil(() => messages.Count > 0, "the download to finish", log, 30);
        Assert.Equal(MessageBoxIcon.Information, messages[0].Icon);
        Assert.Contains("checksum of the region matches", messages[0].Text);
        Assert.Equal(flash, File.ReadAllBytes(file));
        Assert.Contains("LearnedData_", Path.GetFileName(suggested));
        await WaitUntil(() => Named<Button>(control, "downloadLearnedDataButton").Text == "Download", "the button to reset", log, 5);
        await WaitUntil(() => host.LeaseCount == 0, "the session to be released", log, 5);

        if (Environment.GetEnvironmentVariable("CCP_UI_RENDER_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(dir, "snapshots.png"));
        }
        Directory.Delete(directory, recursive: true);
    });

    private const string Fan = "CAL_cooling_fan_afterrun";

    private static LotusECMLogger.Services.A2l.A2lCalibrationItem A2lItem(string name) =>
        LotusECMLogger.Services.A2l.A2lLayout.Resolve(LotusECMLogger.Services.A2l.A2lParser.ParseFile(
            Path.Combine(AppContext.BaseDirectory, "config", "a2l", "C132E0278.a2l"))).Items[name];

    private static byte[] Registry(params (uint Address, byte Size)[] entries)
    {
        var bytes = new byte[44 + entries.Length * 6];
        "TEST PROGRAM"u8.CopyTo(bytes);
        "V0001"u8.CopyTo(bytes.AsSpan(32));
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44 + i * 6), entries[i].Address);
            bytes[44 + i * 6 + 4] = entries[i].Size;
        }
        return bytes;
    }

    /// <summary>Runs <paramref name="body"/> on an off-screen form inside Application.Run, as the app does.</summary>
    private static void RunInMessageLoop(Func<Form, Task> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000),
                ShowInTaskbar = false, Size = new Size(1100, 820),
            };
            form.Shown += async (_, _) =>
            {
                try { await body(form); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3))) Assert.Fail("The UI test did not finish.");
        failure?.Throw();
    }

    private static async Task WaitUntil(Func<bool> condition, string what, TextBox log, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"Timed out waiting for {what}. Log:\n{log.Text}");
            await Task.Delay(10);
        }
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, true).OfType<T>().Single();

    private static T Find<T>(Control root, Func<T, bool> match) where T : Control =>
        Descendants(root).OfType<T>().First(match);

    private static Button ButtonWithText(Control root, string text) => Find<Button>(root, b => b.Text == text);

    private static ListView ListViewWithColumns(Control root, string first, string second) =>
        Find<ListView>(root, l => l.Columns.Count > 1 && l.Columns[0].Text == first && l.Columns[1].Text == second);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
