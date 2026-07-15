using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Diagnostics UI for the Bosch ESP8 ABS/ESP module. Read-only actions: read ECU identification,
    /// probe/scan for the module's diagnostic CAN addressing, and passively sniff the bus (to learn
    /// the addressing by watching an external reference tester). Future features will be added here.
    /// </summary>
    public partial class AbsControl : UserControl
    {
        private readonly IAbsService absService;
        private bool isLoggerActive;

        public AbsControl(IAbsService absService)
        {
            this.absService = absService ?? throw new ArgumentNullException(nameof(absService));
            InitializeComponent();
            SetupListViewColumns();
            GuiIcons.ApplyToButton(readInfoButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(moduleInfoButton, GuiIcons.VehicleInfo);
            GuiIcons.ApplyToButton(testConnectionButton, GuiIcons.Connect);
            GuiIcons.ApplyToButton(sniffBusButton, GuiIcons.LiveData);
        }

        /// <summary>
        /// True while the main logger is running. The ABS read opens its own J2534 session,
        /// which cannot coexist with active logging, so the read button is disabled meanwhile.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLoggerActive
        {
            get => isLoggerActive;
            set
            {
                isLoggerActive = value;
                UpdateUIState();
            }
        }

        private void SetupListViewColumns()
        {
            infoListView.Columns.Clear();
            infoListView.Columns.Add("Field", 220);
            infoListView.Columns.Add("Value", 520);
        }

        // The three actions each open their own J2534 session and must not overlap, so enabling one
        // enables all and disabling one disables all (logging blocks every action).
        private void SetActionsEnabled(bool enabled)
        {
            bool on = enabled && !isLoggerActive;
            testConnectionButton.Enabled = on;
            readInfoButton.Enabled = on;
            moduleInfoButton.Enabled = on;
            sniffBusButton.Enabled = on;
        }

        private void UpdateUIState()
        {
            SetActionsEnabled(true);
            if (isLoggerActive)
                statusLabel.Text = "Stop logging to read from the ABS module.";
        }

        // The results grid can't be copied out of the window, so mirror every run to a text file
        // (overwritten each time) for out-of-band review. Best-effort — never fails the operation.
        private static readonly string DiagnosticsLogPath =
            Path.Combine(LoggerPaths.OutputDirectory, "abs-diagnostics.txt");

        private static void WriteDiagnosticsLog(string title, IEnumerable<KeyValuePair<string, string>> rows)
        {
            try
            {
                LoggerPaths.EnsureParentDirectory(DiagnosticsLogPath);
                var lines = new List<string> { $"# {title} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
                foreach (var row in rows)
                    lines.Add($"{row.Key}\t{row.Value}");
                File.WriteAllText(DiagnosticsLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch
            {
                // Diagnostics logging is best-effort; ignore file/IO errors.
            }
        }

        private async void testConnectionButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive)
                return;

            SetActionsEnabled(false);
            testConnectionButton.Text = "Testing...";
            statusLabel.Text = "Pinging ABS (physical + functional)...";
            infoListView.Items.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => absService.ProbeConnection());

                if (!success)
                {
                    statusLabel.Text = "Could not run connection test — saved to abs-diagnostics.txt";
                    WriteDiagnosticsLog("ABS Test Connection FAILED",
                        [new KeyValuePair<string, string>("Error", errorMessage)]);
                    MessageBox.Show($"Connection test failed:\n\n{errorMessage}",
                        "ABS Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var row in result.Rows)
                {
                    var item = new ListViewItem(row.Key);
                    item.SubItems.Add(row.Value);
                    infoListView.Items.Add(item);
                }

                WriteDiagnosticsLog("ABS Test Connection", result.Rows);

                statusLabel.Text = "Probe complete — saved to Documents\\LotusECMLogger\\abs-diagnostics.txt";
            }
            finally
            {
                testConnectionButton.Text = "Test Connection";
                SetActionsEnabled(true);
            }
        }

        private async void readInfoButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive)
                return;

            SetActionsEnabled(false);
            readInfoButton.Text = "Reading...";
            statusLabel.Text = "Reading ABS trouble codes...";
            infoListView.Items.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => absService.ReadDtcs());

                if (!success)
                {
                    statusLabel.Text = "Could not read ABS DTCs — saved to abs-diagnostics.txt";
                    WriteDiagnosticsLog("ABS Read DTCs FAILED",
                        [new KeyValuePair<string, string>("Error", errorMessage)]);
                    MessageBox.Show($"Failed to read ABS trouble codes:\n\n{errorMessage}",
                        "ABS Read Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var row in result.Rows)
                    AddRow(row.Key, row.Value);

                WriteDiagnosticsLog("ABS Read DTCs", result.Rows);

                statusLabel.Text = "ABS trouble codes read — saved to Documents\\LotusECMLogger\\abs-diagnostics.txt";
            }
            finally
            {
                readInfoButton.Text = "Read DTCs";
                // Re-enable only if logging did not start while the operation was in flight.
                SetActionsEnabled(true);
            }
        }

        // Seconds to passively capture after the idle baseline. Long enough for the user to trigger
        // the reference tester's ABS read during the window.
        private const int SniffCaptureSeconds = 40;

        private static readonly string SniffLogPath =
            Path.Combine(LoggerPaths.OutputDirectory, "abs-sniff.txt");

        private static void WriteSniffLog(AbsSniffResult result)
        {
            try
            {
                LoggerPaths.EnsureParentDirectory(SniffLogPath);
                var lines = new List<string>
                {
                    $"# ABS Bus Sniff — {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"# Baseline periodic ids: {result.BaselineIdCount}",
                    $"# New ids seen during capture ({result.NewIds.Count}):",
                };
                lines.AddRange(result.NewIds);
                lines.Add($"# Frames ({result.Frames.Count}) — elapsed, id, data:");
                lines.AddRange(result.Frames);
                File.WriteAllLines(SniffLogPath, lines);
            }
            catch
            {
                // Best-effort; ignore file/IO errors.
            }
        }

        private async void moduleInfoButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive)
                return;

            SetActionsEnabled(false);
            moduleInfoButton.Text = "Reading...";
            statusLabel.Text = "Reading ABS module info (no unlock)...";
            infoListView.Items.Clear();

            var progress = new Progress<string>(s => statusLabel.Text = s);

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => absService.ReadModuleInfo(progress));

                if (!success)
                {
                    statusLabel.Text = "Could not read ABS info — saved to abs-diagnostics.txt";
                    WriteDiagnosticsLog("ABS Read Info FAILED",
                        [new KeyValuePair<string, string>("Error", errorMessage)]);
                    MessageBox.Show($"Failed to read ABS module info:\n\n{errorMessage}",
                        "ABS Read Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var field in result.Fields)
                    AddRow(field.Key, field.Value);

                WriteDiagnosticsLog("ABS Read Info", result.Fields);

                statusLabel.Text = "ABS module info read — saved to Documents\\LotusECMLogger\\abs-diagnostics.txt";
            }
            finally
            {
                moduleInfoButton.Text = "Read Info";
                SetActionsEnabled(true);
            }
        }

        private async void sniffBusButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive)
                return;

            SetActionsEnabled(false);
            sniffBusButton.Text = "Sniffing...";
            infoListView.Items.Clear();

            // Progress<T> marshals its callback to this (UI) thread, so status updates are safe.
            var progress = new Progress<string>(s => statusLabel.Text = s);

            try
            {
                var (success, errorMessage, result) = await Task.Run(
                    () => absService.SniffBus(SniffCaptureSeconds, progress));

                if (!success)
                {
                    statusLabel.Text = "Sniff failed";
                    MessageBox.Show($"Bus sniff failed:\n\n{errorMessage}",
                        "ABS Bus Sniff", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                AddRow("Baseline", $"{result.BaselineIdCount} periodic ids");
                foreach (var nid in result.NewIds)
                    AddRow("New id", nid);
                AddRow("Frames", $"{result.Frames.Count} captured — see abs-sniff.txt");

                WriteSniffLog(result);

                statusLabel.Text = result.NewIds.Count == 0
                    ? "No new ids — did the tester run during capture? (saved to abs-sniff.txt)"
                    : $"{result.NewIds.Count} new id(s) — saved to Documents\\LotusECMLogger\\abs-sniff.txt";
            }
            finally
            {
                sniffBusButton.Text = "Sniff Bus";
                SetActionsEnabled(true);
            }
        }

        private void AddRow(string field, string value)
        {
            var item = new ListViewItem(field);
            item.SubItems.Add(value);
            infoListView.Items.Add(item);
        }
    }
}
