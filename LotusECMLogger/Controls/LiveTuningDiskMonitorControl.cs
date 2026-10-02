using LotusECMLogger.Models;
using LotusECMLogger.Services;
using System.Diagnostics;
using System.Text.Json;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Live tuning: read a calibration region out of ECU RAM to a file, watch that file and write
    /// every saved change back to the ECU, or upload a whole file in one shot.
    /// </summary>
    /// <remarks>
    /// The control is either idle or running exactly one operation (Read &amp; Start, Start
    /// Monitoring, or Upload). Each operation is one async method run through
    /// <see cref="RunAsync"/>; the single Stop button cancels whichever one is running.
    /// <see cref="UpdateButtons"/> derives every control's enabled state from that one fact.
    /// </remarks>
    public partial class LiveTuningDiskMonitorControl : UserControl
    {
        private List<MemoryPreset> _presets = [];

        /// <summary>The running operation's cancellation; null whenever the control is idle.</summary>
        private CancellationTokenSource? _operation;

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

            // The action buttons depend on these inputs being valid.
            baseAddressTextBox.TextChanged += (_, _) => UpdateButtons();
            outputDirectoryTextBox.TextChanged += (_, _) => UpdateButtons();
            existingFileTextBox.TextChanged += (_, _) => UpdateButtons();
            UpdateButtons();

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

        // ── Buttons ─────────────────────────────────────────────────────────────────────────

        private async void ReadFromEcuButton_Click(object sender, EventArgs e)
        {
            if (!TryParseReadFromEcuInputs(out uint baseAddress, out uint length, out string outputDir))
            {
                return;
            }

            await RunAsync("Read & Start", token => ReadAndMonitorAsync(baseAddress, length, outputDir, token));
        }

        private async void StartMonitoringButton_Click(object sender, EventArgs e)
        {
            string filePath = existingFileTextBox.Text.Trim();
            if (!File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid calibration file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryParseHexAddress(baseAddressTextBox.Text, out uint baseAddress))
            {
                MessageBox.Show("Invalid base address", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            await RunAsync("Live tuning", token => MonitorAsync(filePath, baseAddress, token));
        }

        /// <summary>
        /// Uploads the selected calibration file into ECU RAM in one shot — the inverse of
        /// "Read &amp; Start", which reads that region out to a .cpt file. The file's length decides
        /// how much is written, starting at the base address.
        /// </summary>
        private async void UploadToEcuButton_Click(object sender, EventArgs e)
        {
            string filePath = existingFileTextBox.Text.Trim();
            if (!File.Exists(filePath))
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

            await RunAsync("Upload", token => UploadAsync(filePath, baseAddress, (int)fileLength, token));
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (_operation is null)
            {
                return;
            }

            LogStatus("Stopping...");
            _operation.Cancel();
            UpdateButtons();
        }

        // ── Operation plumbing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs one operation to completion, failure, or Stop. Owns the busy state: the control is
        /// busy exactly while an operation is inside this method, and every exit path returns it
        /// to idle.
        /// </summary>
        private async Task RunAsync(string name, Func<CancellationToken, Task> operation)
        {
            if (_operation != null)
            {
                return; // Already busy — the buttons are disabled, this only guards a stale click.
            }

            _operation = new CancellationTokenSource();
            UpdateButtons();

            try
            {
                await operation(_operation.Token);
            }
            catch (OperationCanceledException)
            {
                LogStatus($"{name} stopped");
            }
            catch (Exception ex)
            {
                LogStatus($"{name} failed: {ex.Message}");
                if (!IsDisposed)
                {
                    MessageBox.Show($"{name} failed: {ex.Message}", name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                UpdateButtons();
            }
        }

        /// <summary>
        /// Sets every control's enabled state. Idle: inputs editable and each action available when
        /// its inputs are valid. Busy: everything locked except Stop.
        /// </summary>
        private void UpdateButtons()
        {
            if (IsDisposed)
            {
                return;
            }

            bool idle = _operation == null;
            bool hasAddress = TryParseHexAddress(baseAddressTextBox.Text, out _);
            bool hasOutputDir = !string.IsNullOrWhiteSpace(outputDirectoryTextBox.Text);
            bool hasFile = File.Exists(existingFileTextBox.Text.Trim());

            presetComboBox.Enabled = idle;
            baseAddressTextBox.Enabled = idle;
            lengthNumericUpDown.Enabled = idle;
            outputDirectoryTextBox.Enabled = idle;
            browseOutputButton.Enabled = idle;
            existingFileTextBox.Enabled = idle;
            browseFileButton.Enabled = idle;

            readFromEcuButton.Enabled = idle && hasAddress && hasOutputDir;
            startMonitoringButton.Enabled = idle && hasAddress && hasFile;
            uploadToEcuButton.Enabled = idle && hasAddress && hasFile;

            // Disabled once pressed, so a second click cannot look like it did something.
            stopButton.Enabled = !idle && !_operation!.IsCancellationRequested;
        }

        // ── Operations ──────────────────────────────────────────────────────────────────────

        /// <summary>Reads the region to a new file, selects that file, then monitors it until Stop.</summary>
        private async Task ReadAndMonitorAsync(uint baseAddress, uint length, string outputDir, CancellationToken token)
        {
            Directory.CreateDirectory(outputDir);
            string filePath = GenerateFilePath(outputDir, baseAddress);

            LogStatus($"Reading {length} bytes from 0x{baseAddress:X8} to {Path.GetFileName(filePath)}...");

            int lastLoggedTenth = -1;
            var progress = new Progress<(int bytesRead, int totalBytes)>(p =>
            {
                int tenth = p.bytesRead * 10 / p.totalBytes;
                if (tenth == lastLoggedTenth)
                {
                    return;
                }
                lastLoggedTenth = tenth;
                LogStatus($"Read {p.bytesRead}/{p.totalBytes} bytes ({tenth * 10}%)");
            });

            using (var rmaService = new T6RMAService())
            {
                if (!await rmaService.ReadMemoryToFileAsync(baseAddress, length, filePath, progress, token))
                {
                    throw new IOException("The ECU stopped answering memory reads. Check that it is unlocked and the ignition is on.");
                }
            }

            LogStatus($"Saved {filePath}");

            // Select the new file so it can be monitored again or uploaded after Stop.
            existingFileTextBox.Text = filePath;

            await MonitorAsync(filePath, baseAddress, token);
        }

        /// <summary>
        /// Watches the file and writes every saved change to the ECU until Stop, or until the
        /// session fails on its own (a write is refused, or the file stays unreadable). The service
        /// lives exactly as long as the session.
        /// </summary>
        private async Task MonitorAsync(string filePath, uint baseAddress, CancellationToken token)
        {
            using var service = new T6LiveTuningService();
            service.WordWritten += OnWordWritten;
            service.ErrorOccurred += OnError;

            var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Faulted += (_, reason) => faulted.TrySetResult(reason);

            service.StartMonitoring(filePath, baseAddress, scanIntervalMs: 100);
            LogStatus($"Live tuning started: {Path.GetFileName(filePath)} at 0x{baseAddress:X8}");
            LogStatus("Changes saved to the file are written to the ECU automatically");

            // Stop the session the moment Stop is pressed (or the control is disposed), rather than
            // whenever this method's continuation next runs on the UI thread.
            using var stopOnCancel = token.Register(service.StopMonitoring);

            // Returns only if the session fails by itself; Stop surfaces as a cancellation instead.
            // Either way, leaving this method disposes the service, which stops the session.
            string reason = await faulted.Task.WaitAsync(token);
            throw new IOException(reason);
        }

        private async Task UploadAsync(string filePath, uint baseAddress, int fileLength, CancellationToken token)
        {
            using var rmaService = new T6RMAService();

            LogStatus("Checking ECU unlock state...");
            if (!await Task.Run(rmaService.IsEcuUnlocked, token))
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

            try
            {
                await RunUploadAsync(rmaService, baseAddress, filePath, fileLength, token);
            }
            finally
            {
                if (!IsDisposed)
                {
                    uploadProgressBar.Value = 0;
                }
            }
        }

        /// <summary>
        /// Performs the upload and reports the outcome. Split out so the caller is only concerned
        /// with the unlock check and the progress bar's reset.
        /// </summary>
        private async Task RunUploadAsync(IT6RMAService rmaService, uint baseAddress, string filePath, int fileLength, CancellationToken cancellationToken)
        {
            // Both phases cover the whole region, so the bar spans two passes and fills once
            // across the entire operation rather than resetting when verification starts.
            uploadProgressBar.Maximum = fileLength * 2;
            uploadProgressBar.Value = 0;

            // Whether any bytes have gone out: distinguishes "stopped before anything was sent" —
            // which the pre-flight check and the unlock probe both make common — from "stopped
            // part-way through", which leaves the region half-written.
            bool sentData = false;

            int lastLoggedPercent = -1;
            var progress = new Progress<T6RMAUploadProgress>(p =>
            {
                if (p.Phase == T6RMAUploadPhase.Writing && p.BytesDone > 0)
                {
                    sentData = true;
                }

                // The tab can be torn down mid-upload (closing the app, for instance) while reports
                // are still in flight; touching the controls after that throws.
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
            }
            catch (OperationCanceledException)
            {
                ReportUploadStopped(sentData);
                throw;
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

        private void ReportUploadStopped(bool sentData)
        {
            if (!sentData)
            {
                LogStatus("Upload stopped before any data was sent — ECU memory is unchanged");
                return;
            }

            LogStatus("Upload stopped — the region now holds a mix of the old and new calibrations");
            if (!IsDisposed)
            {
                MessageBox.Show(
                    "Upload stopped part-way through. ECU RAM now holds part of the old calibration and part of the new one.\n\n" +
                    "Upload the file again to finish, or cycle the ignition to reload the calibration from flash.",
                    "Upload Stopped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

        // ── Service callbacks (raised on background threads) ────────────────────────────────

        private void OnWordWritten(object? sender, LiveTuningWordWrittenEventArgs e)
        {
            LogStatus($"ECU Write: Addr=0x{e.MemoryAddress:X8}, Offset=0x{e.FileOffset:X}, " +
                      $"Old=0x{e.OldValue:X8}, New=0x{e.NewValue:X8}");
        }

        private void OnError(object? sender, string errorMessage)
        {
            LogStatus($"ERROR: {errorMessage}");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────

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

        /// <summary>
        /// Appends a timestamped line to the status log. Safe from any thread: background callers
        /// are queued to the UI thread without waiting, so they can never block on it.
        /// </summary>
        private void LogStatus(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try { BeginInvoke(() => LogStatus(message)); }
                catch (InvalidOperationException) { } // handle gone during teardown
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stops whatever is running: a read or upload at its next chunk, and a monitoring
                // session immediately, through its cancellation registration.
                _operation?.Cancel();

                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
