using LotusECMLogger.Services;
using System.ComponentModel;

namespace LotusECMLogger.Controls
{
    public partial class VehicleInfoControl : UserControl
    {
        private readonly IVehicleInfoService _vehicleInfoService;
        private readonly IObdResetService _resetService;
        private readonly IVinSetService _vinSetService;
        private IHighSpeedLogService _highSpeedLogService;
        private bool _ownsHighSpeedLogService = true;
        private readonly IDynoModeService _dynoModeService;
        private List<VehicleParameterReading> vehicleDataSnapshot = [];

        private enum HighSpeedState { Unknown, Unavailable, Available }

        // Whether someone else (OBD-II logging, the ABS probe or sniff, the T6 flasher) holds the whole

        // adapter; the broker refuses every other use meanwhile (J2534-session-broker-plan.md, Phase 7).

        private readonly AdapterAvailability adapter;
        /// <summary>
        /// The logger whose Identify backs the high-speed availability indicator. The default has its own CCP
        /// session; MainWindow replaces it with the logger on the application's shared session.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IHighSpeedLogService HighSpeedProbeService
        {
            get => _highSpeedLogService;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (_ownsHighSpeedLogService) _highSpeedLogService.Dispose();
                _highSpeedLogService = value;
                _ownsHighSpeedLogService = false;
            }
        }

        public VehicleInfoControl()
        {
            InitializeComponent();
            adapter = AdapterAvailability.Track(this, UpdateAdapterButtons);
            _vehicleInfoService = new VehicleInfoService();
            _resetService = new J2534ObdResetService();
            _vinSetService = new J2534VinSetService();
            _highSpeedLogService = new HighSpeedLogService();
            _dynoModeService = new J2534DynoModeService();
            SetupListViewColumns();
            SetHighSpeedIndicator(HighSpeedState.Unknown);
            GuiIcons.ApplyToButton(readDataButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(resetButton, GuiIcons.UpdateRestore);
            GuiIcons.ApplyToButton(setVinButton, GuiIcons.Write);
            GuiIcons.ApplyToButton(dynoModeButton, GuiIcons.DynoMode);
        }

        private void SetupListViewColumns()
        {
            vehicleInfoView.Columns.Clear();
            vehicleInfoView.Columns.Add("Parameter", 200);
            vehicleInfoView.Columns.Add("Value", 150);
            vehicleInfoView.Columns.Add("Unit", 150);
        }

        private async void readDataButton_Click(object sender, EventArgs e)
        {
            await LoadVehicleDataAsync();
        }

        private async void setVinButton_Click(object? sender, EventArgs e)
        {
            var currentVin = vehicleDataSnapshot
                .FirstOrDefault(r => r.Name == "Vehicle Identification Number")?.Value?.Trim();

            using var dialog = new SetVinDialog(_vinSetService, currentVin);
            var result = dialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                await LoadVehicleDataAsync();
            }
        }

        private void resetButton_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you need to perform an OBD-II learned data reset?\n\n" +
                "This operation cannot be reversed and may affect drivability until the ECU relearns.\n\n" +
                "Confirm to proceed.",
                "Confirm OBD-II Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                resetButton.Enabled = false;
                resetButton.Text = "Resetting...";

