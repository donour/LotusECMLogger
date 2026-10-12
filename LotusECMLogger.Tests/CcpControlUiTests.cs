using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Text;
using LotusECMLogger.Controls;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;

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
