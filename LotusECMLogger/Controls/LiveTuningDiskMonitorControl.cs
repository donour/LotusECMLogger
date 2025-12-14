using LotusECMLogger.Models;
using LotusECMLogger.Services;
using System.Diagnostics;
using System.Text.Json;

namespace LotusECMLogger.Controls
{
    public partial class LiveTuningDiskMonitorControl : UserControl
    {
        private T6LiveTuningService? _liveTuningService;
        private BinaryFileMonitor.BinaryFileMonitor? _fileMonitor;
        private string? _currentFilePath;
        private uint _baseAddress;
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
            // Validate and parse inputs
            if (!TryParseReadFromEcuInputs(out uint baseAddress, out uint length, out string outputDir))
            {
                return;
            }

            try
            {
                var rmaService = new T6RMAService();
                _liveTuningService = new T6LiveTuningService(rmaService);

                // Subscribe to service events
                _liveTuningService.WordWritten += OnWordWritten;
                _liveTuningService.ErrorOccurred += OnError;

                LogStatus("Live tuning service created");
                LogStatus($"Configuration: Address=0x{baseAddress:X8}, Length={length} bytes");
                LogStatus($"Output directory: {outputDir}");

                // Disable new workflows
                SetLoadFileControlsEnabled(false);
                SetReadFromEcuControlsEnabled(false);

                // Enable stop button to allow canceling/stopping
                stopMonitoringButton.Enabled = true;

                // Generate filename with ISO-8601 date and .cpt extension
                _currentFilePath = GenerateFilePath(outputDir, baseAddress);

                // Ensure directory exists
                string? directory = Path.GetDirectoryName(_currentFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    LogStatus($"Created directory: {directory}");
                }

                LogStatus($"Reading ECU memory to file: {Path.GetFileName(_currentFilePath)}");

                // Create progress reporter
                var progress = new Progress<(int bytesRead, int totalBytes)>(p =>
                {
                    if (InvokeRequired)
                    {
                        Invoke(() => LogStatus($"Progress: {p.bytesRead}/{p.totalBytes} bytes ({p.bytesRead * 100 / p.totalBytes}%)"));
                    }
                    else
                    {
                        LogStatus($"Progress: {p.bytesRead}/{p.totalBytes} bytes ({p.bytesRead * 100 / p.totalBytes}%)");
                    }
                });

                // Read memory from ECU
                bool success = await rmaService.ReadMemoryToFileAsync(baseAddress, length, _currentFilePath, progress);

                if (success)
                {
                    LogStatus($"Successfully read {length} bytes from ECU");
                    LogStatus($"File saved: {_currentFilePath}");
                    MessageBox.Show($"ECU memory successfully read to:\n{_currentFilePath}",
                        "Read Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    LogStatus("Failed to read ECU memory");
                    MessageBox.Show("Failed to read ECU memory. Check the status log for details.",
                        "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // Re-enable controls on failure
                    SetReadFromEcuControlsEnabled(true);
                    SetLoadFileControlsEnabled(true);
                    stopMonitoringButton.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Error reading ECU: {ex.Message}");
                MessageBox.Show($"Failed to read ECU memory: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Re-enable controls on error
                SetReadFromEcuControlsEnabled(true);
                SetLoadFileControlsEnabled(true);
                stopMonitoringButton.Enabled = false;
            }
        }

        private void StartMonitoringButton_Click(object sender, EventArgs e)
        {
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
                _baseAddress = baseAddress;

                // Create and configure the binary file monitor
                _fileMonitor = new BinaryFileMonitor.BinaryFileMonitor(_currentFilePath, scanIntervalMs: 100);

                // Subscribe to events
                _fileMonitor.WordChanged += OnFileWordChanged;
                _fileMonitor.FileReloaded += OnFileReloaded;
                _fileMonitor.MonitorError += OnFileMonitorError;

                // Start monitoring
                _fileMonitor.Start();

                LogStatus($"Started monitoring file: {Path.GetFileName(_currentFilePath)}");
                LogStatus($"File size: {new FileInfo(_currentFilePath).Length} bytes ({_fileMonitor.WordCount} words)");
                LogStatus($"Base address: 0x{_baseAddress:X8}");
                LogStatus($"Monitoring for changes every 100ms...");

                SetReadFromEcuControlsEnabled(false);
                SetLoadFileControlsEnabled(false);
                stopMonitoringButton.Enabled = true;
            }
            catch (Exception ex)
            {
                LogStatus($"Error starting monitoring: {ex.Message}");
                MessageBox.Show($"Failed to start monitoring: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Clean up on error
                if (_fileMonitor != null)
                {
                    _fileMonitor.WordChanged -= OnFileWordChanged;
                    _fileMonitor.FileReloaded -= OnFileReloaded;
                    _fileMonitor.MonitorError -= OnFileMonitorError;
                    _fileMonitor.Dispose();
                    _fileMonitor = null;
                }
            }
        }

        private void StopMonitoringButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_fileMonitor != null)
                {
                    // Unsubscribe from events
                    _fileMonitor.WordChanged -= OnFileWordChanged;
                    _fileMonitor.FileReloaded -= OnFileReloaded;
                    _fileMonitor.MonitorError -= OnFileMonitorError;

                    // Stop and dispose
                    _fileMonitor.Stop();
                    _fileMonitor.Dispose();
                    _fileMonitor = null;

                    LogStatus("File monitoring stopped");
                }

                if (_liveTuningService != null)
                {
                    _liveTuningService.StopMonitoring();
                    LogStatus("Live tuning service stopped");
                }

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

        private void OnFileWordChanged(object? sender, BinaryFileMonitor.WordChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnFileWordChanged(sender, e));
                return;
            }

            // Calculate the ECU memory address for this change
            uint memoryAddress = _baseAddress + (uint)e.ByteOffset;

            string message = $"[{DateTime.Now:HH:mm:ss.fff}] File Change Detected: " +
                             $"FileOffset=0x{e.ByteOffset:X}, MemoryAddr=0x{memoryAddress:X8}, " +
                             $"Old=0x{e.OldValue:X8}, New=0x{e.NewValue:X8}";

            LogStatus(message);
            Debug.WriteLine($"LiveTuning: {message}");
        }

