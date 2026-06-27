using System.Collections.Concurrent;
using System.ComponentModel;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Spreadsheet-style UI for the T6e high-speed CAN channel logger. Channels come from JSON presets
    /// (<c>config/highSpeedLogger</c>); the user checks the ones to log and picks a per-channel rate,
    /// then Start programs and arms the ECU via <see cref="IHighSpeedLogService"/> and streams live
    /// values into the grid and a CSV file.
    /// </summary>
    public partial class HighSpeedLogControl : UserControl
    {
        private readonly IHighSpeedLogService service;

        private readonly ConcurrentDictionary<string, (double value, string unit)> latestValues = new();
        private int frameCount;
        private DateTime lastSampleTime;
        private DateTime lastUiUpdate = DateTime.MinValue;
        private bool hasPresets;
        private string currentEcuVersion = "";

        private bool isLoggerActive;

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

        public HighSpeedLogControl(IHighSpeedLogService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.service.DataReceived += OnDataReceived;
            this.service.ErrorOccurred += OnErrorOccurred;

            InitializeComponent();

            channelsGrid.RowsAdded += (_, _) => RefreshRowActionState();
            channelsGrid.RowsRemoved += (_, _) => RefreshRowActionState();
            channelsGrid.UserDeletingRow += ChannelsGrid_UserDeletingRow;

            foreach (var rate in HighSpeedLogPlanner.SupportedRatesHz)
                rateColumn.Items.Add(rate);

            csvPathTextBox.Text =
                $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog\\HighSpeed_{DateTime.Now:yyyyMMddTHHmmss}.csv";

            RefreshPresets();
            UpdateUIState();

            Dock = DockStyle.Fill;
        }

        partial void DisposeManaged()
        {
            service.DataReceived -= OnDataReceived;
            service.ErrorOccurred -= OnErrorOccurred;
            service.Dispose();
        }

        /// <summary>Populates the preset dropdown from the config directory.</summary>
        public void RefreshPresets()
        {
            presetComboBox.Items.Clear();
            var presets = HighSpeedLogPresetLoader.GetAvailablePresets();
            hasPresets = presets.Count > 0;

            if (!hasPresets)
            {
                presetComboBox.Items.Add("No presets found");
                presetComboBox.SelectedIndex = 0;
                presetComboBox.Enabled = false;
                channelsGrid.Rows.Clear();
                return;
            }

            foreach (var preset in presets)
                presetComboBox.Items.Add(preset);

            presetComboBox.Enabled = true;
            presetComboBox.SelectedIndex = 0; // triggers load
        }

        private void PresetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!hasPresets || presetComboBox.SelectedItem is not string name)
                return;
            LoadPreset(name);
            UpdateUIState();
        }

        private void LoadPreset(string presetName)
        {
            channelsGrid.Rows.Clear();

            HighSpeedLogPreset preset;
            try
            {
                preset = HighSpeedLogPresetLoader.LoadByName(presetName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preset '{presetName}': {ex.Message}",
                    "Preset Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            currentEcuVersion = preset.EcuVersion;
            foreach (var ch in preset.Channels)
                AddChannelRow(ch);

            if (preset.Warnings.Count > 0)
                MessageBox.Show(string.Join(Environment.NewLine, preset.Warnings),
                    "Preset Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Adds one channel row to the grid; returns false if its address is already present.</summary>
        private bool AddChannelRow(HighSpeedChannel ch)
        {
            foreach (DataGridViewRow existing in channelsGrid.Rows)
                if (existing.Tag is HighSpeedChannel c && c.Address == ch.Address)
                    return false;

            int idx = channelsGrid.Rows.Add();
            var row = channelsGrid.Rows[idx];
            row.Tag = ch;
            row.Cells[selectColumn.Index].Value = ch.DefaultSelected;
            row.Cells[nameColumn.Index].Value = ch.Name;
            row.Cells[addressColumn.Index].Value = $"0x{ch.Address:X8}";
            row.Cells[unitColumn.Index].Value = ch.Unit;
            row.Cells[rateColumn.Index].Value = SnapRate(ch.DefaultRate);
            row.Cells[valueColumn.Index].Value = string.Empty;
            return true;
        }

        private void AddChannelsButton_Click(object? sender, EventArgs e)
        {
            if (isLoggerActive || service.IsLogging)
                return;

            using var dialog = new AddChannelsDialog(string.IsNullOrWhiteSpace(currentEcuVersion) ? null : currentEcuVersion);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            int added = 0, skipped = 0;
            foreach (var ch in dialog.SelectedChannels)
            {
                if (AddChannelRow(ch)) added++;
                else skipped++;
            }

            RefreshRowActionState();
            statusValueLabel.Text = $"Added {added} channel(s)" + (skipped > 0 ? $", {skipped} already present" : "");
            statusValueLabel.ForeColor = Color.Black;
        }

        private void RemoveChannelButton_Click(object? sender, EventArgs e) => RemoveSelectedChannels();

        private void RemoveSelectedChannels()
        {
            if (service.IsLogging || isLoggerActive)
                return;

            var rows = channelsGrid.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show("Select one or more channel rows to remove.",
                    "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (var row in rows)
                channelsGrid.Rows.Remove(row);

            RefreshRowActionState();
            statusValueLabel.Text = $"Removed {rows.Count} channel(s)";
            statusValueLabel.ForeColor = Color.Black;
        }

        private void ClearChannelsButton_Click(object? sender, EventArgs e)
        {
            if (service.IsLogging || isLoggerActive || channelsGrid.Rows.Count == 0)
                return;

            var confirm = MessageBox.Show($"Remove all {channelsGrid.Rows.Count} channel(s)?",
                "Clear All Channels", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            channelsGrid.Rows.Clear();
            RefreshRowActionState();
            statusValueLabel.Text = "Cleared all channels";
            statusValueLabel.ForeColor = Color.Black;
        }

        private void ChannelsGrid_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
        {
            // Block keyboard (Delete-key) removal while logging; config is otherwise locked.
            if (service.IsLogging || isLoggerActive)
                e.Cancel = true;
        }

        /// <summary>Enables Start / Remove / Clear based on logging state and whether rows exist.</summary>
        private void RefreshRowActionState()
        {
            bool configurable = !service.IsLogging && !isLoggerActive;
            bool hasRows = channelsGrid.Rows.Count > 0;
            startButton.Enabled = configurable && hasRows;
            removeChannelButton.Enabled = configurable && hasRows;
            clearChannelsButton.Enabled = configurable && hasRows;
        }

        /// <summary>Commit checkbox/combobox edits immediately so a single click registers.</summary>
        private void ChannelsGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (channelsGrid.IsCurrentCellDirty)
                channelsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void ChannelsGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
        {
            // Ignore transient combo-box value mismatches.
            e.ThrowException = false;
        }

        private void BrowseCsvButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = csvPathTextBox.Text,
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                csvPathTextBox.Text = dialog.FileName;
        }

        private void StartButton_Click(object? sender, EventArgs e)
        {
            if (isLoggerActive)
            {
                MessageBox.Show("Cannot start high-speed logging while the main logger is active. Stop it first.",
                    "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selected = GatherSelectedChannels();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one channel to log.",
                    "No Channels", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string csvPath = csvPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                MessageBox.Show("Please specify a CSV output file path.",
                    "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirm = MessageBox.Show(
                "High-speed logging makes this PC an active node on the vehicle CAN bus at 500 kbit/s and " +
                "sends configuration commands to the ECU.\n\n" +
                "• Configure with the engine OFF and the vehicle stationary.\n" +
                "• The ECU diagnostic bus must be enabled (CAL_ecu_flexcan_diag_bus_select ≠ 0).\n\n" +
                "Proceed?",
                "Start High-Speed Logging", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK)
                return;

            try
            {
                latestValues.Clear();
                Interlocked.Exchange(ref frameCount, 0);
                framesValueLabel.Text = "0";

                service.StartLogging(selected, csvPath);

                statusValueLabel.Text = "Logging…";
                statusValueLabel.ForeColor = Color.Green;
                UpdateUIState();
            }
            catch (Exception ex)
            {
                statusValueLabel.Text = "Error";
                statusValueLabel.ForeColor = Color.Red;
                MessageBox.Show($"Failed to start high-speed logging: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateUIState();
            }
        }

        private void StopButton_Click(object? sender, EventArgs e)
        {
            try
            {
                service.StopLogging();
                statusValueLabel.Text = "Stopped";
                statusValueLabel.ForeColor = Color.Black;
                UpdateUIState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping logging: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void TestConnectionButton_Click(object? sender, EventArgs e)
        {
            if (isLoggerActive || service.IsLogging)
            {
                MessageBox.Show("Stop logging before testing the connection.",
                    "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The probe holds the J2534 device on its own session; block actions that also open it.
            testConnectionButton.Enabled = false;
            startButton.Enabled = false;
            statusValueLabel.Text = "Testing…";
            statusValueLabel.ForeColor = Color.Black;

            try
            {
                var result = await Task.Run(service.Identify);
                ShowIdentifyResult(result);
            }
            catch (Exception ex)
            {
                statusValueLabel.Text = "Test failed";
                statusValueLabel.ForeColor = Color.Red;
                MessageBox.Show($"Connection test failed: {ex.Message}",
                    "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Re-enable without clobbering the status message we just set.
                bool configurable = !service.IsLogging && !isLoggerActive;
                testConnectionButton.Enabled = configurable;
                addChannelsButton.Enabled = configurable;
                RefreshRowActionState();
            }
        }

        private void ShowIdentifyResult(HighSpeedIdentifyResult r)
        {
            if (!r.SessionOpened)
            {
                statusValueLabel.Text = "No response — check the diagnostic bus is enabled and the CAN wiring (500 kbit/s)";
                statusValueLabel.ForeColor = Color.Red;
            }
            else if (!r.Identified)
            {
                statusValueLabel.Text = "Diagnostic bus is alive, but the ECU did not answer identify";
                statusValueLabel.ForeColor = Color.DarkOrange;
            }
            else if (r.IsChannelLogger)
            {
                statusValueLabel.Text = $"Connected — channel logger present (proto {r.Magic:X8}, fw-id {r.FirmwareIdLength} B)";
                statusValueLabel.ForeColor = Color.Green;
            }
            else
            {
                statusValueLabel.Text = $"Connected, but unexpected protocol magic 0x{r.Magic:X8}";
                statusValueLabel.ForeColor = Color.DarkOrange;
            }
        }

        private List<(HighSpeedChannel channel, int rateHz)> GatherSelectedChannels()
        {
            var result = new List<(HighSpeedChannel, int)>();
            foreach (DataGridViewRow row in channelsGrid.Rows)
            {
                if (row.Tag is not HighSpeedChannel ch)
                    continue;
                if (row.Cells[selectColumn.Index].Value is not true)
                    continue;
                int rate = row.Cells[rateColumn.Index].Value is int r ? r : SnapRate(ch.DefaultRate);
                result.Add((ch, rate));
            }
            return result;
        }

        private void OnDataReceived(object? sender, HighSpeedSampleEventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            foreach (var reading in e.Readings)
                latestValues[reading.Name] = (reading.Value, reading.Unit);

            Interlocked.Increment(ref frameCount);
            lastSampleTime = e.Timestamp;

            // Throttle the cross-thread UI refresh; the grid is expensive to repaint.
            var now = DateTime.UtcNow;
            if ((now - lastUiUpdate).TotalMilliseconds < 100)
                return;
            lastUiUpdate = now;
            SafeUIInvoke(RefreshLiveUI);
        }

        private void RefreshLiveUI()
        {
            foreach (DataGridViewRow row in channelsGrid.Rows)
            {
                if (row.Tag is HighSpeedChannel ch && latestValues.TryGetValue(ch.Name, out var v))
                    row.Cells[valueColumn.Index].Value = v.value.ToString("F2");
            }

            framesValueLabel.Text = Volatile.Read(ref frameCount).ToString();
            lastUpdateValueLabel.Text = lastSampleTime.ToString("HH:mm:ss.fff");
        }

        private void OnErrorOccurred(object? sender, string message)
        {
            SafeUIInvoke(() =>
            {
                service.StopLogging();
                statusValueLabel.Text = "Error";
                statusValueLabel.ForeColor = Color.Red;
                UpdateUIState();
                MessageBox.Show($"High-speed logging error: {message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }

        private void UpdateUIState()
        {
            bool logging = service.IsLogging;
            bool configurable = !logging && !isLoggerActive;

            presetComboBox.Enabled = configurable && hasPresets;
            csvPathTextBox.Enabled = configurable;
            browseCsvButton.Enabled = configurable;
            channelsGrid.Enabled = configurable;
            addChannelsButton.Enabled = configurable;
            testConnectionButton.Enabled = configurable;
            stopButton.Enabled = logging;
            RefreshRowActionState();

            if (configurable && statusValueLabel.Text != "Stopped" && statusValueLabel.Text != "Error")
            {
                statusValueLabel.Text = "Idle";
                statusValueLabel.ForeColor = Color.Black;
            }
        }

        /// <summary>Marshals an action to the UI thread, dropping it if the control is being torn down.</summary>
        private void SafeUIInvoke(Action action)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            if (IsDisposed || Disposing)
                return;
            action();
        }

        private static int SnapRate(int requested)
        {
            var rates = HighSpeedLogPlanner.SupportedRatesHz;
            int best = rates[0];
            foreach (var r in rates)
                if (Math.Abs(r - requested) < Math.Abs(best - requested))
                    best = r;
            return best;
        }
    }
}
