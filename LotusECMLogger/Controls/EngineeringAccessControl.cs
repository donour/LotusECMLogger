using LotusECMLogger.Services;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// UI for the engineering-access probes plus opt-in live-memory writes. The probe section is
    /// read-only; the write section is disabled until the user explicitly enables it, and every
    /// write is confirmed before it is sent.
    /// </summary>
    public partial class EngineeringAccessControl : UserControl
    {
        private readonly IEngineeringAccessService service;
        private bool isLoggerActive;
        private bool isBusy;

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

        public EngineeringAccessControl(IEngineeringAccessService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));

            InitializeComponent();

            foreach (var profile in service.AvailableProfiles)
                profileCombo.Items.Add(profile);
            profileCombo.DisplayMember = nameof(EcuMemoryProfile.Name);
            if (profileCombo.Items.Count > 0)
                profileCombo.SelectedIndex = 0;
            profileCombo.SelectedIndexChanged += (_, _) => UpdateUIState();

            transportCombo.Items.Add(EngineeringWriteTransport.ToolingChannel);
            transportCombo.Items.Add(EngineeringWriteTransport.RmaFamily);
            transportCombo.SelectedIndex = 0;

            enableWritesCheck.CheckedChanged += (_, _) => UpdateUIState();

            Dock = DockStyle.Fill;
            UpdateUIState();
        }

        private EcuMemoryProfile? SelectedProfile => profileCombo.SelectedItem as EcuMemoryProfile;

        private async void RunButton_Click(object? sender, EventArgs e)
        {
            if (!GuardReady()) return;
            if (SelectedProfile is not EcuMemoryProfile profile) return;

            await RunOperation("Probing…", async () =>
            {
                UnlockProbeReport report = await service.ProbeAsync(profile);
                resultTextBox.Text = FormatReport(report);
            });
        }

        private async void WriteButton_Click(object? sender, EventArgs e)
        {
            if (!GuardReady()) return;
            if (SelectedProfile is not EcuMemoryProfile profile) return;

            string addrText = writeAddressTextBox.Text.Trim();
            if (addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) addrText = addrText[2..];
            if (!uint.TryParse(addrText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address))
            {
                MessageBox.Show("Invalid address. Enter a hex address such as 0x40000000.",
                    "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!TryParseHexBytes(writeBytesTextBox.Text, out byte[] data, out string parseError))
            {
                MessageBox.Show($"Invalid bytes: {parseError}",
                    "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var transport = (EngineeringWriteTransport)transportCombo.SelectedItem!;
            string hex = string.Join(" ", data.Select(b => b.ToString("X2")));
            var confirm = MessageBox.Show(
                $"Write {data.Length} byte(s) [{hex}] to 0x{address:X8} on {profile.Name} via {transport}?\n\n" +
                "This modifies live ECU memory. Continue?",
                "Confirm Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            await RunOperation("Writing…", async () =>
            {
                await service.WriteAsync(profile, transport, address, data);
                resultTextBox.Text = $"Wrote {data.Length} byte(s) [{hex}] to 0x{address:X8} via {transport}.";
            });
        }

        private async void ApplyMagicButton_Click(object? sender, EventArgs e)
        {
            if (!GuardReady()) return;
            if (SelectedProfile is not EcuMemoryProfile profile) return;
            if (profile.CalibrationMagic.Length == 0) return;

            var confirm = MessageBox.Show(
                $"Write the {profile.CalibrationMagic.Length} calibration-shadow magic bytes on {profile.Name} via the tooling channel?\n\n" +
                "Per the firmware analysis this patches the RAM shadow only; it does NOT re-run the boot " +
                "predicate or re-enable the engineering mailbox in the current session. It is a diagnostic " +
                "experiment. Continue?",
                "Confirm Calibration-Magic Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            await RunOperation("Applying magic bytes…", async () =>
            {
                await service.ApplyCalibrationMagicAsync(profile);
                resultTextBox.Text =
                    $"Wrote {profile.CalibrationMagic.Length} calibration-shadow magic bytes. " +
                    "Run the probe again to read them back; note this does not unlock live (see firmware analysis).";
            });
        }

        private bool GuardReady()
        {
            if (isLoggerActive)
            {
                MessageBox.Show("Cannot use engineering access while the main logger is active. Stop the logger first.",
                    "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return !isBusy;
        }

        private async Task RunOperation(string statusText, Func<Task> op)
        {
            try
            {
                isBusy = true;
                UpdateUIState();
                statusValueLabel.Text = statusText;
                statusValueLabel.ForeColor = Color.Blue;

                await op();

                statusValueLabel.Text = "Done";
                statusValueLabel.ForeColor = Color.Black;
            }
            catch (Exception ex)
            {
                statusValueLabel.Text = "Error";
                statusValueLabel.ForeColor = Color.Red;
                resultTextBox.Text = $"Operation failed: {ex.Message}";
            }
            finally
            {
                isBusy = false;
                UpdateUIState();
            }
        }

        private static bool TryParseHexBytes(string text, out byte[] data, out string error)
        {
            data = [];
            error = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "no bytes entered";
                return false;
            }

            // Accept space/comma separated tokens ("0D B8 45") or a continuous string ("0DB845").
            string cleaned = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace(",", " ");
            string[] tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var bytes = new List<byte>();
            if (tokens.Length == 1 && tokens[0].Length > 2)
            {
                string s = tokens[0];
                if (s.Length % 2 != 0) { error = "continuous hex must have an even number of digits"; return false; }
                for (int i = 0; i < s.Length; i += 2)
                {
                    if (!byte.TryParse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    { error = $"'{s.Substring(i, 2)}' is not a hex byte"; return false; }
                    bytes.Add(b);
                }
            }
            else
            {
                foreach (var t in tokens)
                {
                    if (!byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    { error = $"'{t}' is not a hex byte"; return false; }
                    bytes.Add(b);
                }
            }

            data = [.. bytes];
            return true;
        }

        private static string FormatReport(UnlockProbeReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Profile: {r.ProfileName}");
            sb.AppendLine();
            sb.AppendLine($"Engineering memory family (ecu_unlocked): {(r.EngineeringAccessLive ? "LIVE — read answered" : "no response (locked or absent)")}");
            sb.AppendLine($"Proprietary tooling channel:              {(r.ToolingChannelReachable ? "reachable" : "no response")}");

            if (r.CalibrationMagic.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("Calibration predicate: no magic pattern recovered for this profile.");
                return sb.ToString();
            }

            sb.AppendLine($"Calibration predicate:                    {(r.CalibrationMagicComplete ? "COMPLETE (all bytes match)" : "incomplete / not all bytes match")}");
            sb.AppendLine();
            sb.AppendLine("  Address     Expected  Actual  Match");
            sb.AppendLine("  ---------   --------  ------  -----");
            foreach (var b in r.CalibrationMagic)
            {
                string actual = b.Read ? $"0x{b.Actual:X2}" : "--";
                string match = !b.Read ? "?" : b.Matches ? "yes" : "NO";
                sb.AppendLine($"  0x{b.Address:X8}  0x{b.Expected:X2}      {actual,-6}  {match}");
            }
            return sb.ToString();
        }

        private void UpdateUIState()
        {
            bool blocked = isBusy || isLoggerActive;
            bool writesOn = enableWritesCheck.Checked && !blocked;

            runButton.Enabled = !blocked && profileCombo.Items.Count > 0;
            profileCombo.Enabled = !blocked;
            enableWritesCheck.Enabled = !blocked;

            transportCombo.Enabled = writesOn;
            writeAddressTextBox.Enabled = writesOn;
            writeBytesTextBox.Enabled = writesOn;
            writeButton.Enabled = writesOn;
            applyMagicButton.Enabled = writesOn && SelectedProfile is { CalibrationMagic.Length: > 0 };

            if (!isBusy)
            {
                if (isLoggerActive)
                {
                    statusValueLabel.Text = "Logger active — stop it to use this tab";
                    statusValueLabel.ForeColor = Color.Gray;
                }
                else if (statusValueLabel.Text is "" or "Idle")
                {
                    statusValueLabel.Text = "Idle";
                    statusValueLabel.ForeColor = Color.Black;
                }
            }
        }
    }
}
