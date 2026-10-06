using System.ComponentModel;
using System.Globalization;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// An OBD-II emissions pre-check: reads what an inspection station reads from the engine ECU
    /// (monitor readiness, MIL, DTCs, counters) and judges it against a chosen program's rules.
    /// Re-judging after a change of program or model year reuses the last read.
    /// </summary>
    public partial class EmissionsControl : UserControl
    {
        private const string UnknownModelYear = "Unknown";

        private static readonly EmissionsStandard[] Standards =
        [
            EmissionsStandard.CaliforniaSmogCheck,
            EmissionsStandard.FederalIm,
        ];

        private static readonly Color PassColor = Color.ForestGreen;
        private static readonly Color FailColor = Color.Firebrick;
        private static readonly Color WarnColor = Color.DarkOrange;

        /// <summary>
        /// Null only under the Windows Forms designer, which builds the control through the
        /// parameterless constructor. Use <see cref="Service"/> from anything a user can trigger.
        /// </summary>
        private readonly IEmissionsService? emissionsService;

        private EmissionsCheckResult? lastResult;
        private bool isLoggerActive;
        private bool isBusy;

        /// <summary>
        /// Design-time constructor: lays out the control and nothing else. It must not create or
        /// touch an <see cref="IEmissionsService"/> — that would open a J2534 device inside Visual Studio.
        /// </summary>
        public EmissionsControl()
        {
            InitializeComponent();
            SetupColumns();
            PopulateSelectors();
            GuiIcons.ApplyToButton(runCheckButton, GuiIcons.Emissions);
            noteLabel.Text = "Pre-inspection estimate from the engine ECU's OBD data. A station also " +
                "checks the MIL bulb and may query other modules (e.g. the transmission).";
            ShowVerdict(null);
        }

        public EmissionsControl(IEmissionsService emissionsService) : this()
        {
            this.emissionsService = emissionsService ?? throw new ArgumentNullException(nameof(emissionsService));
        }

        /// <summary>The service backing the check. Never reached at design time.</summary>
        private IEmissionsService Service => emissionsService
            ?? throw new InvalidOperationException("EmissionsControl was created without an IEmissionsService.");

        /// <summary>
        /// True while the main logger is running. The check opens its own J2534 session, which
        /// cannot coexist with active logging, so the action is disabled meanwhile.
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

        private void UpdateUIState()
        {
            runCheckButton.Enabled = !isLoggerActive && !isBusy && emissionsService != null;
        }

        private void SetupColumns()
        {
            monitorsListView.Columns.Clear();
            monitorsListView.Columns.Add("Monitor", 220);
            monitorsListView.Columns.Add("Type", 110);
            monitorsListView.Columns.Add("Supported", 80);
            monitorsListView.Columns.Add("Since Codes Cleared", 140);
            monitorsListView.Columns.Add("This Drive Cycle", 120);

            criteriaListView.Columns.Clear();
            criteriaListView.Columns.Add("Check", 150);
            criteriaListView.Columns.Add("Result", 70);
            criteriaListView.Columns.Add("Detail", 360);

            detailsListView.Columns.Clear();
            detailsListView.Columns.Add("Item", 200);
            detailsListView.Columns.Add("Value", 300);
        }

        private void PopulateSelectors()
        {
            standardComboBox.Items.AddRange(Standards.Select(s => (object)EmissionsCheckEvaluator.DisplayName(s)).ToArray());
            standardComboBox.SelectedIndex = 0;

            modelYearComboBox.Items.Add(UnknownModelYear);
            for (int year = DateTime.Now.Year + 1; year >= 1996; year--)
                modelYearComboBox.Items.Add(year.ToString(CultureInfo.InvariantCulture));
            modelYearComboBox.SelectedIndex = 0;
        }

        private EmissionsStandard SelectedStandard => Standards[Math.Max(standardComboBox.SelectedIndex, 0)];

        private int? SelectedModelYear =>
            int.TryParse(modelYearComboBox.SelectedItem as string, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
                ? year
                : null;

        private async void runCheckButton_Click(object sender, EventArgs e)
        {
            isBusy = true;
            UpdateUIState();
            runCheckButton.Text = "Checking...";
            statusLabel.Text = "Reading emissions data...";

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => Service.RunCheck());

                if (!success)
                {
                    statusLabel.Text = "Emissions check failed to run";
                    MessageBox.Show($"Failed to run the emissions check: {errorMessage}", "Emissions Check",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                lastResult = result;
                SelectModelYearFromVin(result.Vin);
                PopulateMonitors(result);
                Evaluate();

                statusLabel.Text = result.Warnings.Count == 0
                    ? $"Read at {DateTime.Now:T}"
                    : $"Read at {DateTime.Now:T} — {result.Warnings.Count} item(s) unavailable";
            }
            finally
            {
                isBusy = false;
                UpdateUIState();
                runCheckButton.Text = "Run Emissions Check";
            }
        }

        // The VIN is the only model-year source the ECU offers; when it does not decode, the
        // user's existing selection stands.
        private void SelectModelYearFromVin(string? vin)
        {
            if (EmissionsDecoder.ModelYearFromVin(vin) is not int year)
                return;
            int index = modelYearComboBox.Items.IndexOf(year.ToString(CultureInfo.InvariantCulture));
            if (index >= 0)
                modelYearComboBox.SelectedIndex = index;
        }

        private void standardComboBox_SelectedIndexChanged(object? sender, EventArgs e) => Evaluate();

        private void modelYearComboBox_SelectedIndexChanged(object? sender, EventArgs e) => Evaluate();

        /// <summary>Judges the last read against the selected program and model year.</summary>
        private void Evaluate()
        {
            if (lastResult is null)
                return;

            var verdict = EmissionsCheckEvaluator.Evaluate(lastResult, SelectedStandard, SelectedModelYear);
            ShowVerdict(verdict);
            PopulateCriteria(verdict);
            PopulateDetails(lastResult);
        }

        private void ShowVerdict(EmissionsVerdict? verdict)
        {
            (verdictLabel.Text, verdictLabel.ForeColor) = verdict?.Outcome switch
            {
                null => ("Run the check with the ignition on (engine running or off).", SystemColors.GrayText),
                EmissionsOutcome.Pass => ("PASS", PassColor),
                EmissionsOutcome.Fail => ("FAIL", FailColor),
                EmissionsOutcome.NotReady => ("NOT READY — drive the car to complete the monitors, then retest", WarnColor),
                _ => ("INCONCLUSIVE — see the checks below", SystemColors.GrayText),
            };
        }

        private void PopulateMonitors(EmissionsCheckResult result)
        {
            monitorsListView.BeginUpdate();
            monitorsListView.Items.Clear();

            foreach (var monitor in result.SinceClear.Monitors)
            {
                var item = new ListViewItem(monitor.Name);
                item.SubItems.Add(monitor.Kind == MonitorKind.Continuous ? "Continuous" : "Non-continuous");
                item.SubItems.Add(monitor.Supported ? "Yes" : "No");
                item.SubItems.Add(monitor.Readiness switch
                {
                    MonitorReadiness.Complete => "Complete",
                    MonitorReadiness.Incomplete => "Incomplete",
                    _ => "N/A",
                });
                item.SubItems.Add(DriveCycleText(monitor, result.ThisDriveCycle));
                item.ForeColor = monitor.Readiness switch
                {
                    MonitorReadiness.Complete => PassColor,
                    MonitorReadiness.Incomplete => WarnColor,
                    _ => SystemColors.GrayText,
                };
                monitorsListView.Items.Add(item);
            }

            monitorsListView.EndUpdate();
        }

        private static string DriveCycleText(ReadinessMonitor sinceClear, MonitorStatus? thisCycle)
        {
            if (!sinceClear.Supported)
                return "—";
            var monitor = thisCycle?.Monitors.FirstOrDefault(m => m.Id == sinceClear.Id);
            return monitor switch
            {
                null => "—",
                { Supported: false } => "Disabled",
                { Complete: true } => "Complete",
                _ => "Incomplete",
            };
        }

        private void PopulateCriteria(EmissionsVerdict verdict)
        {
            criteriaListView.BeginUpdate();
            criteriaListView.Items.Clear();

            foreach (var criterion in verdict.Criteria)
            {
                var item = new ListViewItem(criterion.Name);
                item.SubItems.Add(criterion.Passed switch { true => "Pass", false => "Fail", null => "Unknown" });
                item.SubItems.Add(criterion.Detail);
                item.ForeColor = criterion.Passed switch
                {
                    true => PassColor,
                    false => FailColor,
                    null => SystemColors.GrayText,
                };
                item.ToolTipText = criterion.Detail;
                criteriaListView.Items.Add(item);
            }

            criteriaListView.EndUpdate();
        }

        private void PopulateDetails(EmissionsCheckResult result)
        {
            detailsListView.BeginUpdate();
            detailsListView.Items.Clear();

            var status = result.SinceClear;
            AddDetail("VIN", result.Vin ?? "Unavailable");
            AddDetail("Model year (VIN)", EmissionsDecoder.ModelYearFromVin(result.Vin)?.ToString(CultureInfo.InvariantCulture) ?? "Unknown");
            AddDetail("Calibration ID", result.CalibrationIds.Count > 0 ? string.Join(", ", result.CalibrationIds) : "Unavailable");
            AddDetail("CVN", result.Cvns.Count > 0 ? string.Join(", ", result.Cvns.Select(c => c.ToString("X8"))) : "Unavailable");
            AddDetail("OBD standard",result.ObdStandard is byte std ? EmissionsDecoder.ObdStandardName(std) : "Unavailable");
            AddDetail("Ignition type", status.Ignition == IgnitionType.Spark ? "Spark" : "Compression");
            AddDetail("MIL", status.MilOn ? "ON" : "Off", status.MilOn ? FailColor : null);
            AddDetail("Confirmed DTCs (PID 0x01)", status.DtcCount.ToString(CultureInfo.InvariantCulture));

            foreach (var counter in result.Counters)
                AddDetail(counter.Name, FormatCounter(counter));

            AddDtcDetail("Stored DTCs", result.Dtcs.Stored, result.StoredDtcError);
            AddDtcDetail("Pending DTCs", result.Dtcs.Pending, result.Dtcs.PendingError);
            AddDtcDetail("Permanent DTCs", result.Dtcs.Permanent, result.Dtcs.PermanentError);

            foreach (var warning in result.Warnings)
                AddDetail("Unavailable", warning, SystemColors.GrayText);

            detailsListView.EndUpdate();
        }

        private void AddDetail(string name, string value, Color? color = null)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add(value);
            item.ToolTipText = $"{name}: {value}";
            if (color is Color c)
                item.ForeColor = c;
            detailsListView.Items.Add(item);
        }

        private void AddDtcDetail(string name, IReadOnlyList<DiagnosticTroubleCode> codes, string? error)
        {
            if (error != null)
                AddDetail(name, $"Unavailable ({error})", SystemColors.GrayText);
            else if (codes.Count == 0)
                AddDetail(name, "None");
            else
                AddDetail(name, string.Join(", ", codes.Select(c => c.Code)), FailColor);
        }

        private static string FormatCounter(EmissionsCounter counter) => counter.Unit switch
        {
            "km" => $"{counter.Value:N0} km ({counter.Value / 1.609344:N0} mi)",
            "min" => $"{counter.Value:N0} min ({counter.Value / 60} h {counter.Value % 60:00} m)",
            _ => counter.Value.ToString("N0", CultureInfo.CurrentCulture),
        };
    }
}
