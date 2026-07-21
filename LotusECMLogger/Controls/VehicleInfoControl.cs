using LotusECMLogger.Services;
using System.ComponentModel;

namespace LotusECMLogger.Controls
{
    public partial class VehicleInfoControl : UserControl
    {
        private readonly IVehicleInfoService _vehicleInfoService;
        private readonly IObdResetService _resetService;
        private readonly IVinSetService _vinSetService;
        private readonly IT6RMAService _rmaService;
        private readonly IHighSpeedLogService _highSpeedLogService;
        private readonly IDynoModeService _dynoModeService;
        private List<VehicleParameterReading> vehicleDataSnapshot = [];

        private enum EcuUnlockState { Unknown, Locked, Unlocked }
        private enum HighSpeedState { Unknown, Unavailable, Available }

        private bool _isLoggerActive;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLoggerActive
        {
            get => _isLoggerActive;
            set
            {
                _isLoggerActive = value;
                resetButton.Enabled = !value;
                setVinButton.Enabled = !value;
                dynoModeButton.Enabled = !value;
            }
        }

        public VehicleInfoControl()
        {
            InitializeComponent();
            _vehicleInfoService = new VehicleInfoService();
            _resetService = new J2534ObdResetService();
            _vinSetService = new J2534VinSetService();
            _rmaService = new T6RMAService();
            _highSpeedLogService = new HighSpeedLogService();
            _dynoModeService = new J2534DynoModeService();
            SetupListViewColumns();
            SetUnlockIndicator(EcuUnlockState.Unknown);
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
            if (_isLoggerActive)
            {
                MessageBox.Show("Cannot set VIN while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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
            if (_isLoggerActive)
            {
                MessageBox.Show("Cannot perform reset while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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
                resetButton.Enabled = !_isLoggerActive;
                resetButton.Text = "Perform Reset";
            }
        }

        private async void dynoModeButton_Click(object? sender, EventArgs e)
        {
            if (_isLoggerActive)
            {
                MessageBox.Show("Cannot enable dyno mode while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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
                dynoModeButton.Enabled = !_isLoggerActive;
                dynoModeButton.Text = "Dyno Mode";
            }
        }

        private async Task LoadVehicleDataAsync()
        {
            readDataButton.Enabled = false;
            readDataButton.Text = "Loading...";
            SetUnlockIndicator(EcuUnlockState.Unknown);
            SetHighSpeedIndicator(HighSpeedState.Unknown);

            // Snapshot UI/shared state on the UI thread; the worker must not read instance fields.
            bool loggerActive = _isLoggerActive;

            try
            {
                // All blocking J2534 work runs off the UI thread so the window stays responsive.
                var (readings, unlockState, highSpeedState) =
                    await Task.Run(() => GatherVehicleData(loggerActive));

                // Resumes on the UI thread — safe to touch controls and the shared field.
                vehicleDataSnapshot = readings;
                UpdateVehicleInfoView();
                SetUnlockIndicator(unlockState);
                SetHighSpeedIndicator(highSpeedState);

                MessageBox.Show($"Successfully loaded {readings.Count} vehicle data points!",
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetUnlockIndicator(EcuUnlockState.Unknown);
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
        private (List<VehicleParameterReading> Readings, EcuUnlockState UnlockState, HighSpeedState HighSpeedState)
            GatherVehicleData(bool loggerActive)
        {
            // Read all ECU identification and learned data. The service opens and disposes its own
            // ISO15765 session, so the probes below (which open their own separate CAN sessions)
            // run only after that session is closed.
            var readings = _vehicleInfoService.LoadVehicleData();

            // Probe ECU unlock state via raw-CAN RMA (separate channel, so run only after the
            // ISO15765 session above has been disposed). The successful data load above doubles
            // as the liveness check: data present + no RMA reply => genuinely locked; no data at
            // all => ECU not reachable, so leave the state Unknown.
            var unlockState = ProbeUnlockState(loggerActive, ecuAlive: readings.Count > 0);

            // Probe whether the high-speed channel logger is present, using the same open-session +
            // identify handshake as the High-Speed Log tab's Test Connection button. Opens its own
            // temporary CAN session, so it runs after the channels above are closed.
            var highSpeedState = ProbeHighSpeedState(loggerActive, ecuAlive: readings.Count > 0);

            return (readings, unlockState, highSpeedState);
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

        // Runs the raw-CAN RMA unlock probe and maps the result to an unlock state. Pure I/O + logic,
        // no UI — runs on the worker thread; the caller renders the result via SetUnlockIndicator.
        // A reply => Unlocked; no reply but vehicle data did load (ECU is alive) => Locked;
        // nothing loaded at all => Unknown (ECU not reachable, not necessarily locked).
        private EcuUnlockState ProbeUnlockState(bool loggerActive, bool ecuAlive)
        {
            // Skip while logging holds the J2534 device; the probe opens its own CAN channel.
            if (loggerActive)
                return EcuUnlockState.Unknown;

            try
            {
                bool unlocked = _rmaService.IsEcuUnlocked();
                return unlocked ? EcuUnlockState.Unlocked
                     : ecuAlive ? EcuUnlockState.Locked
                     : EcuUnlockState.Unknown;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unlock probe failed: {ex.Message}");
                return EcuUnlockState.Unknown;
            }
        }

        // Probes the ECU's high-speed channel logger with the open-session + identify handshake. Pure
        // I/O + logic, no UI — runs on the worker thread. The logger answering identify with its
        // capability magic => Available; the diagnostic bus is alive but the logger does not respond
        // (or returns a different protocol) => Unavailable; the bus is unreachable => Unknown. The
        // open-session reply doubles as the logger's own liveness check, but a disabled logger and an
        // unreachable ECU both yield no reply — so when the vehicle-data load proved the ECU is alive
        // (ecuAlive), no session reply means the logger is genuinely Unavailable, not Unknown.
        private HighSpeedState ProbeHighSpeedState(bool loggerActive, bool ecuAlive)
        {
            // Skip while logging holds the J2534 device; the probe opens its own CAN session.
            if (loggerActive)
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

        private void SetUnlockIndicator(EcuUnlockState state)
        {
            switch (state)
            {
                case EcuUnlockState.Unlocked:
                    unlockIndicatorLabel.Text = "ECU: UNLOCKED";
                    unlockIndicatorLabel.BackColor = Color.FromArgb(46, 160, 67);
                    unlockIndicatorLabel.ForeColor = Color.White;
                    break;
                case EcuUnlockState.Locked:
                    unlockIndicatorLabel.Text = "ECU: LOCKED";
                    unlockIndicatorLabel.BackColor = Color.FromArgb(207, 34, 46);
                    unlockIndicatorLabel.ForeColor = Color.White;
                    break;
                default:
                    unlockIndicatorLabel.Text = "ECU: UNKNOWN";
                    unlockIndicatorLabel.BackColor = Color.Gainsboro;
                    unlockIndicatorLabel.ForeColor = Color.DimGray;
                    break;
            }
        }
    }
}
