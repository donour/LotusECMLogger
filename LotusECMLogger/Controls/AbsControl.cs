using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>ABS diagnostics, capture, offline review and the bounded OEM pump motor test.</summary>
    public partial class AbsControl : UserControl
    {
        private readonly IAbsService? absService;
        private readonly CancellationTokenSource lifetimeCts = new();
        private CancellationTokenSource? pumpCts;
        private bool isLoggerActive;
        private bool operationBusy;
        private bool stoppingDiagnostics;
        private bool stoppingTelemetry;
        private volatile bool shuttingDown;
        private volatile int monitorGeneration;
        private AbsDiagnosticBaseline? latestBaseline;
        private AbsDiagnosticCaptureDocument? reviewedCapture;
        private string? reviewedCapturePath;
        private string? diagnosticCapturePath;
        private bool changingReview;
        private bool reviewingBaseline;

        // The designer must never create a service or open a J2534 device.
        public AbsControl()
        {
            InitializeComponent();
            actuationProgressLabel.Text = "Confirm the operator conditions before running the pump test.";
            GuiIcons.ApplyToButton(readInfoButton, GuiIcons.Dtc);
            GuiIcons.ApplyToButton(moduleInfoButton, GuiIcons.VehicleInfo);
            GuiIcons.ApplyToButton(testConnectionButton, GuiIcons.Connect);
            GuiIcons.ApplyToButton(sniffBusButton, GuiIcons.LiveData);
            GuiIcons.ApplyToButton(readLiveStateButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(startDiagnosticButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopDiagnosticButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(startTelemetryButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopTelemetryButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(runRoutineButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopRoutineButton, GuiIcons.Stop);
            UpdateUIState();
        }

        public AbsControl(IAbsService absService) : this()
        {
            this.absService = absService ?? throw new ArgumentNullException(nameof(absService));
            absService.TelemetryReceived += OnTelemetryReceived;
            absService.TelemetryError += OnTelemetryError;
            absService.DiagnosticSampleReceived += OnDiagnosticSampleReceived;
            absService.DiagnosticMonitorError += OnDiagnosticMonitorError;
            UpdateUIState();
        }

        private IAbsService Service => absService
            ?? throw new InvalidOperationException("No ABS service is available.");
        private bool IsMonitoringTelemetry => absService?.IsMonitoringTelemetry == true;
        private bool IsMonitoringDiagnostics => absService?.IsMonitoringDiagnostics == true;
        private bool IsClosing => shuttingDown || IsDisposed || Disposing;
        private bool DeviceAvailable => !IsClosing && absService is not null && !operationBusy &&
            !isLoggerActive && !IsMonitoringTelemetry && !IsMonitoringDiagnostics;
        private bool FilesAvailable => !IsClosing && !operationBusy &&
            !IsMonitoringTelemetry && !IsMonitoringDiagnostics;

        partial void DisposeManaged()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            monitorGeneration++;
            lifetimeCts.Cancel();
            if (absService is not null)
            {
                absService.TelemetryReceived -= OnTelemetryReceived;
                absService.TelemetryError -= OnTelemetryError;
                absService.DiagnosticSampleReceived -= OnDiagnosticSampleReceived;
                absService.DiagnosticMonitorError -= OnDiagnosticMonitorError;
                // Driver shutdown can outlive its join timeout. Let the service retain its
                // device lease until cleanup finishes without blocking form destruction.
                var service = absService;
                _ = Task.Run(() =>
                {
                    if (service is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                    else
                    {
                        try { service.StopDiagnosticMonitor(); } catch { }
                        try { service.StopTelemetryMonitor(); } catch { }
                    }
                });
            }
            lifetimeCts.Dispose();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLoggerActive
        {
            get => isLoggerActive;
            set
            {
                isLoggerActive = value;
                UpdateUIState();
                if (value && !operationBusy && !IsMonitoringDiagnostics && !IsMonitoringTelemetry)
                    statusLabel.Text = "Stop the main logger before starting ABS diagnostics.";
            }
        }

        /// <summary>Keep ordinary form closure pending while a pump operation finishes its shutdown attempts.</summary>
        public bool DeferCloseForPumpCleanup()
        {
            if (pumpCts is not { } cancellation) return false;
            cancellation.Cancel();
            if (!IsClosing)
            {
                absTabs.SelectedTab = actuationTab;
                const string message = "Close paused while OFF, stop and default-session attempts finish. Review the result, then close again.";
                actuationProgressLabel.Text = message;
                statusLabel.Text = message;
            }
            return true;
        }

        private void UpdateUIState()
        {
            if (IsClosing) return;
            bool available = DeviceAvailable;
            testConnectionButton.Enabled = available;
            readInfoButton.Enabled = available;
            moduleInfoButton.Enabled = available;
            sniffBusButton.Enabled = available;
            readLiveStateButton.Enabled = available;
            startTelemetryButton.Enabled = available;
            startDiagnosticButton.Enabled = available;
            stopTelemetryButton.Enabled = !operationBusy && IsMonitoringTelemetry && !stoppingTelemetry;
            stopDiagnosticButton.Enabled = !operationBusy && IsMonitoringDiagnostics && !stoppingDiagnostics;
            logTelemetryCheckBox.Enabled = available;
            diagnosticIntervalNumeric.Enabled = available;
            captureNotesTextBox.ReadOnly = operationBusy || IsMonitoringDiagnostics;
            saveBaselineButton.Enabled = FilesAvailable && latestBaseline is not null;
            openCaptureButton.Enabled = FilesAvailable;
            exportCaptureButton.Enabled = FilesAvailable && reviewedCapture is not null;
            reviewBaselineButton.Enabled = FilesAvailable && reviewedCapture?.Baseline is not null;
            reviewSampleNumeric.Enabled = FilesAvailable && reviewedCapture?.Samples.Count > 0;

            pumpOperatorCheckBox.Enabled = available;
            runRoutineButton.Enabled = available && pumpOperatorCheckBox.Checked;
            // Cancellation does not end device ownership. Keep Stop available until the runner returns.
            stopRoutineButton.Enabled = pumpCts is not null;
            durationNumeric.Enabled = available;
        }

        private bool BeginOperation(bool deviceOperation, string status)
        {
            if (deviceOperation ? !DeviceAvailable : !FilesAvailable) return false;
            operationBusy = true;
            UpdateUIState();
            statusLabel.Text = status;
            return true;
        }

        private void EndOperation()
        {
            operationBusy = false;
            UpdateUIState();
        }

        private void PostToUi(Action action)
        {
            if (IsClosing || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke((Action)(() => { if (!IsClosing) action(); }));
                else action();
            }
            catch (InvalidOperationException) { } // Handle destruction races with queued updates.
        }

        private IProgress<string> StatusProgress() => new Progress<string>(text =>
        {
            if (!IsClosing && operationBusy) statusLabel.Text = text;
        });

        private static readonly string DiagnosticsLogPath =
            Path.Combine(LoggerPaths.OutputDirectory, "abs-diagnostics.txt");
        private const string SavedToLog = "saved to Documents\\LotusECMLogger\\abs-diagnostics.txt";

        private static void WriteDiagnosticsLog(string title, IEnumerable<AbsReportRow> rows)
        {
            try
            {
                LoggerPaths.EnsureParentDirectory(DiagnosticsLogPath);
                var lines = new List<string> { $"# {title} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
                lines.AddRange(rows.Select(row => $"{row.Field}\t{row.Value}\t{row.Detail}"));
                File.WriteAllLines(DiagnosticsLogPath, lines);
            }
            catch { } // Auxiliary text output must not hide a diagnostic result.
        }

        private static void Populate(ListView list, IEnumerable<AbsReportRow> rows)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var row in rows)
                {
                    var item = new ListViewItem(row.Field);
                    item.SubItems.Add(row.Value);
                    item.SubItems.Add(row.Detail);
                    list.Items.Add(item);
                }
            }
            finally { list.EndUpdate(); }
        }

        private async Task<bool> RunOperation(Button button, string busyText, string busyStatus,
            string title, ListView target,
            Func<(bool success, string errorMessage, IReadOnlyList<AbsReportRow> rows)> operation,
            string successStatus)
        {
            if (!BeginOperation(true, busyStatus)) return false;
            string originalText = button.Text;
            button.Text = busyText;
            target.Items.Clear();
            try
            {
                var (success, error, rows) = await Task.Run(operation, lifetimeCts.Token);
                if (IsClosing) return false;
                Populate(target, rows);
                WriteDiagnosticsLog(success ? title : $"{title} FAILED",
                    rows.Count == 0 && !success ? [new AbsReportRow("Error", error)] : rows);
                statusLabel.Text = success ? successStatus : $"{title}: {error}";
                if (!success && rows.Count == 0) ShowError(title, error);
                return success;
            }
            catch (OperationCanceledException) when (IsClosing) { return false; }
            catch (Exception ex)
            {
                if (!IsClosing) ShowError(title, ex.Message);
                return false;
            }
            finally
            {
                if (!IsClosing) button.Text = originalText;
                EndOperation();
            }
        }

        private void ShowError(string title, string message)
        {
            statusLabel.Text = $"{title}: {message}";
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private async void testConnectionButton_Click(object sender, EventArgs e) =>
            await RunOperation(testConnectionButton, "Testing...", "Probing ABS addressing...",
                "ABS connection test", infoListView, () =>
                {
                    var (ok, error, result) = Service.ProbeConnection();
                    return (ok, error, result.Rows);
                }, $"Probe complete — {SavedToLog}");

        private async void readInfoButton_Click(object sender, EventArgs e) =>
            await RunOperation(readInfoButton, "Reading...", "Reading ABS trouble codes...",
                "ABS DTC read", infoListView, () =>
                {
                    var (ok, error, result) = Service.ReadDtcs();
                    return (ok, error, result.Rows);
                }, $"ABS trouble codes read — {SavedToLog}");

        private async void moduleInfoButton_Click(object sender, EventArgs e)
        {
            AbsDiagnosticBaseline? baseline = null;
            var progress = StatusProgress();
            await RunOperation(moduleInfoButton, "Reading...", "Reading ABS baseline...",
                "ABS baseline", infoListView, () =>
                {
                    var (ok, error, result) = Service.ReadBaseline(progress);
                    baseline = result;
                    return (ok, error, result.Rows);
                }, "ABS baseline read. Save Baseline preserves the raw exchanges and notes.");
            if (!IsClosing && baseline is not null)
            {
                latestBaseline = baseline; // Partial reads retain their raw exchanges and errors too.
                UpdateUIState();
            }
        }

        private async void readLiveStateButton_Click(object sender, EventArgs e)
        {
            if (!DeviceAvailable) return;
            ClearOfflineReview();
            captureStatusLabel.Text = "Reading one diagnostic live-data sample...";
            var progress = StatusProgress();
            await RunOperation(readLiveStateButton, "Reading...", "Reading diagnostic live data (61 04)...",
                "ABS diagnostic live data", liveStateListView, () =>
                {
                    var (ok, error, result) = Service.ReadLiveState(progress);
                    return (ok, error, result.Rows);
                }, $"Diagnostic live data read — {SavedToLog}");
            if (!IsClosing) captureStatusLabel.Text = "Diagnostic live-data read finished. Start Capture to save a sequence.";
        }

        private void ClearOfflineReview()
        {
            reviewedCapture = null;
            reviewedCapturePath = null;
            reviewingBaseline = false;
            reviewBaselineButton.Text = "View Baseline";
            reviewCountLabel.Text = "No saved capture open";
            changingReview = true;
            try { reviewSampleNumeric.Value = 1; reviewSampleNumeric.Maximum = 1; }
            finally { changingReview = false; }
            UpdateUIState();
        }

        private static void RequireNewFile(string path)
        {
            if (File.Exists(path))
                throw new IOException("Choose a new file name. Existing captures, baselines and exports are preserved.");
        }

        private static SaveFileDialog SaveDialog(string title, string prefix, string extension, string filter)
        {
            string path = LoggerPaths.UniquePath(LoggerPaths.TimestampedPath(prefix, extension));
            LoggerPaths.EnsureParentDirectory(path);
            return new SaveFileDialog
            {
                Title = title, InitialDirectory = LoggerPaths.OutputDirectory,
                FileName = Path.GetFileName(path), DefaultExt = extension,
                Filter = filter, AddExtension = true, OverwritePrompt = false,
            };
        }

        private async void saveBaselineButton_Click(object sender, EventArgs e)
        {
            if (!FilesAvailable || latestBaseline is not { } baseline) return;
            try
            {
                using var dialog = SaveDialog("Save ABS baseline", "ABS_Baseline", "json", "ABS baseline (*.json)|*.json");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RequireNewFile(dialog.FileName);
                if (!BeginOperation(false, "Saving ABS baseline...")) return;
                string notes = captureNotesTextBox.Text;
                try
                {
                    await Task.Run(() => AbsDiagnosticCaptureFile.SaveBaseline(dialog.FileName, baseline, notes), lifetimeCts.Token);
                    if (!IsClosing) statusLabel.Text = $"Baseline saved: {dialog.FileName}";
                }
                finally { EndOperation(); }
            }
            catch (OperationCanceledException) when (IsClosing) { }
            catch (Exception ex) { if (!IsClosing) ShowError("Save ABS baseline", ex.Message); }
        }

        private async void startDiagnosticButton_Click(object sender, EventArgs e)
        {
            if (!DeviceAvailable) return;
            try
            {
                using var dialog = SaveDialog("Save a new ABS diagnostic capture", "ABS_Diagnostics", "jsonl",
                    "ABS diagnostic capture (*.jsonl)|*.jsonl");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RequireNewFile(dialog.FileName);
                if (!BeginOperation(true, "Starting diagnostic capture...")) return;
                ClearOfflineReview();
                int interval = (int)diagnosticIntervalNumeric.Value;
                string notes = captureNotesTextBox.Text;
                diagnosticCapturePath = dialog.FileName;
                stoppingDiagnostics = false;
                monitorGeneration++;
                liveStateListView.Items.Clear();
                try
                {
                    await Task.Run(() => Service.StartDiagnosticMonitor(dialog.FileName, interval, notes), lifetimeCts.Token);
                    if (!IsClosing && IsMonitoringDiagnostics)
                    {
                        captureStatusLabel.Text = $"Capturing every {interval} ms: {diagnosticCapturePath}";
                        statusLabel.Text = "Diagnostic capture started. Raw exchanges are saved as they arrive.";
                    }
                }
                finally { EndOperation(); }
            }
            catch (OperationCanceledException) when (IsClosing) { }
            catch (Exception ex) { if (!IsClosing) ShowError("Start ABS capture", ex.Message); }
        }

        private async void stopDiagnosticButton_Click(object sender, EventArgs e)
        {
            if (operationBusy || stoppingDiagnostics || !IsMonitoringDiagnostics || IsClosing) return;
            operationBusy = true;
            stoppingDiagnostics = true;
            monitorGeneration++;
            UpdateUIState();
            statusLabel.Text = "Stopping diagnostic capture...";
            try
            {
                await Task.Run(Service.StopDiagnosticMonitor);
                if (!IsClosing)
                {
                    stoppingDiagnostics = IsMonitoringDiagnostics;
                    captureStatusLabel.Text = stoppingDiagnostics
                        ? $"Stopping capture; waiting for the driver: {diagnosticCapturePath}"
                        : $"Capture stopped: {diagnosticCapturePath}";
                    statusLabel.Text = stoppingDiagnostics
                        ? "Stopping diagnostic capture; waiting for the driver to release the device."
                        : "Capture stopped. Open Capture to review or export the saved data.";
                }
            }
            catch (Exception ex)
            {
                stoppingDiagnostics = false;
                if (!IsClosing) ShowError("Stop ABS capture", ex.Message);
            }
            finally { EndOperation(); }
        }

        private void OnDiagnosticSampleReceived(object? sender, AbsDiagnosticSample sample)
        {
            int generation = monitorGeneration;
            PostToUi(() =>
            {
                if (generation != monitorGeneration || !IsMonitoringDiagnostics || stoppingDiagnostics) return;
                Populate(liveStateListView, sample.Rows);
                captureStatusLabel.Text = $"Latest sample {sample.TimestampUtc:HH:mm:ss.fff} UTC, {sample.ElapsedMilliseconds:F0} ms elapsed — {diagnosticCapturePath}";
            });
        }

        private void OnDiagnosticMonitorError(object? sender, string message)
        {
            int generation = monitorGeneration;
            PostToUi(() =>
            {
                if (generation != monitorGeneration) return;
                stoppingDiagnostics = false;
                UpdateUIState();
                if (string.IsNullOrEmpty(message))
                {
                    captureStatusLabel.Text = $"Capture stopped: {diagnosticCapturePath}";
                    statusLabel.Text = "Capture stopped. Open Capture to review or export the saved data.";
                }
                else
                {
                    captureStatusLabel.Text = $"Capture stopped after an error: {diagnosticCapturePath}";
                    ShowError("ABS diagnostic capture", message);
                }
            });
        }

        private async void openCaptureButton_Click(object sender, EventArgs e)
        {
            if (!FilesAvailable) return;
            using var dialog = new OpenFileDialog
            {
                Title = "Review a saved ABS capture or baseline", InitialDirectory = LoggerPaths.OutputDirectory,
                Filter = "ABS captures and baselines (*.jsonl;*.json)|*.jsonl;*.json|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || !BeginOperation(false, "Loading saved ABS capture...")) return;
            try
            {
                var document = await Task.Run(() => AbsDiagnosticCaptureFile.Load(dialog.FileName), lifetimeCts.Token);
                if (IsClosing) return;
                reviewedCapture = document;
                reviewedCapturePath = dialog.FileName;
                captureNotesTextBox.Text = document.Notes;
                changingReview = true;
                try
                {
                    reviewSampleNumeric.Maximum = Math.Max(1, document.Samples.Count);
                    reviewSampleNumeric.Value = Math.Max(1, document.Samples.Count);
                }
                finally { changingReview = false; }
                reviewCountLabel.Text = $"of {document.Samples.Count:N0} samples";
                if (document.Samples.Count > 0) ShowReviewedSample();
                else ShowReviewedBaseline();
                statusLabel.Text = $"Opened saved data: {reviewedCapturePath}";
            }
            catch (OperationCanceledException) when (IsClosing) { }
            catch (Exception ex) { if (!IsClosing) ShowError("Open ABS capture", ex.Message); }
            finally { EndOperation(); }
        }

        private void reviewSampleNumeric_ValueChanged(object sender, EventArgs e)
        {
            if (!changingReview && FilesAvailable) ShowReviewedSample();
        }

        private void ShowReviewedSample()
        {
            if (reviewedCapture is null || reviewedCapture.Samples.Count == 0) return;
            int index = (int)reviewSampleNumeric.Value - 1;
            var sample = reviewedCapture.Samples[index];
            reviewingBaseline = false;
            reviewBaselineButton.Text = "View Baseline";
            Populate(liveStateListView, sample.Rows);
            captureStatusLabel.Text = $"Saved sample {index + 1:N0}: {sample.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff} UTC, {sample.ElapsedMilliseconds:F0} ms — {reviewedCapturePath}";
        }

        private void reviewBaselineButton_Click(object sender, EventArgs e)
        {
            if (!FilesAvailable) return;
            if (reviewingBaseline && reviewedCapture?.Samples.Count > 0) ShowReviewedSample();
            else ShowReviewedBaseline();
        }

        private void ShowReviewedBaseline()
        {
            reviewingBaseline = true;
            reviewBaselineButton.Text = reviewedCapture?.Samples.Count > 0 ? "View Sample" : "View Baseline";
            Populate(liveStateListView, reviewedCapture?.Baseline?.Rows ??
                [new AbsReportRow("Saved capture", "No baseline or samples")]);
            captureStatusLabel.Text = $"Saved baseline: {reviewedCapturePath}";
        }

        private async void exportCaptureButton_Click(object sender, EventArgs e)
        {
            if (!FilesAvailable || reviewedCapture is not { } document) return;
            try
            {
                using var dialog = SaveDialog("Export reviewed ABS capture", "ABS_Diagnostic_Export", "csv", "CSV files (*.csv)|*.csv");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RequireNewFile(dialog.FileName);
                if (!BeginOperation(false, "Exporting saved ABS capture...")) return;
                var exportDocument = document with { Notes = captureNotesTextBox.Text };
                try
                {
                    await Task.Run(() => AbsDiagnosticCaptureFile.ExportCsv(dialog.FileName, exportDocument), lifetimeCts.Token);
                    if (!IsClosing) statusLabel.Text = $"CSV exported: {dialog.FileName}";
                }
                finally { EndOperation(); }
            }
            catch (OperationCanceledException) when (IsClosing) { }
            catch (Exception ex) { if (!IsClosing) ShowError("Export ABS capture", ex.Message); }
        }

        private async void startTelemetryButton_Click(object sender, EventArgs e)
        {
            if (!BeginOperation(true, "Starting passive broadcast monitor...")) return;
            string? csvPath = logTelemetryCheckBox.Checked
                ? LoggerPaths.UniquePath(LoggerPaths.TimestampedCsvPath("ABS_Telemetry")) : null;
            stoppingTelemetry = false;
            monitorGeneration++;
            try
            {
                await Task.Run(() => Service.StartTelemetryMonitor(csvPath), lifetimeCts.Token);
                if (!IsClosing && IsMonitoringTelemetry)
                {
                    telemetryListView.Items.Clear();
                    statusLabel.Text = csvPath is null ? "Monitoring passive broadcasts; decoding is provisional."
                        : $"Provisional broadcast monitor — saving to {csvPath}";
                }
            }
            catch (OperationCanceledException) when (IsClosing) { }
            catch (Exception ex) { if (!IsClosing) ShowError("ABS broadcast monitor", ex.Message); }
            finally { EndOperation(); }
        }

        private async void stopTelemetryButton_Click(object sender, EventArgs e)
        {
            if (operationBusy || stoppingTelemetry || !IsMonitoringTelemetry || IsClosing) return;
            operationBusy = true;
            stoppingTelemetry = true;
            monitorGeneration++;
            UpdateUIState();
            statusLabel.Text = "Stopping passive broadcast monitor...";
            try
            {
                await Task.Run(Service.StopTelemetryMonitor);
                if (!IsClosing)
                {
                    stoppingTelemetry = IsMonitoringTelemetry;
                    statusLabel.Text = stoppingTelemetry
                        ? "Stopping passive broadcast monitor; waiting for the driver to release the device."
                        : "Passive broadcast monitor stopped.";
                }
            }
            catch (Exception ex)
            {
                stoppingTelemetry = false;
                if (!IsClosing) ShowError("Stop ABS broadcast monitor", ex.Message);
            }
            finally { EndOperation(); }
        }

        private void OnTelemetryReceived(object? sender, AbsTelemetrySample sample)
        {
            int generation = monitorGeneration;
            PostToUi(() =>
            {
                if (generation == monitorGeneration && IsMonitoringTelemetry && !stoppingTelemetry)
                    Populate(telemetryListView, DescribeTelemetry(sample));
            });
        }

        private void OnTelemetryError(object? sender, string message)
        {
            int generation = monitorGeneration;
            PostToUi(() =>
            {
                if (generation != monitorGeneration) return;
                stoppingTelemetry = false;
                UpdateUIState();
                if (string.IsNullOrEmpty(message))
                    statusLabel.Text = "Passive broadcast monitor stopped.";
                else
                    ShowError("ABS broadcast monitor", message);
            });
        }

        private static IReadOnlyList<AbsReportRow> DescribeTelemetry(AbsTelemetrySample s) =>
        [
            new("Broadcast decoding", "Provisional", "Layout and scale are unverified; use diagnostic Live Data for comparison."),
            Wheel("Left front", s.WheelLf), Wheel("Right front", s.WheelRf),
            Wheel("Left rear", s.WheelLr), Wheel("Right rear", s.WheelRr),
            Wheel("Vehicle speed", s.VehicleSpeedRaw),
            new("Brake switch", s.BrakeSwitch is int b ? AbsTelemetrySample.BrakeSwitchName(b) : "—",
                s.BrakeSwitch is int raw ? $"raw {raw}; provisional" : "no 0x0A4 frame"),
            Flag("ESP active", s.EspActive), Flag("ABS active", s.AbsActive),
            Flag("Torque reduction requested", s.TorqueRequest), Flag("No intervention", s.NoIntervention),
            Flag("ESP warning lamp", s.EspWarning),
            new("Frame counters", $"0x0A2 {s.CounterA2} / 0x0A4 {s.CounterA4}",
                $"provisional checksum checks: 0x0A2 {s.ChecksumA2Ok}, 0x0A4 {s.ChecksumA4Ok}"),
            new("Raw 0x0A2", s.RawA2 ?? "—"),
            new("Raw 0x0A4", s.RawA4 ?? "—"),
            new("Raw 0x0A8", s.RawA8 ?? "—"),
            new("Last update", s.Timestamp.ToString("HH:mm:ss.fff")),
        ];

        private static AbsReportRow Wheel(string name, int? raw) => new(name,
            raw is int r ? $"raw {r}" : "—",
            raw is not null ? "Provisional broadcast layout; physical units unverified"
                : "no frame / decoder reports invalid");
        private static AbsReportRow Flag(string name, bool? value) => new(name,
            value is null ? "—" : value.Value ? "YES" : "no", "provisional broadcast interpretation");

        private void pumpOperatorCheckBox_CheckedChanged(object sender, EventArgs e) => UpdateUIState();

        private async void runRoutineButton_Click(object sender, EventArgs e)
        {
            bool operatorConfirmed = pumpOperatorCheckBox.Checked;
            if (!DeviceAvailable || !operatorConfirmed) return;
            string capturePath;
            try
            {
                capturePath = LoggerPaths.UniquePath(LoggerPaths.TimestampedPath("ABS_Pump", "jsonl"));
                LoggerPaths.EnsureParentDirectory(capturePath);
                RequireNewFile(capturePath);
            }
            catch (Exception ex)
            {
                ShowError("Pump test journal", ex.Message);
                return;
            }

            if (!BeginOperation(true, "Checking pump test prerequisites...")) return;
            int seconds = (int)durationNumeric.Value;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            pumpCts = cancellation;
            runRoutineButton.Text = "Running...";
            actuationProgressLabel.Text = "Checking prerequisites before activation...";
            Populate(actuationListView, [PumpCaptureRow(capturePath)]);
            UpdateUIState();
            var progress = new Progress<AbsRoutineProgress>(update =>
            {
                if (IsClosing || !ReferenceEquals(pumpCts, cancellation)) return;
                string elapsed = update.TotalSeconds > 0 && update.ElapsedSeconds > 0
                    ? $" ({update.ElapsedSeconds:F1} / {update.TotalSeconds:F1} s)" : "";
                actuationProgressLabel.Text = update.Phase + elapsed;
                if (update.Rows.Count > 0)
                    Populate(actuationListView, update.Rows.Append(PumpCaptureRow(capturePath)));
            });
            try
            {
                // The service performs the identity/live-data gates and owns independent cleanup.
                // Let it observe cancellation itself; cancelling Task.Run must not skip that cleanup.
                var (success, error, result) = await Task.Run(() =>
                    Service.RunPumpCycle(seconds, operatorConfirmed, capturePath, progress, cancellation.Token));
                if (IsClosing) return;
                Populate(actuationListView, result.Rows.Append(PumpCaptureRow(capturePath)));
                ShowPumpResult(success, error, result, capturePath);
            }
            catch (Exception ex)
            {
                if (!IsClosing)
                {
                    const string outcome = "Pump result and shutdown are unconfirmed. Turn the ignition off and check the unit.";
                    actuationProgressLabel.Text = outcome;
                    Populate(actuationListView,
                        [new AbsReportRow("Pump test error", ex.Message), PumpCaptureRow(capturePath)]);
                    ShowError("Pump test", outcome + "\r\n\r\n" + ex.Message);
                }
            }
            finally
            {
                pumpCts = null;
                if (!IsClosing)
                {
                    runRoutineButton.Text = "Run Pump Test";
                    pumpOperatorCheckBox.Checked = false;
                }
                EndOperation();
            }
        }

        private void ShowPumpResult(bool success, string error, AbsRoutineResult result, string capturePath)
        {
            bool commandsAcknowledged = result.OffCommandCompleted && result.StopConfirmed;
            string outcome;
            if (result.CleanupRequired && !commandsAcknowledged)
                outcome = "Pump shutdown commands are unconfirmed. Turn the ignition off and check the unit.";
            else if (!result.CleanupRequired)
                outcome = result.Cancelled ? "Pump test cancelled before activation."
                    : result.ActivationAttempted ? $"Pump ON was refused: {error}"
                    : $"Pump test not started: {error}";
            else
            {
                outcome = result.Cancelled ? "Pump test cancelled."
                    : success && result.Completed ? "Pump test finished." : $"Pump test ended with an error: {error}";
                outcome += " OFF command completed; stop request accepted. Physical motor state is not measured.";
            }
            if (result.ActivationAttempted && !result.SessionRestored)
                outcome += " The default diagnostic session was not restored.";
            actuationProgressLabel.Text = outcome;
            statusLabel.Text = outcome;
            if (result.CleanupRequired && !commandsAcknowledged)
                MessageBox.Show(this, outcome + "\r\n\r\n" + error +
                    $"\r\n\r\nRaw exchanges: {capturePath}", "Pump shutdown unconfirmed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static AbsReportRow PumpCaptureRow(string path) => new("Pump test journal", path,
            "Open this JSONL file in Live Data & Captures to review the raw exchanges.");

        private void stopRoutineButton_Click(object sender, EventArgs e)
        {
            if (IsClosing || pumpCts is not { } cancellation) return;
            cancellation.Cancel();
            actuationProgressLabel.Text = "Stop requested; waiting for OFF, stop and session-restoration attempts...";
            statusLabel.Text = actuationProgressLabel.Text;
        }

        private async void sniffBusButton_Click(object sender, EventArgs e)
        {
            var progress = StatusProgress();
            await RunOperation(sniffBusButton, "Sniffing...", "Listening for external tester traffic...",
                "ABS bus sniff", infoListView, () =>
                {
                    var (ok, error, result) = Service.SniffBus(40, progress);
                    var rows = new List<AbsReportRow> { new("Baseline", $"{result.BaselineIdCount} periodic ids") };
                    rows.AddRange(result.NewIds.Select(id => new AbsReportRow("New id", id)));
                    rows.Add(new("Frames", $"{result.Frames.Count} captured", "see abs-sniff.txt"));
                    if (ok)
                    {
                        string path = Path.Combine(LoggerPaths.OutputDirectory, "abs-sniff.txt");
                        LoggerPaths.EnsureParentDirectory(path);
                        File.WriteAllLines(path, new[] { $"# ABS bus sniff — {DateTime.Now:O}" }
                            .Concat(result.NewIds).Concat(result.Frames));
                    }
                    return (ok, error, rows);
                }, "Bus sniff complete — saved to Documents\\LotusECMLogger\\abs-sniff.txt");
        }
    }
}
