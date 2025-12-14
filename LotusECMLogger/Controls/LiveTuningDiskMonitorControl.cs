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
        private List<MemoryPreset> _presets = [];

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
            outputDirectoryTextBox.Text = defaultPath;

            // Load memory presets from JSON
            LoadMemoryPresets();

            // Subscribe to text changed event to validate inputs
            baseAddressTextBox.TextChanged += ValidateInputs;
            lengthNumericUpDown.ValueChanged += ValidateInputs;
            outputDirectoryTextBox.TextChanged += ValidateReadFromEcuInputs;
            existingFileTextBox.TextChanged += ValidateLoadFileInputs;

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
                    presetComboBox.Items.AddRange([.. _presets]);

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

        private void BrowseOutputButton_Click(object sender, EventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select output directory for calibration files",
                UseDescriptionForTitle = true,
                SelectedPath = outputDirectoryTextBox.Text
            };

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                outputDirectoryTextBox.Text = folderDialog.SelectedPath;
                LogStatus($"Output directory changed to: {folderDialog.SelectedPath}");
            }
        }

        private void BrowseFileButton_Click(object sender, EventArgs e)
        {
            using var fileDialog = new OpenFileDialog
            {
                Title = "Select Calibration File",
                Filter = "Calibration Files (*.cpt)|*.cpt|All Files (*.*)|*.*",
                InitialDirectory = string.IsNullOrEmpty(existingFileTextBox.Text)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LotusECMLogger", "LiveTuning")
                    : Path.GetDirectoryName(existingFileTextBox.Text)
            };

            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                existingFileTextBox.Text = fileDialog.FileName;
                LogStatus($"Selected file: {fileDialog.FileName}");
            }
        }

        private async void ReadFromEcuButton_Click(object sender, EventArgs e)
        {
            if (_liveTuningService == null)
            {
                MessageBox.Show("Live tuning service not initialized", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validate and parse inputs
            if (!TryParseReadFromEcuInputs(out uint baseAddress, out uint length, out string outputDir))
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
            SetReadFromEcuControlsEnabled(false);
            SetLoadFileControlsEnabled(false);
            LogStatus($"Reading ECU memory: Address=0x{baseAddress:X8}, Length={length} bytes");
            LogStatus($"Output file: {_currentFilePath}");

            try
            {
                bool success = await T6LiveTuningService.ReadEcuImageToFileAsync(baseAddress, length, _currentFilePath);

                if (success)
                {
                    LogStatus($"Successfully read {length} bytes from ECU to {Path.GetFileName(_currentFilePath)}");

                    // Automatically start monitoring after successful read
                    _liveTuningService.StartMonitoring(_currentFilePath, baseAddress);
                    LogStatus($"Started monitoring: {Path.GetFileName(_currentFilePath)}");
                    LogStatus($"Changes will be written to ECU at base address 0x{baseAddress:X8}");

                    stopMonitoringButton.Enabled = true;
                }
                else
                {
                    LogStatus("Failed to read ECU memory (stub not implemented)");
                    MessageBox.Show("Read operation not yet implemented (stub)", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetReadFromEcuControlsEnabled(true);
                    SetLoadFileControlsEnabled(true);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Error reading ECU: {ex.Message}");
                MessageBox.Show($"Failed to read ECU memory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetReadFromEcuControlsEnabled(true);
                SetLoadFileControlsEnabled(true);
            }
        }

        private void StartMonitoringButton_Click(object sender, EventArgs e)
        {
            if (_liveTuningService == null)
            {
                MessageBox.Show("Live tuning service not initialized", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Get file path from existing file textbox
            string filePath = existingFileTextBox.Text.Trim();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid calibration file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _currentFilePath = filePath;
                _liveTuningService.StartMonitoring(_currentFilePath, baseAddress);
                LogStatus($"Started monitoring existing file: {Path.GetFileName(_currentFilePath)}");
                LogStatus($"Changes will be written to ECU at base address 0x{baseAddress:X8}");

                SetReadFromEcuControlsEnabled(false);
                SetLoadFileControlsEnabled(false);
                stopMonitoringButton.Enabled = true;
            }
            catch (Exception ex)
            {
                LogStatus($"Error starting monitoring: {ex.Message}");
                MessageBox.Show($"Failed to start monitoring: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopMonitoringButton_Click(object sender, EventArgs e)
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
                SetReadFromEcuControlsEnabled(true);
                SetLoadFileControlsEnabled(true);
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
            ValidateReadFromEcuInputs(sender, e);
            ValidateLoadFileInputs(sender, e);
        }

        private void ValidateReadFromEcuInputs(object? sender, EventArgs e)
        {
            // Enable/disable Read & Start button based on input validation
            bool hasValidAddress = TryParseHexAddress(baseAddressTextBox.Text, out _);
            bool hasValidPath = !string.IsNullOrWhiteSpace(outputDirectoryTextBox.Text);

            readFromEcuButton.Enabled = hasValidAddress && hasValidPath;
        }

        private void ValidateLoadFileInputs(object? sender, EventArgs e)
        {
            // Enable/disable Start Monitoring button based on input validation
            bool hasValidAddress = TryParseHexAddress(baseAddressTextBox.Text, out _);
            bool hasValidFile = !string.IsNullOrWhiteSpace(existingFileTextBox.Text) && File.Exists(existingFileTextBox.Text.Trim());

            startMonitoringButton.Enabled = hasValidAddress && hasValidFile;
        }

        private bool TryParseReadFromEcuInputs(out uint baseAddress, out uint length, out string outputDir)
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
            outputDir = outputDirectoryTextBox.Text.Trim();
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show("Please specify an output directory", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private static bool TryParseHexAddress(string hexString, out uint address)
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
                hexString = hexString[2..];
            }

            // Try to parse as hex
            return uint.TryParse(hexString, System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        private static string GenerateFilePath(string directory, uint baseAddress)
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

        private void SetReadFromEcuControlsEnabled(bool enabled)
        {
            baseAddressTextBox.Enabled = enabled;
            lengthNumericUpDown.Enabled = enabled;
            outputDirectoryTextBox.Enabled = enabled;
            browseOutputButton.Enabled = enabled;
            presetComboBox.Enabled = enabled;

            if (enabled)
            {
                ValidateReadFromEcuInputs(null, EventArgs.Empty);
            }
            else
            {
                readFromEcuButton.Enabled = false;
            }
        }

        private void SetLoadFileControlsEnabled(bool enabled)
        {
            existingFileTextBox.Enabled = enabled;
            browseFileButton.Enabled = enabled;

            if (enabled)
            {
                ValidateLoadFileInputs(null, EventArgs.Empty);
            }
            else
            {
                startMonitoringButton.Enabled = false;
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
