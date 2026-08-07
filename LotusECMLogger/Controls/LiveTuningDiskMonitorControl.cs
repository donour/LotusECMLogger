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
        private uint _baseAddress;
        private List<MemoryPreset> _presets = [];

        /// <summary>Cancels an in-flight upload; null whenever no upload is running.</summary>
        private CancellationTokenSource? _uploadCts;

        /// <summary>
        /// Whether the current upload has put any bytes on the wire. Set from the first write-phase
        /// progress report, so it distinguishes "cancelled before anything was sent" — which the
        /// pre-flight check and the unlock probe both make common — from "cancelled part-way
        /// through", which leaves the region half-written.
        /// </summary>
        private bool _uploadSentData;

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

            // Set default output directory under the shared logger output root
            outputDirectoryTextBox.Text = Path.Combine(LoggerPaths.OutputDirectory, "LiveTuning");

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
                    ? Path.Combine(LoggerPaths.OutputDirectory, "LiveTuning")
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

                LogStatus("Reading ECU memory...");
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

                    // Dispose the RMA service since we're done reading
                    rmaService.Dispose();

                    // Now create the live tuning service for monitoring
                    _liveTuningService = new T6LiveTuningService();

                    // Subscribe to service events
                    _liveTuningService.WordWritten += OnWordWritten;
                    _liveTuningService.ErrorOccurred += OnError;

                    // Store base address for monitoring
                    _baseAddress = baseAddress;

                    // Start monitoring the file we just created
                    _liveTuningService.StartMonitoring(_currentFilePath, _baseAddress, scanIntervalMs: 100);

                    LogStatus($"Started live tuning: {Path.GetFileName(_currentFilePath)}");
                    LogStatus($"Base address: 0x{_baseAddress:X8}");
                    LogStatus($"Monitoring for changes every 100ms...");
                    LogStatus($"Changes will be automatically written to ECU");

                    MessageBox.Show($"ECU memory successfully read and live tuning started:\n{_currentFilePath}\n\nYou can now edit this file and changes will be written to the ECU automatically.",
                        "Read Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    LogStatus("Failed to read ECU memory");
                    MessageBox.Show("Failed to read ECU memory. Check the status log for details.",
                        "Read Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // Dispose the RMA service on failure
                    rmaService.Dispose();

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

                // Cleanup on error
                if (_liveTuningService != null)
                {
                    try
                    {
                        _liveTuningService.StopMonitoring();
                        _liveTuningService.Dispose();
                        _liveTuningService = null;
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }

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

                // Create T6LiveTuningService if not already created
                if (_liveTuningService == null)
                {
                    _liveTuningService = new T6LiveTuningService();

                    // Subscribe to service events
                    _liveTuningService.WordWritten += OnWordWritten;
                    _liveTuningService.ErrorOccurred += OnError;
                }

                // Start live tuning monitoring (this will monitor file AND write to ECU)
                _liveTuningService.StartMonitoring(_currentFilePath, _baseAddress, scanIntervalMs: 100);

                LogStatus($"Started live tuning: {Path.GetFileName(_currentFilePath)}");
                LogStatus($"Base address: 0x{_baseAddress:X8}");
                LogStatus($"Monitoring for changes every 100ms...");
                LogStatus($"Changes will be automatically written to ECU");

                SetReadFromEcuControlsEnabled(false);
                SetLoadFileControlsEnabled(false);
                stopMonitoringButton.Enabled = true;
            }
            catch (Exception ex)
            {
                LogStatus($"Error starting live tuning: {ex.Message}");
                MessageBox.Show($"Failed to start live tuning: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Clean up on error
                if (_liveTuningService != null)
                {
                    try
                    {
                        _liveTuningService.StopMonitoring();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        private void StopMonitoringButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_liveTuningService != null)
                {
                    _liveTuningService.StopMonitoring();
                    LogStatus("Live tuning stopped");
                }

                stopMonitoringButton.Enabled = false;
                SetReadFromEcuControlsEnabled(true);
                SetLoadFileControlsEnabled(true);
            }
            catch (Exception ex)
            {
                LogStatus($"Error stopping live tuning: {ex.Message}");
                MessageBox.Show($"Failed to stop live tuning: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        /// <summary>
        /// Uploads the selected calibration file into ECU RAM in one shot — the inverse of
        /// "Read &amp; Start", which reads that region out to a .cpt file. The file's length decides
        /// how much is written, starting at the base address.
        /// </summary>
        private async void UploadToEcuButton_Click(object sender, EventArgs e)
        {
            // A monitoring session holds the J2534 device open, and the device cannot be opened
            // twice. The button is disabled while monitoring; this covers the rest.
            if (_liveTuningService?.IsMonitoring == true)
            {
                MessageBox.Show("Stop live tuning before uploading — the monitoring session holds the J2534 device.",
                    "Monitoring Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filePath = existingFileTextBox.Text.Trim();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid calibration file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryParseHexAddress(baseAddressTextBox.Text, out uint baseAddress))
            {
                MessageBox.Show("Invalid base address. Must be 8 hex digits (e.g., 40008654)", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            long fileLength = new FileInfo(filePath).Length;
            if (fileLength == 0)
            {
                MessageBox.Show("The selected calibration file is empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!ConfirmUpload(filePath, baseAddress, fileLength))
            {
                LogStatus("Upload cancelled at confirmation");
                return;
            }

            SetReadFromEcuControlsEnabled(false);
            SetLoadFileControlsEnabled(false);
            uploadToEcuButton.Enabled = false;
            cancelUploadButton.Enabled = true;

            _uploadCts = new CancellationTokenSource();
            _uploadSentData = false;

            try
            {
                using var rmaService = new T6RMAService();

                LogStatus("Checking ECU unlock state...");
                if (!await Task.Run(rmaService.IsEcuUnlocked, _uploadCts.Token))
                {
                    LogStatus("ECU did not answer the unlock probe — upload aborted");
                    MessageBox.Show(
                        "The ECU did not respond to the unlock probe. A locked ECU silently discards memory writes, " +
                        "so nothing would be uploaded. Unlock the ECU and try again.",
                        "ECU Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LogStatus($"Uploading {fileLength} bytes to 0x{baseAddress:X8}-0x{baseAddress + (uint)fileLength - 1:X8}");
                LogStatus($"Source: {Path.GetFileName(filePath)}");
                LogStatus($"Checking the file against the first 32 bytes in ECU memory...");

                await RunUploadAsync(rmaService, baseAddress, filePath, (int)fileLength, _uploadCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_uploadSentData)
                {
                    LogStatus("Upload cancelled before any data was sent — ECU memory is unchanged");
                    return;
                }

                LogStatus("Upload cancelled — the region now holds a mix of the old and new calibrations");
                MessageBox.Show(
                    "Upload cancelled part-way through. ECU RAM now holds part of the old calibration and part of the new one.\n\n" +
                    "Upload the file again to finish, or cycle the ignition to reload the calibration from flash.",
                    "Upload Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                LogStatus($"Upload failed: {ex.Message}");
                MessageBox.Show($"Failed to upload calibration: {ex.Message}",
                    "Upload Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _uploadCts?.Dispose();
                _uploadCts = null;

                if (!IsDisposed && !Disposing)
                {
                    cancelUploadButton.Enabled = false;
                    uploadProgressBar.Value = 0;
                    SetReadFromEcuControlsEnabled(true);
                    SetLoadFileControlsEnabled(true);
                }
            }
        }

        /// <summary>
        /// Performs the upload and reports the outcome. Split out so the surrounding handler is
        /// only concerned with guards, control state, and error presentation.
        /// </summary>
        private async Task RunUploadAsync(IT6RMAService rmaService, uint baseAddress, string filePath, int fileLength, CancellationToken cancellationToken)
        {
            // Both phases cover the whole region, so the bar spans two passes and fills once
            // across the entire operation rather than resetting when verification starts.
            uploadProgressBar.Maximum = fileLength * 2;
            uploadProgressBar.Value = 0;

            int lastLoggedPercent = -1;
            var progress = new Progress<T6RMAUploadProgress>(p =>
            {
                // The tab can be torn down mid-upload (closing the app, for instance) while reports
                // are still in flight; touching the controls after that throws.
                if (p.Phase == T6RMAUploadPhase.Writing && p.BytesDone > 0)
                {
                    _uploadSentData = true;
                }

                if (IsDisposed || Disposing)
                {
                    return;
                }

                int overall = (p.Phase == T6RMAUploadPhase.Verifying ? fileLength : 0) + p.BytesDone;
                uploadProgressBar.Value = Math.Clamp(overall, 0, uploadProgressBar.Maximum);

                // The service reports once per kilobyte; logging every one of those would bury the
                // rest of the status history, so the log gets one line per 10%.
                int percent = p.TotalBytes == 0 ? 100 : p.BytesDone * 100 / p.TotalBytes;
                int bucket = percent / 10;
                int key = (int)p.Phase * 100 + bucket;
                if (key == lastLoggedPercent)
                {
                    return;
                }
                lastLoggedPercent = key;

                string phase = p.Phase == T6RMAUploadPhase.Writing ? "Writing" : "Verifying";
                LogStatus($"{phase}: {p.BytesDone}/{p.TotalBytes} bytes ({percent}%)");
            });

            T6RMAUploadResult result;
            try
            {
                result = await rmaService.WriteFileToMemoryAsync(
                    baseAddress, filePath, verify: true, checkHeader: true, progress, cancellationToken);
            }
            catch (T6RMAHeaderMismatchException mismatch)
            {
                // Nothing was written — the check runs before the first frame. Uploading a genuinely
                // different calibration is a legitimate thing to want, so this asks rather than refuses.
                if (IsDisposed || Disposing)
                {
                    return;
                }

                LogStatus($"Pre-flight check failed at 0x{mismatch.Address:X8} — the file does not match ECU memory");

                if (!ConfirmHeaderMismatch(mismatch))
                {
                    LogStatus("Upload abandoned — ECU memory is unchanged");
                    return;
                }

                LogStatus("Mismatch overridden — uploading anyway");
                lastLoggedPercent = -1;
                uploadProgressBar.Value = 0;

                result = await rmaService.WriteFileToMemoryAsync(
                    baseAddress, filePath, verify: true, checkHeader: false, progress, cancellationToken);
            }

            if (IsDisposed || Disposing)
            {
                return;
            }

            uploadProgressBar.Value = uploadProgressBar.Maximum;

            if (result.Success)
            {
                LogStatus($"Upload complete and verified: {result.BytesWritten} bytes at 0x{baseAddress:X8}");
                MessageBox.Show(
                    $"Uploaded {result.BytesWritten} bytes to 0x{baseAddress:X8} and verified the region reads back identically.\n\n" +
                    "The calibration is live in RAM. It is not written to flash — cycling the ignition restores the flashed calibration.",
                    "Upload Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string sample = string.Join(", ", result.SampleMismatchAddresses.Select(a => $"0x{a:X8}"));
            LogStatus($"Verification FAILED: {result.MismatchCount} byte(s) differ. First: {sample}");
            MessageBox.Show(
                $"The upload sent {result.BytesWritten} bytes, but {result.MismatchCount} byte(s) read back differently.\n\n" +
                $"First mismatching addresses: {sample}\n\n" +
                "ECU RAM does not match the file. Upload again, or cycle the ignition to reload the calibration from flash.",
                "Verification Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Asks for confirmation before writing to live ECU memory, spelling out the target range
        /// and flagging a file whose size disagrees with the configured region length — usually a
        /// sign that the selected preset does not match the file.
        /// </summary>
        private bool ConfirmUpload(string filePath, uint baseAddress, long fileLength)
        {
            var message = new System.Text.StringBuilder();
            message.AppendLine($"Upload {Path.GetFileName(filePath)} ({fileLength:N0} bytes) into ECU RAM?");
            message.AppendLine();
            message.AppendLine($"Target: 0x{baseAddress:X8} - 0x{baseAddress + (uint)fileLength - 1:X8}");
            message.AppendLine();

            long configuredLength = (long)lengthNumericUpDown.Value;
            if (fileLength != configuredLength)
            {
                message.AppendLine(
                    $"WARNING: the file is {fileLength:N0} bytes but the configured region length is " +
                    $"{configuredLength:N0}. The file's own size is what gets written. Check that the " +
                    "selected preset matches this file.");
                message.AppendLine();
            }

            message.AppendLine(
                "This writes directly into the memory the ECU is calibrated from. The transfer is not " +
                "atomic — until it finishes, a running engine is using a mix of the old and new " +
                "calibrations. Only the RAM copy changes; cycling the ignition reloads the flashed one.");

            return MessageBox.Show(message.ToString(), "Confirm Upload to ECU",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>
        /// Shows what the pre-flight check found and asks whether to write anyway. Deliberately
        /// replacing the running calibration with an unrelated image is a real use, so this is a
        /// confirmation rather than a refusal — but it defaults to No and shows both headers so the
        /// choice is made on evidence.
        /// </summary>
        private bool ConfirmHeaderMismatch(T6RMAHeaderMismatchException mismatch)
        {
            var message = new System.Text.StringBuilder();
            message.AppendLine(
                $"The first {mismatch.ExpectedFromFile.Length} bytes at 0x{mismatch.Address:X8} do not match the calibration file.");
            message.AppendLine();
            message.AppendLine("Currently in ECU memory:");
            message.Append(FormatHeaderBytes(mismatch.ActualFromEcu));
            message.AppendLine();
            message.AppendLine("Calibration file:");
            message.Append(FormatHeaderBytes(mismatch.ExpectedFromFile));
            message.AppendLine();
            message.AppendLine(
                "This usually means the file belongs to a different calibration, a different ECU, or a " +
                "different memory region — check the base address and the selected file. Nothing has " +
                "been written yet.");
            message.AppendLine();
            message.AppendLine("Upload anyway?");

            return MessageBox.Show(message.ToString(), "Calibration Mismatch",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Renders header bytes as indented hex, 16 per line, for side-by-side comparison.</summary>
        private static string FormatHeaderBytes(byte[] bytes)
        {
            var text = new System.Text.StringBuilder();
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                int count = Math.Min(16, bytes.Length - offset);
                text.AppendLine("    " + Convert.ToHexString(bytes, offset, count));
            }
            return text.ToString();
        }

        private void CancelUploadButton_Click(object sender, EventArgs e)
        {
            if (_uploadCts is null)
            {
                return;
            }

            LogStatus("Cancelling upload...");
            cancelUploadButton.Enabled = false;
            _uploadCts.Cancel();
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

            // Upload takes the same two inputs as Start Monitoring: which file, and where it lives
            // in ECU memory.
            uploadToEcuButton.Enabled = hasValidAddress && hasValidFile;
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
                uploadToEcuButton.Enabled = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // An upload in flight would otherwise keep writing to the ECU after the tab is gone.
                // The handler's finally block disposes the source, so only cancel here.
                _uploadCts?.Cancel();

                // Stop and dispose live tuning service
                if (_liveTuningService != null)
                {
                    _liveTuningService.WordWritten -= OnWordWritten;
                    _liveTuningService.ErrorOccurred -= OnError;
                    _liveTuningService.StopMonitoring();
                    _liveTuningService.Dispose();
                    _liveTuningService = null;
                }

                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
