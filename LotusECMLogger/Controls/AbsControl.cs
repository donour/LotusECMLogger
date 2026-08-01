using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Diagnostics UI for the Bosch ESP8 ABS/ESP module, covering the operations in
    /// <c>DIAGNOSTICS_PROGRAMMING_GUIDE.md</c>:
    ///
    /// <list type="bullet">
    /// <item>Module &amp; Faults — identification/coding records, fault codes, and the addressing
    /// discovery tools (probe / passive sniff).</item>
    /// <item>Live State — the module's internal RAM state (mu, EDC accumulators, valve positions,
    /// brake pressures) via ReadMemoryByAddress.</item>
    /// <item>Telemetry — passive decoding of the 100 Hz wheel-speed / status broadcasts.</item>
    /// <item>Pump &amp; Valves — hydraulic actuation routines for brake bleeding and testing.</item>
    /// </list>
    ///
    /// Everything except the actuation tab is read-only. No service that alters persistent module
    /// state (variant recoding, memory write, DTC clear) is offered.
    /// </summary>
    public partial class AbsControl : UserControl
    {
        private readonly IAbsService absService;
        private bool isLoggerActive;
        private CancellationTokenSource? routineCts;

        public AbsControl(IAbsService absService)
        {
            this.absService = absService ?? throw new ArgumentNullException(nameof(absService));
            this.absService.TelemetryReceived += OnTelemetryReceived;
            this.absService.TelemetryError += OnTelemetryError;

            InitializeComponent();
            SetupListViewColumns();
            PopulateRoutines();

            GuiIcons.ApplyToButton(readInfoButton, GuiIcons.Dtc);
            GuiIcons.ApplyToButton(moduleInfoButton, GuiIcons.VehicleInfo);
            GuiIcons.ApplyToButton(testConnectionButton, GuiIcons.Connect);
            GuiIcons.ApplyToButton(sniffBusButton, GuiIcons.LiveData);
            GuiIcons.ApplyToButton(readLiveStateButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(startTelemetryButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopTelemetryButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(checkPreconditionsButton, GuiIcons.Connect);
            GuiIcons.ApplyToButton(runRoutineButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopRoutineButton, GuiIcons.Stop);
        }

        partial void DisposeManaged()
        {
            absService.TelemetryReceived -= OnTelemetryReceived;
            absService.TelemetryError -= OnTelemetryError;
            routineCts?.Cancel();
            (absService as IDisposable)?.Dispose();
        }

        /// <summary>
        /// True while the main logger is running. Every ABS operation opens its own J2534 session,
        /// which cannot coexist with active logging, so the actions are disabled meanwhile.
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
            foreach (var list in new[] { infoListView, liveStateListView, telemetryListView, actuationListView })
            {
                list.Columns.Clear();
                list.Columns.Add("Field", 230);
                list.Columns.Add("Value", 260);
                list.Columns.Add("Detail", 420);
            }
        }

        /// <summary>Selectable actuation routines, plus the guide's full 3-phase bleeding sequence.</summary>
        private sealed record RoutineChoice(byte? Type, string Label, int DefaultSeconds, string Description)
        {
            /// <summary>The bleed sequence runs its own fixed phase durations, so it has no routine type.</summary>
            public bool IsBleedSequence => Type is null;

            public override string ToString() => Label;
        }

        private void PopulateRoutines()
        {
            routineComboBox.Items.Add(new RoutineChoice(null, "Full bleed sequence (3 phases)",
                AbsProtocol.BleedSequence.Sum(p => p.Seconds),
                "Runs bleed circulation (30 s), pressure hold (10 s), then a quick valve cycle (5 s)."));

            foreach (var routine in AbsProtocol.Routines)
                routineComboBox.Items.Add(new RoutineChoice(routine.Type,
                    $"{routine.Name} (0x{routine.Type:X2})", routine.DefaultSeconds, routine.Description));

            routineComboBox.SelectedIndex = 0;
        }

        private RoutineChoice? SelectedRoutine => routineComboBox.SelectedItem as RoutineChoice;

        private void routineComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SelectedRoutine is not RoutineChoice choice)
                return;

            // The bleed sequence's phase durations are fixed by the guide, so the duration box only
            // applies to a single routine.
            durationNumeric.Enabled = !choice.IsBleedSequence;
            durationNumeric.Value = Math.Clamp(choice.DefaultSeconds,
                (int)durationNumeric.Minimum, (int)durationNumeric.Maximum);
            actuationProgressLabel.Text = choice.Description;
        }

        // ── Shared UI state ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Every action opens its own J2534 session and none may overlap, so they are enabled and
        /// disabled together. Telemetry monitoring holds the device, so it disables the rest too.
        /// </summary>
        private void SetActionsEnabled(bool enabled)
        {
            bool on = enabled && !isLoggerActive && !absService.IsMonitoringTelemetry;

            testConnectionButton.Enabled = on;
            readInfoButton.Enabled = on;
            moduleInfoButton.Enabled = on;
            sniffBusButton.Enabled = on;
            readLiveStateButton.Enabled = on;
            checkPreconditionsButton.Enabled = on;
            runRoutineButton.Enabled = on;
            routineComboBox.Enabled = on;
            durationNumeric.Enabled = on && SelectedRoutine?.IsBleedSequence == false;

            // The telemetry monitor is the one action that stays available while it owns the device.
            startTelemetryButton.Enabled = enabled && !isLoggerActive && !absService.IsMonitoringTelemetry;
            stopTelemetryButton.Enabled = absService.IsMonitoringTelemetry;
            logTelemetryCheckBox.Enabled = !absService.IsMonitoringTelemetry;
        }

        private void UpdateUIState()
        {
            SetActionsEnabled(true);
            if (isLoggerActive)
                statusLabel.Text = "Stop logging to use the ABS module.";
        }

        // The results grids can't be copied out of the window, so mirror every run to a text file
        // (overwritten each time) for out-of-band review. Best-effort — never fails the operation.
        private static readonly string DiagnosticsLogPath =
            Path.Combine(LoggerPaths.OutputDirectory, "abs-diagnostics.txt");

        private static void WriteDiagnosticsLog(string title, IEnumerable<AbsReportRow> rows)
        {
            try
            {
                LoggerPaths.EnsureParentDirectory(DiagnosticsLogPath);
                var lines = new List<string> { $"# {title} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
                foreach (var row in rows)
                    lines.Add($"{row.Field}\t{row.Value}\t{row.Detail}");
                File.WriteAllText(DiagnosticsLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch
            {
                // Diagnostics logging is best-effort; ignore file/IO errors.
            }
        }

        private static void Populate(ListView list, IEnumerable<AbsReportRow> rows)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Field);
                item.SubItems.Add(row.Value);
                item.SubItems.Add(row.Detail);
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        /// <summary>
        /// Runs an ABS operation on a worker thread with the action buttons disabled, shows its rows,
        /// and mirrors them to the diagnostics log. Returns false when the operation reported failure.
        /// </summary>
        private async Task<bool> RunOperation(
            Button button, string busyText, string busyStatus, string title, ListView target,
            Func<(bool success, string errorMessage, IReadOnlyList<AbsReportRow> rows)> operation,
            string successStatus)
        {
            if (isLoggerActive)
                return false;

            string originalText = button.Text;
            SetActionsEnabled(false);
            button.Text = busyText;
            statusLabel.Text = busyStatus;
            target.Items.Clear();

            try
            {
                var (success, errorMessage, rows) = await Task.Run(operation);

                if (!success && rows.Count == 0)
                {
                    statusLabel.Text = $"{title} failed — saved to abs-diagnostics.txt";
                    WriteDiagnosticsLog($"{title} FAILED", [new AbsReportRow("Error", errorMessage)]);
                    MessageBox.Show($"{title} failed:\n\n{errorMessage}", "ABS",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                Populate(target, rows);
                WriteDiagnosticsLog(success ? title : $"{title} FAILED", rows);

                // A failure that still produced rows (e.g. refused preconditions) shows them and
                // explains itself in the status line rather than hiding the detail behind a dialog.
                statusLabel.Text = success
                    ? successStatus
                    : $"{title}: {errorMessage}";
                return success;
            }
            finally
            {
                button.Text = originalText;
                SetActionsEnabled(true);
            }
        }

        private const string SavedToLog = "saved to Documents\\LotusECMLogger\\abs-diagnostics.txt";

        // ── Module & faults ─────────────────────────────────────────────────────────────

        private async void testConnectionButton_Click(object sender, EventArgs e) =>
            await RunOperation(testConnectionButton, "Testing...", "Probing ABS addressing...",
                "ABS connection test", infoListView,
                () =>
                {
                    var (success, error, result) = absService.ProbeConnection();
                    return (success, error, result.Rows);
                },
                $"Probe complete — {SavedToLog}");

        private async void readInfoButton_Click(object sender, EventArgs e) =>
            await RunOperation(readInfoButton, "Reading...", "Reading ABS trouble codes...",
                "ABS DTC read", infoListView,
                () =>
                {
                    var (success, error, result) = absService.ReadDtcs();
                    return (success, error, result.Rows);
                },
                $"ABS trouble codes read — {SavedToLog}");

        private async void moduleInfoButton_Click(object sender, EventArgs e)
        {
            var progress = new Progress<string>(s => statusLabel.Text = s);
            await RunOperation(moduleInfoButton, "Reading...", "Reading ABS module info...",
                "ABS module info read", infoListView,
                () =>
                {
                    var (success, error, result) = absService.ReadModuleInfo(progress);
                    return (success, error, result.Fields);
                },
                $"ABS module info read — {SavedToLog}");
        }

        // ── Live state (§5) ─────────────────────────────────────────────────────────────

        private async void readLiveStateButton_Click(object sender, EventArgs e)
        {
            var progress = new Progress<string>(s => statusLabel.Text = s);
            await RunOperation(readLiveStateButton, "Reading...", "Reading ABS live state...",
                "ABS live state read", liveStateListView,
                () =>
                {
                    var (success, error, result) = absService.ReadLiveState(progress);
                    return (success, error, result.Rows);
                },
                $"ABS live state read — {SavedToLog}");
        }

        // ── Passive telemetry (§4) ──────────────────────────────────────────────────────

        private void startTelemetryButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive || absService.IsMonitoringTelemetry)
                return;

            string? csvPath = null;
            if (logTelemetryCheckBox.Checked)
                csvPath = LoggerPaths.UniquePath(LoggerPaths.TimestampedCsvPath("ABS_Telemetry"));

            try
            {
                absService.StartTelemetryMonitor(csvPath);
                telemetryListView.Items.Clear();
                statusLabel.Text = csvPath is null
                    ? "Monitoring ABS broadcasts (0x0A2 / 0x0A4 / 0x0A8)…"
                    : $"Monitoring ABS broadcasts — logging to {csvPath}";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Could not start telemetry monitor";
                MessageBox.Show($"Could not start the ABS telemetry monitor:\n\n{ex.Message}",
                    "ABS Telemetry", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            SetActionsEnabled(true);
        }

        private void stopTelemetryButton_Click(object sender, EventArgs e)
        {
            absService.StopTelemetryMonitor();
            statusLabel.Text = "Telemetry monitor stopped.";
            SetActionsEnabled(true);
        }

        private void OnTelemetryReceived(object? sender, AbsTelemetrySample sample)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                // The monitor fires at ~10 Hz; a dropped update during shutdown is harmless.
                try { BeginInvoke(() => OnTelemetryReceived(sender, sample)); } catch (ObjectDisposedException) { }
                return;
            }

            Populate(telemetryListView, DescribeTelemetry(sample));
        }

        private void OnTelemetryError(object? sender, string message)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(() => OnTelemetryError(sender, message)); } catch (ObjectDisposedException) { }
                return;
            }

            absService.StopTelemetryMonitor();
            SetActionsEnabled(true);
            statusLabel.Text = "Telemetry monitor stopped after an error.";
            MessageBox.Show($"ABS telemetry monitoring failed:\n\n{message}",
                "ABS Telemetry", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Renders a telemetry sample as report rows. Raw 14-bit counts are shown alongside km/h,
        /// because the raw values are exact while km/h depends on an ECU-side wheel multiplier that
        /// this module does not carry.
        /// </summary>
        private static IReadOnlyList<AbsReportRow> DescribeTelemetry(AbsTelemetrySample s)
        {
            var rows = new List<AbsReportRow>
            {
                Wheel("Left front", s.WheelLf),
                Wheel("Right front", s.WheelRf),
                Wheel("Left rear", s.WheelLr),
                Wheel("Right rear", s.WheelRr),
                Wheel("Vehicle speed", s.VehicleSpeedRaw),
                new("Brake switch",
                    s.BrakeSwitch is int b ? AbsTelemetrySample.BrakeSwitchName(b) : "—",
                    s.BrakeSwitch is int raw ? $"raw {raw}" : "no 0x0A4 frame"),
                Flag("ESP active", s.EspActive),
                Flag("ABS active", s.AbsActive),
                Flag("Torque reduction requested", s.TorqueRequest),
                Flag("No intervention", s.NoIntervention),
                Flag("ESP warning lamp", s.EspWarning),
                new("Frame counters",
                    $"0x0A2 {Counter(s.CounterA2)} / 0x0A4 {Counter(s.CounterA4)}",
                    $"checksums: 0x0A2 {Checksum(s.ChecksumA2Ok)}, 0x0A4 {Checksum(s.ChecksumA4Ok)}"),
                new("Last update", s.Timestamp.ToString("HH:mm:ss.fff")),
            };

            return rows;

            static AbsReportRow Wheel(string name, int? raw) => new(name,
                raw is int v ? $"{AbsTelemetrySample.ToKph(v):F1} km/h" : "—",
                raw is int r ? $"raw {r}" : "no frame / sensor invalid");

            static AbsReportRow Flag(string name, bool? value) => new(name,
                value is null ? "—" : value.Value ? "YES" : "no",
                value is null ? "no 0x0A8 frame" : "");

            static string Counter(int? value) => value?.ToString() ?? "—";

            static string Checksum(bool? ok) => ok is null ? "—" : ok.Value ? "OK" : "MISMATCH";
        }

        // ── Actuation (§9) ──────────────────────────────────────────────────────────────

        private async void checkPreconditionsButton_Click(object sender, EventArgs e) =>
            await RunOperation(checkPreconditionsButton, "Checking...", "Checking actuation preconditions...",
                "ABS precondition check", actuationListView,
                () =>
                {
                    var (success, error, result) = absService.CheckActuationPreconditions();
                    return (success, error, result.Rows);
                },
                "Precondition check complete — review the rows before running a routine.");

        private async void runRoutineButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive || SelectedRoutine is not RoutineChoice choice)
                return;

            int seconds = choice.IsBleedSequence ? choice.DefaultSeconds : (int)durationNumeric.Value;

            var confirm = MessageBox.Show(
                $"{choice.Label}\n\n{choice.Description}\n\n" +
                "This runs the ABS pump motor and solenoid valves, and moves brake fluid.\n\n" +
                "Confirm ALL of the following:\n" +
                "  • The vehicle is stationary and safely supported/chocked\n" +
                "  • Ignition is ON and the engine is OFF\n" +
                "  • The brake pedal is released\n" +
                "  • The reservoir is full and, when bleeding, a pressure bleeder is attached\n\n" +
                $"Estimated run time: {seconds} s. Continue?",
                "Run ABS Actuation Routine", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            routineCts?.Dispose();
            routineCts = new CancellationTokenSource();
            CancellationToken token = routineCts.Token;

            SetActionsEnabled(false);
            stopRoutineButton.Enabled = true;
            runRoutineButton.Text = "Running...";
            actuationListView.Items.Clear();
            statusLabel.Text = $"Running {choice.Label}…";

            // Progress<T> marshals to the UI thread, so the live poll rows can be shown directly.
            var progress = new Progress<AbsRoutineProgress>(p =>
            {
                actuationProgressLabel.Text =
                    $"{p.Phase} — {p.ElapsedSeconds:F0}/{p.TotalSeconds:F0} s";
                Populate(actuationListView, p.Rows);
            });

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => choice.IsBleedSequence
                    ? absService.RunBleedSequence(progress, token)
                    : absService.RunRoutine(choice.Type!.Value, seconds, progress, token));

                Populate(actuationListView, result.Rows);
                WriteDiagnosticsLog(success ? $"ABS actuation — {choice.Label}"
                                            : $"ABS actuation FAILED — {choice.Label}", result.Rows);

                if (success)
                {
                    actuationProgressLabel.Text = "Routine complete.";
                    statusLabel.Text = $"{choice.Label} completed — {SavedToLog}";
                }
                else
                {
                    actuationProgressLabel.Text = "Routine did not complete.";
                    statusLabel.Text = $"{choice.Label}: {errorMessage}";
                    MessageBox.Show($"{choice.Label} did not complete:\n\n{errorMessage}\n\n" +
                        "Any routine that was started has been stopped and the module returned to the " +
                        "default session.",
                        "ABS Actuation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                runRoutineButton.Text = "Run";
                stopRoutineButton.Enabled = false;
                SetActionsEnabled(true);
            }
        }

        private void stopRoutineButton_Click(object sender, EventArgs e)
        {
            // Cancellation is cooperative: the service breaks out of its poll loop and always sends
            // StopRoutine (0x32) plus a return to the default session before it returns.
            routineCts?.Cancel();
            stopRoutineButton.Enabled = false;
            statusLabel.Text = "Stopping routine…";
        }

        // ── Bus sniff ───────────────────────────────────────────────────────────────────

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

                var rows = new List<AbsReportRow>
                {
                    new("Baseline", $"{result.BaselineIdCount} periodic ids"),
                };
                rows.AddRange(result.NewIds.Select(id => new AbsReportRow("New id", id)));
                rows.Add(new AbsReportRow("Frames", $"{result.Frames.Count} captured", "see abs-sniff.txt"));
                Populate(infoListView, rows);

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
    }
}