        private void OnFileReloaded(object? sender, BinaryFileMonitor.FileReloadedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnFileReloaded(sender, e));
                return;
            }

            if (e.ChangeCount > 0)
            {
                string message = $"[{DateTime.Now:HH:mm:ss.fff}] File scan complete: " +
                                 $"{e.ChangeCount} word(s) changed" +
                                 (e.SizeChanged ? " (file size changed)" : "");

                LogStatus(message);
                Debug.WriteLine($"LiveTuning: {message}");
            }
        }

        private void OnFileMonitorError(object? sender, BinaryFileMonitor.FileMonitorErrorEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnFileMonitorError(sender, e));
                return;
            }

            string message = $"[{DateTime.Now:HH:mm:ss.fff}] File Monitor Error: {e.Exception.Message}";
            LogStatus(message);
            Debug.WriteLine($"LiveTuning ERROR: {e.Exception}");
        }

        private void OnWordWritten(object? sender, LiveTuningWordWrittenEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => OnWordWritten(sender, e));
                return;
            }

            string message = $"[{DateTime.Now:HH:mm:ss.fff}] ECU Write: Addr=0x{e.MemoryAddress:X8}, " +
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
                // Stop and dispose file monitor
                if (_fileMonitor != null)
                {
                    _fileMonitor.WordChanged -= OnFileWordChanged;
                    _fileMonitor.FileReloaded -= OnFileReloaded;
                    _fileMonitor.MonitorError -= OnFileMonitorError;
                    _fileMonitor.Stop();
                    _fileMonitor.Dispose();
                    _fileMonitor = null;
                }

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
