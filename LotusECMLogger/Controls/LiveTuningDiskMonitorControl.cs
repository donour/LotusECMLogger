using LotusECMLogger.Models;
using LotusECMLogger.Services;
using System.Diagnostics;
using System.Text.Json;

namespace LotusECMLogger.Controls
{
    public partial class LiveTuningDiskMonitorControl : UserControl
    {
        private T6LiveTuningService? _liveTuningService;
        private string? _currentFilePath;
        private List<MemoryPreset> _presets = new();

        private bool _isInitialized = false;

        public LiveTuningDiskMonitorControl()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Only initialize once at runtime, never in designer
            if (_isInitialized || DesignMode)
            {
                return;
            }

            _isInitialized = true;

            // Set default output directory to Documents folder
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string defaultPath = Path.Combine(documentsPath, "LotusECMLogger", "LiveTuning");
            filePathTextBox.Text = defaultPath;

            // Load memory presets from JSON
            LoadMemoryPresets();

            // Subscribe to text changed event to validate inputs
            baseAddressTextBox.TextChanged += ValidateInputs;
            lengthNumericUpDown.ValueChanged += ValidateInputs;
            filePathTextBox.TextChanged += ValidateInputs;

            LogStatus("Live Tuning control initialized");
        }

        private void LoadMemoryPresets()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "liveTuning", "memoryConfig.json");

                if (!File.Exists(configPath))
                {
                    LogStatus($"Warning: Memory config file not found at {configPath}");
                    return;
                }

                string jsonContent = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<MemoryPresetsConfig>(jsonContent);

                if (config?.Presets != null && config.Presets.Count > 0)
                {
                    _presets = config.Presets;
                    presetComboBox.Items.Clear();
                    presetComboBox.Items.AddRange(_presets.ToArray());

                    // Select first preset by default
                    if (presetComboBox.Items.Count > 0)
                    {
                        presetComboBox.SelectedIndex = 0;
                    }

                    LogStatus($"Loaded {_presets.Count} memory presets");
                }
                else
                {
                    LogStatus("Warning: No presets found in config file");
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Error loading memory presets: {ex.Message}");
                Debug.WriteLine($"Error loading memory presets: {ex}");
            }
        }

        private void presetComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (presetComboBox.SelectedItem is MemoryPreset preset)
            {
                // Update the base address and length fields
                baseAddressTextBox.Text = preset.BaseAddress;
                lengthNumericUpDown.Value = preset.Length;

                // Log the selection
                string description = string.IsNullOrEmpty(preset.Description)
                    ? ""
                    : $" - {preset.Description}";
                LogStatus($"Preset selected: {preset.Name}{description}");
            }
        }

        /// <summary>
        /// Sets the T6LiveTuningService instance to use
        /// </summary>
        public void SetLiveTuningService(T6LiveTuningService service)
        {
            _liveTuningService = service;

            if (_liveTuningService != null)
            {
                // Subscribe to service events
                _liveTuningService.WordWritten += OnWordWritten;
                _liveTuningService.ErrorOccurred += OnError;
                LogStatus("Live tuning service connected");
            }
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select output directory for calibration files",
                UseDescriptionForTitle = true,
                SelectedPath = filePathTextBox.Text
            };

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                filePathTextBox.Text = folderDialog.SelectedPath;
                LogStatus($"Output directory changed to: {folderDialog.SelectedPath}");
            }
        }

        private async void readFromEcuButton_Click(object sender, EventArgs e)
        {
            if (_liveTuningService == null)
            {
                MessageBox.Show("Live tuning service not initialized", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validate and parse inputs
            if (!TryParseInputs(out uint baseAddress, out uint length, out string outputDir))
            {
                return;
            }

            // Generate filename with ISO-8601 date and .cpt extension
            _currentFilePath = GenerateFilePath(outputDir, baseAddress);

            // Ensure directory exists
            try
            {
                string? directory = Path.GetDirectoryName(_currentFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    LogStatus($"Created directory: {directory}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create output directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Disable controls during operation
            SetControlsEnabled(false);
            LogStatus($"Reading ECU memory: Address=0x{baseAddress:X8}, Length={length} bytes");
            LogStatus($"Output file: {_currentFilePath}");

            try
            {
                bool success = await _liveTuningService.ReadEcuImageToFileAsync(baseAddress, length, _currentFilePath);

                if (success)
                {
                    LogStatus($"Successfully read {length} bytes from ECU to {Path.GetFileName(_currentFilePath)}");
                    startMonitoringButton.Enabled = true;
                }
                else
                {
                    LogStatus("Failed to read ECU memory (stub not implemented)");
                    MessageBox.Show("Read operation not yet implemented (stub)", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Error reading ECU: {ex.Message}");
                MessageBox.Show($"Failed to read ECU memory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private void startMonitoringButton_Click(object sender, EventArgs e)
        {
            if (_liveTuningService == null)
            {
                MessageBox.Show("Live tuning service not initialized", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                MessageBox.Show("No calibration file loaded. Please read from ECU first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Parse base address
            if (!TryParseHexAddress(baseAddressTextBox.Text, out uint baseAddress))
            {
                MessageBox.Show("Invalid base address", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                _liveTuningService.StartMonitoring(_currentFilePath, baseAddress);
                LogStatus($"Started monitoring: {Path.GetFileName(_currentFilePath)}");
                LogStatus($"Changes will be written to ECU at base address 0x{baseAddress:X8}");

                startMonitoringButton.Enabled = false;
                stopMonitoringButton.Enabled = true;
                readFromEcuButton.Enabled = false;
                baseAddressTextBox.Enabled = false;
                lengthNumericUpDown.Enabled = false;
            }
            catch (Exception ex)
            {
                LogStatus($"Error starting monitoring: {ex.Message}");
                MessageBox.Show($"Failed to start monitoring: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void stopMonitoringButton_Click(object sender, EventArgs e)
        {
            if (_liveTuningService == null)
            {
                return;
            }

            try
            {
                _liveTuningService.StopMonitoring();
                LogStatus("Monitoring stopped");

                stopMonitoringButton.Enabled = false;
                startMonitoringButton.Enabled = true;
                readFromEcuButton.Enabled = true;
                baseAddressTextBox.Enabled = true;
                lengthNumericUpDown.Enabled = true;
            }
            catch (Exception ex)
            {
                LogStatus($"Error stopping monitoring: {ex.Message}");
                MessageBox.Show($"Failed to stop monitoring: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnWordWritten(object? sender, LiveTuningWordWrittenEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnWordWritten(sender, e));
                return;
            }

            string message = $"[{e.Timestamp:HH:mm:ss.fff}] ECU Write: Addr=0x{e.MemoryAddress:X8}, " +
                             $"Offset=0x{e.FileOffset:X}, Old=0x{e.OldValue:X8}, New=0x{e.NewValue:X8}";
            LogStatus(message);
        }

        private void OnError(object? sender, string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnError(sender, errorMessage));
                return;
            }

            LogStatus($"ERROR: {errorMessage}");
        }

        private void ValidateInputs(object? sender, EventArgs e)
        {
            // Enable/disable Read button based on input validation
            bool hasValidAddress = TryParseHexAddress(baseAddressTextBox.Text, out _);
            bool hasValidPath = !string.IsNullOrWhiteSpace(filePathTextBox.Text);

            readFromEcuButton.Enabled = hasValidAddress && hasValidPath;
        }

        private bool TryParseInputs(out uint baseAddress, out uint length, out string outputDir)
        {
            baseAddress = 0;
            length = 0;
            outputDir = string.Empty;

            // Parse base address
            if (!TryParseHexAddress(baseAddressTextBox.Text, out baseAddress))
            {
                MessageBox.Show("Invalid base address. Must be 8 hex digits (e.g., 40000000)", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Get length from numeric up/down (already in decimal form)
            length = (uint)lengthNumericUpDown.Value;

            // Validate length is multiple of 4
            if (length % 4 != 0)
            {
                MessageBox.Show("Length must be a multiple of 4 bytes for word alignment", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Get output directory
            outputDir = filePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show("Please specify an output directory", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private bool TryParseHexAddress(string hexString, out uint address)
        {
            address = 0;

            if (string.IsNullOrWhiteSpace(hexString))
            {
                return false;
            }

            // Remove any 0x prefix if present
            hexString = hexString.Trim();
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexString = hexString.Substring(2);
            }

            // Try to parse as hex
            return uint.TryParse(hexString, System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        private string GenerateFilePath(string directory, uint baseAddress)
        {
            // Generate filename: YYYY-MM-DDTHH-MM-SS_ADDRESS.cpt
            // Using ISO 8601 format but replacing colons with hyphens for filesystem compatibility
            string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            string filename = $"{timestamp}_{baseAddress:X8}.cpt";
            return Path.Combine(directory, filename);
        }

        private void LogStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(() => LogStatus(message));
                return;
            }

            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            statusTextBox.AppendText(timestampedMessage + Environment.NewLine);

            // Auto-scroll to bottom
            statusTextBox.SelectionStart = statusTextBox.Text.Length;
            statusTextBox.ScrollToCaret();

            // Also log to debug output
            Debug.WriteLine($"LiveTuning: {message}");
        }

        private void SetControlsEnabled(bool enabled)
        {
            baseAddressTextBox.Enabled = enabled;
            lengthNumericUpDown.Enabled = enabled;
            filePathTextBox.Enabled = enabled;
            browseButton.Enabled = enabled;
            readFromEcuButton.Enabled = enabled;

            // Don't enable start button if we haven't read from ECU yet
            if (enabled && !string.IsNullOrEmpty(_currentFilePath))
            {
                startMonitoringButton.Enabled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from service events
                if (_liveTuningService != null)
                {
                    _liveTuningService.WordWritten -= OnWordWritten;
                    _liveTuningService.ErrorOccurred -= OnError;
                }

                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
