using System.Globalization;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab's Self-Test page: runs <see cref="CcpSelfTest"/> on the connected session, shows each check as it
    /// finishes, and saves the report as text for sharing.
    /// </summary>
    internal sealed class CcpSelfTestPanel : UserControl
    {
        private static readonly Color PassColor = Color.ForestGreen;
        private static readonly Color WarnColor = Color.DarkOrange;
        private static readonly Color FailColor = Color.Firebrick;

        private readonly ICcpTabContext context;
        private readonly Button runButton = new() { Text = "Run Self-Test", AutoSize = true };
        private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true };
        private readonly Button saveButton = new() { Text = "Save Report…", AutoSize = true };
        private readonly Label overallLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Padding = new Padding(8, 6, 0, 0) };
        private readonly ListView resultsView = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
        private CcpSelfTestReport? report;
        private bool running;

        public CcpSelfTestPanel(ICcpTabContext context)
        {
            this.context = context;
            resultsView.Columns.Add("Check", 230);
            resultsView.Columns.Add("Result", 70);
            resultsView.Columns.Add("Time", 60);
            resultsView.Columns.Add("Detail", 900);
            GuiIcons.ApplyToButton(runButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(cancelButton, GuiIcons.Clear);
            GuiIcons.ApplyToButton(saveButton, GuiIcons.Save);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            bar.Controls.AddRange([runButton, cancelButton, saveButton, overallLabel]);
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 36, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 2, 6, 0),
                Text = "Read-only: nothing is written to the ECU. On a car it takes about a minute, mostly reading " +
                    "calibration RAM. The DAQ check needs every DAQ list, so stop the High-Speed Log and any variable watch first.",
            };
            Controls.Add(resultsView);
            Controls.Add(note);
            Controls.Add(bar);

            runButton.Click += async (_, _) => await RunAsync();
            cancelButton.Click += (_, _) => context.CancelOperation();
            saveButton.Click += (_, _) => Save();
            context.SessionChanged += (_, _) => UpdateState();
            UpdateState();
        }

        private async Task RunAsync()
        {
            if (context.Master is not { } master) return;
            running = true;
            report = null;
            resultsView.Items.Clear();
            overallLabel.Text = "Running…";
            overallLabel.ForeColor = SystemColors.ControlText;
            var progress = new Progress<CcpCheckResult>(AddRow);
            var build = context.Identification?.Build;
            var options = new CcpSelfTestOptions { EngineSpeed = build is null ? null : CcpControl.EngineSpeedFor(build) };
            try
            {
                await context.RunAsync("Self-test", async ct =>
                {
                    report = await CcpSelfTest.RunAsync(master, options, progress, ct);
                    context.Log($"Self-test: {report.Overall} ({report.Checks.Count(c => c.Status == CcpCheckStatus.Fail)} failed, " +
                        $"{report.Checks.Count(c => c.Status == CcpCheckStatus.Warn)} warning(s)) in {report.Duration.TotalSeconds:F0} s.");
                });
            }
            finally
            {
                running = false;
                if (!IsDisposed && !Disposing) // a self-test can outlive the window
                {
                    (overallLabel.Text, overallLabel.ForeColor) = report is null
                        ? ("Did not finish.", FailColor)
                        : ($"Overall: {report.Overall.ToString().ToUpperInvariant()}", ColorFor(report.Overall));
                    UpdateState();
                }
            }
        }

        private void AddRow(CcpCheckResult check)
        {
            if (IsDisposed || Disposing) return; // a report can arrive after the window closed
            resultsView.Items.Add(new ListViewItem(
            [
                check.Name, check.Status.ToString(), check.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s",
                check.Detail,
            ]) { ForeColor = ColorFor(check.Status), ToolTipText = check.Detail });
        }

        private static Color ColorFor(CcpCheckStatus status) => status switch
        {
            CcpCheckStatus.Pass => PassColor,
            CcpCheckStatus.Warn => WarnColor,
            CcpCheckStatus.Fail => FailColor,
            _ => SystemColors.GrayText,
        };

        private void Save()
        {
            if (report is null) return;
            Directory.CreateDirectory(LoggerPaths.OutputDirectory);
            using var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                InitialDirectory = LoggerPaths.OutputDirectory,
                FileName = $"ccp-selftest_{report.Build ?? "unknown"}_{report.Started:yyyyMMdd-HHmmss}.txt",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName, report.Format(
            [
                $"App:      LotusECMLogger {Application.ProductVersion}",
                $"OS:       {Environment.OSVersion.VersionString}",
            ]));
            context.Log($"Saved the self-test report to {dialog.FileName}.");
        }

        private void UpdateState()
        {
            runButton.Enabled = !running && !context.IsBusy && context.IsConnected;
            cancelButton.Enabled = running;
            saveButton.Enabled = !running && report is not null;
        }
    }
}