                var (success, error) = _resetService.PerformLearningReset();
                if (success)
                    MessageBox.Show("OBD-II learned data reset request sent successfully.", "Reset Sent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show($"Failed to send reset: {error}", "Reset Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                resetButton.Enabled = !adapter.Blocked;
                resetButton.Text = "Perform Reset";
            }
        }

        private async void dynoModeButton_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Enable dyno mode?\n\n" +
                "Dyno mode will inhibit ECU faults from external systems such as ABS.\n\n" +
                "Dyno mode can be disabled by powering off the vehicle.",
                "Confirm Dyno Mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                dynoModeButton.Enabled = false;
                dynoModeButton.Text = "Enabling...";

                var (success, error) = await Task.Run(() => _dynoModeService.EnableDynoMode());
                if (success)
                    MessageBox.Show("Dyno mode enabled.", "Dyno Mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show($"Failed to enable dyno mode: {error}", "Dyno Mode Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                dynoModeButton.Enabled = !adapter.Blocked;
                dynoModeButton.Text = "Dyno Mode";
            }
        }

        private async Task LoadVehicleDataAsync()
        {
            readDataButton.Enabled = false;
            readDataButton.Text = "Loading...";
            SetHighSpeedIndicator(HighSpeedState.Unknown);

            // Snapshot UI/shared state on the UI thread; the worker must not read instance fields.
            bool adapterBlocked = adapter.Blocked;

            try
            {
                // All blocking J2534 work runs off the UI thread so the window stays responsive.
                var (readings, highSpeedState) =
                    await Task.Run(() => GatherVehicleData(adapterBlocked));

                // Resumes on the UI thread — safe to touch controls and the shared field.
                vehicleDataSnapshot = readings;
                UpdateVehicleInfoView();
                SetHighSpeedIndicator(highSpeedState);

                MessageBox.Show($"Successfully loaded {readings.Count} vehicle data points!",
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetHighSpeedIndicator(HighSpeedState.Unknown);
                MessageBox.Show($"Error loading vehicle data: {ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                readDataButton.Enabled = true;
                readDataButton.Text = "Load Vehicle Data";
            }
        }

        // Runs on a thread-pool thread. Performs all J2534 I/O and returns the data; touches no UI
        // and no instance fields that the UI thread also uses, so it cannot race with the UI.
        private (List<VehicleParameterReading> Readings, HighSpeedState HighSpeedState)
            GatherVehicleData(bool adapterBlocked)
        {
            // Read all ECU identification and learned data. The service opens and disposes its own
            // ISO15765 session, so the probe below (which opens its own CAN session) runs only after
            // that session is closed.
            var readings = _vehicleInfoService.LoadVehicleData();

            // Probe whether the high-speed channel logger is present, using the same open-session +
            // identify handshake as the High-Speed Log tab's Test Connection button. Opens its own
            // temporary CAN session, so it runs after the channels above are closed.
            var highSpeedState = ProbeHighSpeedState(adapterBlocked, ecuAlive: readings.Count > 0);

            return (readings, highSpeedState);
        }

        /// <summary>The buttons that write to the ECU, off while someone else holds the whole adapter.</summary>
        private void UpdateAdapterButtons()
        {
            resetButton.Enabled = !adapter.Blocked;
            setVinButton.Enabled = !adapter.Blocked;
            dynoModeButton.Enabled = !adapter.Blocked;
        }

        private void UpdateVehicleInfoView()
        {
            vehicleInfoView.BeginUpdate();
            vehicleInfoView.Items.Clear();

            foreach (var reading in vehicleDataSnapshot)
            {
                var item = new ListViewItem(reading.Name);
                item.SubItems.Add(reading.Value);
                item.SubItems.Add(reading.Unit);

                vehicleInfoView.Items.Add(item);
            }

            vehicleInfoView.EndUpdate();
        }

        // Probes the ECU's high-speed channel logger with the open-session + identify handshake. Pure
        // I/O + logic, no UI — runs on the worker thread. The logger answering identify with its
        // capability magic => Available; the diagnostic bus is alive but the logger does not respond
        // (or returns a different protocol) => Unavailable; the bus is unreachable => Unknown. The
        // open-session reply doubles as the logger's own liveness check, but a disabled logger and an
        // unreachable ECU both yield no reply — so when the vehicle-data load proved the ECU is alive
        // (ecuAlive), no session reply means the logger is genuinely Unavailable, not Unknown.
        private HighSpeedState ProbeHighSpeedState(bool adapterBlocked, bool ecuAlive)
        {
            // Skip while someone else holds the whole adapter: the probe needs a CCP session, which would be refused.
            if (adapterBlocked)
                return HighSpeedState.Unknown;

            try
            {
                var result = _highSpeedLogService.Identify();
                if (!result.SessionOpened)
                    return ecuAlive ? HighSpeedState.Unavailable : HighSpeedState.Unknown;
                return result.IsChannelLogger ? HighSpeedState.Available : HighSpeedState.Unavailable;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"High-speed probe failed: {ex.Message}");
                return HighSpeedState.Unknown;
            }
        }

        private void SetHighSpeedIndicator(HighSpeedState state)
        {
            switch (state)
            {
                case HighSpeedState.Available:
                    highSpeedIndicatorLabel.Text = "HS LOGGER: AVAILABLE";
                    highSpeedIndicatorLabel.BackColor = Color.FromArgb(46, 160, 67);
                    highSpeedIndicatorLabel.ForeColor = Color.White;
                    break;
                case HighSpeedState.Unavailable:
                    highSpeedIndicatorLabel.Text = "HS LOGGER: UNAVAILABLE";
                    highSpeedIndicatorLabel.BackColor = Color.FromArgb(207, 34, 46);
                    highSpeedIndicatorLabel.ForeColor = Color.White;
                    break;
                default:
                    highSpeedIndicatorLabel.Text = "HS LOGGER: UNKNOWN";
                    highSpeedIndicatorLabel.BackColor = Color.Gainsboro;
                    highSpeedIndicatorLabel.ForeColor = Color.DimGray;
                    break;
            }
        }
    }
}
