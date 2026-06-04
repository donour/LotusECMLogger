using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger
{
    /// <summary>
    /// Modal dialog for the rarely-used, destructive "Erase Model Info" operation. Lives under the
    /// Tools menu rather than the ECU Coding tab so it stays out of the way of everyday coding edits.
    /// The user picks the firmware version (which resolves the coding_cmd RAM address) and confirms;
    /// the dialog then issues an RMA write of CODING_CMD_ERASE_MODEL while the ECU is unlocked.
    /// </summary>
    public sealed class EraseModelInfoDialog : Form
    {
        // CODING_CMD_ERASE_MODEL: fills COD_base.model[] with 0xFF and flags an EEPROM write
        // (see service_coding_333ms in the firmware). Triggered by an RMA write of this value
        // into the version-specific coding_cmd register while the ECU is unlocked.
        private const byte CodingCmdEraseModel = 0x04;

        private readonly IT6RMAService rmaService = new T6RMAService();
        private List<CodingCommandTarget> codingTargets = [];

        private readonly ComboBox firmwareVersionCombo;
        private readonly Label codingAddressLabel;
        private readonly Button eraseModelButton;

        public EraseModelInfoDialog()
        {
            Text = "Erase Model Info";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 200);

            var dangerLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(176, 0, 32),
                Location = new Point(16, 16),
                Size = new Size(508, 40),
                Text = "⚠ DANGER — Erase Model Info writes 0xFF to model[] and persists the change to EEPROM. This cannot be undone."
            };

            var firmwareVersionLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 72),
                Text = "Firmware:"
            };

            firmwareVersionCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(96, 69),
                Size = new Size(240, 23)
            };
            firmwareVersionCombo.SelectedIndexChanged += firmwareVersionCombo_SelectedIndexChanged;

            codingAddressLabel = new Label
            {
                AutoSize = true,
                Location = new Point(352, 72),
                Text = "coding_cmd: —"
            };

            eraseModelButton = new Button
            {
                Text = "Erase Model Info",
                Enabled = false,
                Size = new Size(160, 32),
                Location = new Point(364, 152)
            };
            eraseModelButton.Click += eraseModelButton_Click;

            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 32),
                Location = new Point(266, 152)
            };

            Controls.Add(dangerLabel);
            Controls.Add(firmwareVersionLabel);
            Controls.Add(firmwareVersionCombo);
            Controls.Add(codingAddressLabel);
            Controls.Add(eraseModelButton);
            Controls.Add(closeButton);
            CancelButton = closeButton;

            PopulateFirmwareVersions();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                rmaService.Dispose();
            base.Dispose(disposing);
        }

        private void PopulateFirmwareVersions()
        {
            codingTargets = CodingCommandTargetsConfig.Load();
            firmwareVersionCombo.Items.Clear();
            foreach (var target in codingTargets)
                firmwareVersionCombo.Items.Add(target);
            firmwareVersionCombo.SelectedIndex = -1; // force a deliberate choice
            UpdateAddressLabel();
            UpdateEraseButtonState();
        }

        private void firmwareVersionCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateAddressLabel();
            UpdateEraseButtonState();
        }

        private void UpdateAddressLabel()
        {
            if (TryGetSelectedAddress(out uint address))
                codingAddressLabel.Text = $"coding_cmd: 0x{address:X8}";
            else
                codingAddressLabel.Text = "coding_cmd: —";
        }

        private void UpdateEraseButtonState()
        {
            eraseModelButton.Enabled = TryGetSelectedAddress(out _);
        }

        // Resolves the coding_cmd address for the selected firmware. Returns false when nothing is
        // selected or the configured address is not valid hex.
        private bool TryGetSelectedAddress(out uint address)
        {
            address = 0;
            if (firmwareVersionCombo.SelectedItem is not CodingCommandTarget target)
                return false;
            try
            {
                address = target.CodingCmdAddressValue;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid coding_cmd address for '{target}': {ex.Message}");
                return false;
            }
        }

        private async void eraseModelButton_Click(object? sender, EventArgs e)
        {
            if (firmwareVersionCombo.SelectedItem is not CodingCommandTarget target || !TryGetSelectedAddress(out uint address))
            {
                MessageBox.Show("Select a firmware version first.", "No Firmware Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"This will ERASE the model info on the ECU for firmware '{target}'.\r\n\r\n" +
                $"It writes CODING_CMD_ERASE_MODEL (0x{CodingCmdEraseModel:X2}) to coding_cmd at 0x{address:X8} " +
                "via an RMA write, which fills model[] with 0xFF and persists the change to EEPROM.\r\n\r\n" +
                "The ECU must be UNLOCKED, and selecting the wrong firmware writes to the wrong address. " +
                "This cannot be undone. Continue?",
                "Confirm Erase Model Info",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                eraseModelButton.Enabled = false;
                eraseModelButton.Text = "Erasing...";

                bool unlocked = await Task.Run(() => rmaService.IsEcuUnlocked());
                if (!unlocked)
                {
                    MessageBox.Show("The ECU is locked or not responding. Unlock the ECU before erasing model info.", "ECU Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                await rmaService.WriteByteAsync(address, CodingCmdEraseModel);

                MessageBox.Show(
                    "Erase command sent. The ECU coding handler fills model[] with 0xFF and writes EEPROM on its next cycle.\r\n\r\n" +
                    "Re-read coding or vehicle info to verify.",
                    "Erase Model Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to erase model info: {ex.Message}", "Erase Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                eraseModelButton.Text = "Erase Model Info";
                UpdateEraseButtonState();
            }
        }
    }
}
