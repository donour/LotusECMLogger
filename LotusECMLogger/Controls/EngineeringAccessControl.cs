using LotusECMLogger.Services;
using System.ComponentModel;
using System.Text;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Read-only UI for the engineering-access probes. Lets the user pick an ECU profile and run
    /// <see cref="IEngineeringAccessService.ProbeAsync"/>, then displays whether the raw memory
    /// family is live, whether the proprietary tooling channel answers, and the state of the four
    /// calibration-shadow predicate bytes. Performs no writes.
    /// </summary>
    public partial class EngineeringAccessControl : UserControl
    {
        private readonly IEngineeringAccessService service;
        private bool isLoggerActive;
        private bool isProbing;

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

            Dock = DockStyle.Fill;
            UpdateUIState();
        }

        private async void RunButton_Click(object? sender, EventArgs e)
        {
            if (isLoggerActive)
            {
                MessageBox.Show("Cannot probe while the main logger is active. Stop the logger first.",
                    "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (profileCombo.SelectedItem is not EcuMemoryProfile profile)
                return;

            try
            {
                isProbing = true;
                UpdateUIState();
                statusValueLabel.Text = "Probing…";
                statusValueLabel.ForeColor = Color.Blue;
                resultTextBox.Text = "Running probes…";

                UnlockProbeReport report = await service.ProbeAsync(profile);

                resultTextBox.Text = FormatReport(report);
                statusValueLabel.Text = "Done";
                statusValueLabel.ForeColor = Color.Black;
            }
            catch (Exception ex)
            {
                statusValueLabel.Text = "Error";
                statusValueLabel.ForeColor = Color.Red;
                resultTextBox.Text = $"Probe failed: {ex.Message}";
            }
            finally
            {
                isProbing = false;
                UpdateUIState();
            }
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
            bool busy = isProbing || isLoggerActive;
            runButton.Enabled = !busy && profileCombo.Items.Count > 0;
            profileCombo.Enabled = !busy;

            if (!isProbing)
            {
                if (isLoggerActive)
                {
                    statusValueLabel.Text = "Logger active — stop it to probe";
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
