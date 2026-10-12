using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab: connects to the ECU's CCP slave through the application's shared session, shows what it
    /// reports (slave ID, version, resources, identified build and calibration range), reads memory to a hex
    /// view or a .bin file, and checksums a region on the ECU, optionally against a file. Calibration writes go
    /// through <see cref="CcpCalibrationWriter"/>'s guards and an explicit confirmation.
    /// </summary>
    public partial class CcpControl : UserControl
    {
        private static readonly Color GoodColor = Color.ForestGreen;
        private static readonly Color WarnColor = Color.DarkOrange;
        private static readonly Color BadColor = Color.Firebrick;

        /// <summary>Null only under the Windows Forms designer, which uses the parameterless constructor.</summary>
        private readonly CcpSessionHost? host;

        private CcpSessionLease? lease;
        private CcpBuildIdentification? identification;
        private T6SymbolRegistry? registry;
        private (uint Address, byte[] Bytes)? lastRead;
        private CancellationTokenSource? operation;
        private bool isLoggerActive;
        // The build writes use: the identified one, or one the user chose for an unrecognised ECU.
        private CcpBuildProfile? writeBuild;
        private CcpBuildSource writeBuildSource;

        private static string BackupDirectory => Path.Combine(LoggerPaths.OutputDirectory, "ccp-backups");

        /// <summary>Design-time constructor: lays out the control and opens nothing.</summary>
        public CcpControl()
        {
            InitializeComponent();
            infoListView.Columns.Add("Item", 180);
            infoListView.Columns.Add("Value", 640);
            noteLabel.Text = "Connect, identify, read and checksum ECU memory over CCP. " +
                "The session is shared with the High-Speed Log tab.";
            writeNoteLabel.Text = "Engine off. Writes are live on the next lookup and lost at power-off. " +
                "A backup .bin is saved before every write.";
            GuiIcons.ApplyToButton(connectButton, GuiIcons.Connect);
            GuiIcons.ApplyToButton(disconnectButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(readRegistryButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(readButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(saveButton, GuiIcons.Save);
            GuiIcons.ApplyToButton(checksumButton, GuiIcons.Refresh);
            GuiIcons.ApplyToButton(compareButton, GuiIcons.OpenFile);
            GuiIcons.ApplyToButton(cancelButton, GuiIcons.Clear);
            GuiIcons.ApplyToButton(writeValueButton, GuiIcons.Write);
            GuiIcons.ApplyToButton(writeFileButton, GuiIcons.Write);
            GuiIcons.ApplyToButton(restoreButton, GuiIcons.UpdateRestore);
            ShowInfo();
            UpdateUIState();
        }

        public CcpControl(CcpSessionHost host) : this()
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            host.Changed += OnHostChanged;
            ShowInfo();
            UpdateUIState();
        }

        /// <summary>
        /// True while the OBD logger holds the J2534 adapter. Connecting would need the adapter too, so it is
        /// disabled meanwhile; an open session is left alone.
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

        private CcpMaster? Master => lease?.Master;
        private bool IsConnected => Master?.State == CcpSessionState.Connected;
        private bool IsBusy => operation is not null;

        // ---- Session -------------------------------------------------------------------------------

        private async void connectButton_Click(object sender, EventArgs e) => await RunAsync("Connect", async ct =>
        {
            if (lease is { Master.State: CcpSessionState.Uncertain })
            {
                await lease.Master.ConnectAsync(ct);
                Log("Reconnected.");
            }
            else
            {
                if (lease is not null) await ReleaseAsync(); // the old session failed; start a new one
                lease = await host!.AcquireAsync(ct);
                Log(host.LeaseCount > 1 ? "Joined the open CCP session." : "Connected.");
            }
            identification = await lease.Master.IdentifyAsync(cancellationToken: ct);
            ResetWriteBuild();
            var build = identification.Build;
            Log(build is null
                ? $"{identification.Slave.SlaveId}: build not recognised; memory access still works, the calibration region is unknown."
                : $"{identification.Slave.SlaveId}: {build.Build} \"{identification.CalibrationDescription}\".");
            ShowInfo();
        });

        private async void disconnectButton_Click(object sender, EventArgs e)
        {
            operation?.Cancel();
            await ReleaseAsync();
            Log(host?.LeaseCount > 0 ? "Released; the High-Speed Log tab still uses the session." : "Disconnected.");
            UpdateUIState();
        }

        private async Task ReleaseAsync()
        {
            var held = lease;
            lease = null;
            identification = null;
            registry = null;
            ResetWriteBuild();
            ShowInfo();
            if (held is not null) await held.DisposeAsync();
        }

        private async void readRegistryButton_Click(object sender, EventArgs e) => await RunAsync("Read variable table", async ct =>
        {
            var build = identification!.Build!;
            progressBar.Maximum = T6SymbolRegistry.MaximumEntries;
            registry = await Master!.ReadSymbolRegistryAsync(build, Progress(), ct);
            Log($"Variable table: {registry.BuildId} {registry.Version}, {registry.Entries.Count} entries " +
                $"({registry.Entries.Count(e => e.IsRam)} in RAM).");
            ShowInfo();
        });

        private void OnHostChanged(object? sender, EventArgs e)
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() =>
            {
                ShowInfo();
                UpdateUIState();
            });
        }

        // ---- Memory --------------------------------------------------------------------------------

        private void calibrationRangeButton_Click(object sender, EventArgs e)
        {
            if (identification?.Build is not { } build) return;
            addressTextBox.Text = $"0x{build.CalibrationBase:X8}";
            lengthUpDown.Value = build.CalibrationSize;
        }

        private async void readButton_Click(object sender, EventArgs e)
        {
            if (!TryGetRange(out uint address, out int length)) return;
            await RunAsync("Read", async ct =>
            {
                progressBar.Maximum = length;
                var watch = Stopwatch.StartNew();
                var bytes = await Master!.ReadAsync(address, length, Progress(), ct);
                lastRead = (address, bytes);
                hexTextBox.Text = HexDump(address, bytes);
                Log($"Read {length:N0} bytes at 0x{address:X8} in {watch.Elapsed.TotalSeconds:F1} s.");
            });
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (lastRead is not { } read) return;
            Directory.CreateDirectory(LoggerPaths.OutputDirectory);
            using var dialog = new SaveFileDialog
            {
                Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
                InitialDirectory = LoggerPaths.OutputDirectory,
                FileName = $"ccp_{read.Address:X8}_{read.Bytes.Length}.bin",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, read.Bytes);
            Log($"Saved {read.Bytes.Length:N0} bytes from 0x{read.Address:X8} to {dialog.FileName}.");
        }

        private async void checksumButton_Click(object sender, EventArgs e)
        {
            if (!TryGetRange(out uint address, out int length)) return;
            await RunAsync("Checksum", async ct =>
            {
                progressBar.Maximum = length;
                ushort sum = await Master!.ChecksumAsync(address, length, Progress(), ct);
                Log($"Checksum of 0x{address:X8}+{length:N0}: 0x{sum:X4} (16-bit additive, computed by the ECU).");
            });
        }

        private async void compareButton_Click(object sender, EventArgs e)
        {
            if (!TryGetRange(out uint address, out int length)) return;
            using var dialog = new OpenFileDialog { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var file = File.ReadAllBytes(dialog.FileName);
            if (file.Length < length)
            {
                Log($"{Path.GetFileName(dialog.FileName)} has {file.Length:N0} bytes; the region needs {length:N0}.");
                return;
            }
            // A 64 KB Snapshots calibration download starts at the calibration base, so its prefix lines up.
            ushort expected = CcpFrame.AdditiveChecksum(file.AsSpan(0, length));
            await RunAsync("Compare", async ct =>
            {
                progressBar.Maximum = length;
                ushort actual = await Master!.ChecksumAsync(address, length, Progress(), ct);
                Log(actual == expected
                    ? $"Match: ECU 0x{address:X8}+{length:N0} and the first {length:N0} bytes of {Path.GetFileName(dialog.FileName)} both sum to 0x{actual:X4}."
                    : $"Differ: ECU 0x{actual:X4}, file 0x{expected:X4}. Read the region and save it to find the changed bytes.");
            });
        }

        private void cancelButton_Click(object sender, EventArgs e) => operation?.Cancel();

        // ---- Calibration writes ----------------------------------------------------------------------

        private const string ChooseBuildItem = "Not recognised: choose a build to allow writes…";

        /// <summary>Writes use the identified build; an unrecognised ECU allows none until the user picks one.</summary>
        private void ResetWriteBuild()
        {
            writeBuild = identification?.Build;
            writeBuildSource = CcpBuildSource.Identified;
            buildComboBox.Items.Clear();
            if (identification is null) return;
            if (writeBuild is not null)
            {
                buildComboBox.Items.Add(writeBuild.Build);
            }
            else
            {
                buildComboBox.Items.Add(ChooseBuildItem);
                foreach (var profile in CcpBuildProfile.Known.Where(p => p.Target == Master?.Target))
                    buildComboBox.Items.Add(profile.Build);
            }
            buildComboBox.SelectedIndex = 0;
        }

        private void buildComboBox_SelectionChangeCommitted(object? sender, EventArgs e)
        {
            var chosen = CcpBuildProfile.Known.FirstOrDefault(p => p.Build == buildComboBox.SelectedItem as string);
            if (identification?.Build is not null || chosen is null)
            {
                ResetWriteBuild();
                UpdateUIState();
                return;
            }
            var answer = MessageBox.Show(this,
                $"The ECU's calibration ID matches no known build. Allow writes using {chosen.Build}'s layout " +
                $"(calibration RAM 0x{chosen.CalibrationBase:X8}+{chosen.CalibrationSize:N0}, CCP enable byte " +
                $"{(chosen.EnableByteAddress is uint enable ? $"0x{enable:X8}" : "none")})?\n\n" +
                "If this ECU is a different build, writes land at the wrong addresses and can corrupt live RAM.",
                "Override build", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
            {
                writeBuild = chosen;
                writeBuildSource = CcpBuildSource.UserOverride;
                Log($"Writes allowed with {chosen.Build}'s layout (override: the ECU was not recognised).");
            }
            else buildComboBox.SelectedIndex = 0;
            UpdateUIState();
        }

        private CcpCalibrationWriter CreateWriter(CcpBuildProfile build) =>
            new(Master!, build, writeBuildSource, BackupDirectory, EngineSpeedFor(build));

        /// <summary>The build's engine-speed symbol from its catalog, so writes are refused while the engine turns.</summary>
        private static CcpEngineSpeedSymbol? EngineSpeedFor(CcpBuildProfile build)
        {
            try
            {
                var entry = SymbolCatalogLoader.LoadByName(build.Build)?.Resolve("engine_speed_16bit");
                if (entry is null || entry.Type.Size is not (1 or 2 or 4) || entry.Type.Signed) return null;
                return new CcpEngineSpeedSymbol(entry.Address, entry.Type.Size, entry.Type.Scale);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CcpControl: engine speed symbol unavailable for {build.Build}: {ex.Message}");
                return null;
            }
        }

        private async void writeValueButton_Click(object sender, EventArgs e)
        {
            if (writeBuild is not { } build || !TryGetAddress(out uint address) || !TryParseBytes(out var bytes)) return;
            await RunAsync("Write", ct => WriteAsync(CreateWriter(build), address, bytes, wholeImage: false, "bytes", ct));
        }

        private async void writeFileButton_Click(object sender, EventArgs e)
        {
            if (writeBuild is not { } build) return;
            using var dialog = new OpenFileDialog
            {
                Title = $"Calibration file for {build.Build}",
                Filter = "Calibration images (*.bin)|*.bin|All files (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await RunAsync("Write calibration file", async ct =>
            {
                var writer = CreateWriter(build);
                var image = writer.CalibrationImageFromFile(await File.ReadAllBytesAsync(dialog.FileName, ct));
                await WriteAsync(writer, build.CalibrationBase, image, wholeImage: true, Path.GetFileName(dialog.FileName), ct);
            });
        }

        private async void restoreButton_Click(object sender, EventArgs e)
        {
            if (writeBuild is not { } build) return;
            Directory.CreateDirectory(BackupDirectory);
            using var dialog = new OpenFileDialog
            {
                Title = "Restore a CCP backup",
                Filter = "CCP backups (ccp-backup_*.bin)|ccp-backup_*.bin|All files (*.*)|*.*",
                InitialDirectory = BackupDirectory,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!CcpCalibrationWriter.TryParseBackupName(dialog.FileName, out string backupBuild, out uint address, out int length))
            {
                Log($"{Path.GetFileName(dialog.FileName)} is not named like a CCP backup (ccp-backup_<build>_<address>_<length>_<time>.bin).");
                return;
            }
            if (backupBuild != build.Build)
            {
                Log($"That backup is from {backupBuild}; writes are set up for {build.Build}.");
                return;
            }
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length != length)
            {
                Log($"That backup has {bytes.Length:N0} bytes but its name says {length:N0}.");
                return;
            }
            await RunAsync("Restore", ct => WriteAsync(CreateWriter(build), address, bytes, wholeImage: false, Path.GetFileName(dialog.FileName), ct));
        }

        /// <summary>Prepare (nothing sent), confirm with the user, then execute with backup and verification.</summary>
        private async Task WriteAsync(CcpCalibrationWriter writer, uint address, byte[] data, bool wholeImage, string source,
            CancellationToken ct)
        {
            var plan = await writer.PrepareAsync(address, data, wholeImage, ct);
            if (plan.EnableBytePreserved is { } preserved) Log($"CCP enable byte preserved: {preserved}.");
            if (plan.Runs.Count == 0)
            {
                Log($"Nothing to write: the ECU already holds {source} at 0x{address:X8}.");
                return;
            }
            if (!Confirm(plan, source)) { Log("Write cancelled before anything was sent."); return; }

            progressBar.Maximum = plan.ChangedBytes;
            try
            {
                var result = await writer.ExecuteAsync(plan, Progress(), ct);
                Log($"Wrote and verified {plan.ChangedBytes:N0} byte(s) in {result.RunsWritten} run(s) at 0x{address:X8}. " +
                    $"Backup: {result.BackupPath}");
            }
            catch (CcpCalibrationWriteException ex)
            {
                Log(ex.Message);
                MessageBox.Show(this, ex.Message + "\n\nReconnect, then use Restore Backup to put the region back.",
                    "Calibration write incomplete", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool Confirm(CcpWritePlan plan, string source)
        {
            var text = new StringBuilder()
                .AppendLine($"Write {plan.ChangedBytes:N0} changed byte(s) in {plan.Runs.Count} run(s) from {source} into " +
                    $"{plan.Build.Build} calibration RAM at 0x{plan.Address:X8}?")
                .AppendLine()
                .AppendLine(plan.EngineSpeedRpm is null
                    ? "• The engine must be off. Engine speed could not be checked for this build."
                    : "• The engine must be off (engine speed reads 0 rpm).")
                .AppendLine("• Changes take effect on the next lookup and are lost at power-off. A multi-byte change is " +
                    "not atomic: the engine can briefly see a half-written table.")
                .AppendLine($"• The current bytes are saved first to {BackupDirectory}.");
            if (plan.EnableBytePreserved is { } preserved) text.AppendLine($"• CCP enable byte left unchanged ({preserved}).");
            if (plan.Source == CcpBuildSource.UserOverride)
                text.AppendLine().AppendLine($"WARNING: the ECU was not recognised; addresses come from {plan.Build.Build}, which you chose.");
            return MessageBox.Show(this, text.ToString(), "Write calibration RAM", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private bool TryParseBytes(out byte[] bytes)
        {
            string hex = new(bytesTextBox.Text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            bytes = [];
            try { bytes = Convert.FromHexString(hex); }
            catch (FormatException) { }
            if (bytes.Length is 0 or > 256)
            {
                Log("Enter 1–256 bytes in hex, e.g. \"01 2C\".");
                return false;
            }
            return true;
        }

        private bool TryGetAddress(out uint address)
        {
            string text = addressTextBox.Text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)) return true;
            Log($"\"{addressTextBox.Text}\" is not a hexadecimal address.");
            return false;
        }

        private bool TryGetRange(out uint address, out int length)
        {
            length = (int)lengthUpDown.Value;
            if (!TryGetAddress(out address)) return false;
            if ((ulong)address + (uint)length > 0x1_0000_0000UL)
            {
                Log("The range runs past the end of the 32-bit address space.");
                return false;
            }
            return true;
        }

        // ---- Plumbing ------------------------------------------------------------------------------

        /// <summary>Runs one operation with the buttons disabled and Cancel enabled, logging any failure.</summary>
        private async Task RunAsync(string what, Func<CancellationToken, Task> action)
        {
            if (host is null || IsBusy) return;
            using var cancel = new CancellationTokenSource();
            operation = cancel;
            progressBar.Value = 0;
            UpdateUIState();
            try { await action(cancel.Token); }
            catch (OperationCanceledException)
            {
                Log($"{what} cancelled. The ECU's transfer state is unknown, so the session must reconnect before more memory access.");
            }
            catch (Exception ex)
            {
                Log($"{what} failed: {ex.Message}");
            }
            finally
            {
                operation = null;
                progressBar.Value = 0;
                ShowInfo();
                UpdateUIState();
            }
        }

        private IProgress<int> Progress() =>
            new Progress<int>(n => progressBar.Value = Math.Clamp(n, 0, progressBar.Maximum));

        private void UpdateUIState()
        {
            bool idle = host is not null && !IsBusy;
            var state = Master?.State;
            bool needsSession = lease is null || state is CcpSessionState.Uncertain or CcpSessionState.Faulted or CcpSessionState.Disposed;
            connectButton.Enabled = idle && !isLoggerActive && needsSession;
            connectButton.Text = lease is null ? "Connect" : "Reconnect";
            disconnectButton.Enabled = host is not null && lease is not null;
            readRegistryButton.Enabled = idle && IsConnected && identification?.Build?.SymbolEntriesAddress is not null;
            calibrationRangeButton.Enabled = idle && identification?.Build is not null;
            readButton.Enabled = checksumButton.Enabled = compareButton.Enabled = idle && IsConnected;
            saveButton.Enabled = idle && lastRead is not null;
            buildComboBox.Enabled = idle && IsConnected && identification is { Build: null };
            bytesTextBox.Enabled = idle && IsConnected;
            writeValueButton.Enabled = writeFileButton.Enabled = restoreButton.Enabled = idle && IsConnected && writeBuild is not null;
            cancelButton.Enabled = IsBusy;

            (stateLabel.Text, stateLabel.ForeColor) = state switch
            {
                null => ("Not connected", SystemColors.ControlText),
                CcpSessionState.Connected => (host?.LeaseCount > 1 ? "Connected (shared with High-Speed Log)" : "Connected", GoodColor),
                CcpSessionState.Uncertain => ($"Uncertain: {Master!.StateReason}. Reconnect before memory access.", WarnColor),
                CcpSessionState.Faulted => ($"Adapter failed: {Master!.StateReason}", BadColor),
                _ => ("Session closed", SystemColors.ControlText),
            };
        }

        private void ShowInfo()
        {
            var rows = new List<(string, string)>();
            var target = Master?.Target ?? CcpTarget.T6;
            rows.Add(("Target", $"{target.Name}: commands on 0x{target.CroId:X3}, replies/DAQ on 0x{target.DtoId:X3}"));
            if (identification is { } id)
            {
                var fields = id.Slave.Fields;
                rows.Add(("Slave ID", id.Slave.SlaveId));
                rows.Add(("CCP version", $"{id.Slave.VersionMajor}.{id.Slave.VersionMinor}"));
                rows.Add(("Resources", $"available {DescribeResources(fields.AvailableResources)}; " +
                    $"protected {DescribeResources(fields.ProtectedResources)}"));
                rows.Add(("Driver version", $"0x{fields.DriverVersion:X2}"));
                if (id.Build is { } build)
                {
                    rows.Add(("Build", build.Build));
                    if (id.CalibrationDescription is { } description) rows.Add(("Calibration", description));
                    if (id.RegistryBuildId is { } registryBuild) rows.Add(("Program", $"{registryBuild} {id.RegistryVersion}".Trim()));
                    rows.Add(("Calibration RAM", $"0x{build.CalibrationBase:X8}–0x{build.CalibrationBase + build.CalibrationSize - 1:X8} " +
                        $"({build.CalibrationSize:N0} bytes)"));
                    if (build.EnableByteAddress is uint enable) rows.Add(("CCP enable byte", $"0x{enable:X8}"));
                }
                else rows.Add(("Build", "Not recognised: the calibration ID matches no known build"));
            }
            if (registry is { } table) rows.Add(("Variable table", $"{table.Entries.Count} entries"));
            if (host?.LeaseCount > 0) rows.Add(("Session users", host.LeaseCount.ToString(CultureInfo.InvariantCulture)));

            infoListView.BeginUpdate();
            infoListView.Items.Clear();
            foreach (var (item, value) in rows) infoListView.Items.Add(new ListViewItem([item, value]));
            infoListView.EndUpdate();
        }

        private static string DescribeResources(byte mask)
        {
            if (mask == 0) return "none (0x00)";
            var names = new List<string>();
            if ((mask & 0x01) != 0) names.Add("CAL");
            if ((mask & 0x02) != 0) names.Add("DAQ");
            if ((mask & 0x40) != 0) names.Add("PGM");
            return $"{string.Join('+', names)} (0x{mask:X2})";
        }

        /// <summary>16 bytes per line: address, hex bytes, printable ASCII.</summary>
        internal static string HexDump(uint address, byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 4 + bytes.Length / 16 * 14);
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                int count = Math.Min(16, bytes.Length - offset);
                text.Append((address + (uint)offset).ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    text.Append(i < count ? bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture) : "  ");
                    text.Append(i == 7 ? "  " : " ");
                }
                text.Append(' ');
                for (int i = 0; i < count; i++)
                {
                    byte value = bytes[offset + i];
                    text.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
                }
                text.Append("\r\n");
            }
            return text.ToString();
        }

        private void Log(string message) =>
            logTextBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\r\n");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (host is not null) host.Changed -= OnHostChanged;
                operation?.Cancel();
                try { lease?.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"CcpControl: releasing the session failed: {ex.Message}"); }
                lease = null;
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
