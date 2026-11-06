using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger
{
    /// <summary>
    /// Modal dialog for the rarely-used, destructive "Erase Model Info" operation. Lives under the
    /// Tools menu rather than the ECU Coding tab so it stays out of the way of everyday coding edits.
    /// The user picks the firmware version (which resolves the coding_cmd RAM address) and confirms;
    /// the dialog then writes CODING_CMD_ERASE_MODEL there over CCP, on the application's shared session, and
    /// confirms the firmware took it. The firmware acts on it only while the ECU is unlocked.
    /// </summary>
    public sealed class EraseModelInfoDialog : Form
    {
        // CODING_CMD_ERASE_MODEL: fills COD_base.model[] with 0xFF and flags an EEPROM write
        // (see service_coding_333ms in the firmware), which only runs commands while ecu_unlocked is set.
        private const byte CodingCmdEraseModel = 0x04;
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

        private readonly CcpSessionHost host;
        private List<CodingCommandTarget> codingTargets = [];

        private readonly ComboBox firmwareVersionCombo;
        private readonly Label codingAddressLabel;
        private readonly Button eraseModelButton;

        public EraseModelInfoDialog(CcpSessionHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
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

            var noteLabel = new Label
            {
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Location = new Point(16, 104),
                Size = new Size(508, 40),
                Text = "Sent over CCP. The firmware only acts on it when the ECU is unlocked (its calibration's unlock field is set)."
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
            Controls.Add(noteLabel);
            Controls.Add(eraseModelButton);
            Controls.Add(closeButton);
            CancelButton = closeButton;

            PopulateFirmwareVersions();
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

        /// <summary>Whether a target entry such as "E132E0288/302" is the build the ECU identified as.</summary>
        private static bool IsFor(CodingCommandTarget target, CcpBuildProfile build) =>
            target.Version == build.Build || target.Version.Split('/')[0] == build.Build;

        /// <summary>The ECU's unlock state from the build's symbol catalog, or null when the catalog cannot say.</summary>
        private static async Task<bool?> ReadUnlockedAsync(CcpMaster master, CcpBuildProfile build, CancellationToken ct)
        {
            var catalog = SymbolCatalogLoader.LoadByName(build.Catalog);
            if (catalog?.Resolve("ecu_unlocked") is { } flag)
                return await CcpCodingCommand.ReadUnlockedFlagAsync(master, flag.Address, ct);
            if (catalog?.Resolve("CAL_ecu_unlock_magic") is { } magic)
                return await CcpCodingCommand.ReadUnlockMagicAsync(master, magic.Address, ct);
            return null;
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
                "over CCP, which fills model[] with 0xFF and persists the change to EEPROM.\r\n\r\n" +
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

                await using var lease = await host.AcquireAsync();
                var master = lease.Master;
                var identification = await master.IdentifyAsync();
                if (identification.Build is { } build)
                {
                    if (!IsFor(target, build))
                    {
                        MessageBox.Show($"The ECU is {build.Build}, not '{target}'. Select its firmware and try again; nothing was written.",
                            "Wrong Firmware", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (await ReadUnlockedAsync(master, build, CancellationToken.None) == false)
                    {
                        MessageBox.Show("The ECU is locked: its calibration's unlock field is not set, so the firmware ignores coding " +
                            "commands. Nothing was written.", "ECU Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                var outcome = await CcpCodingCommand.SendAsync(master, address, CodingCmdEraseModel, CommandTimeout);
                if (outcome == CodingCommandOutcome.Executed)
                    MessageBox.Show(
                        "The ECU took the erase command: it fills model[] with 0xFF and writes EEPROM.\r\n\r\n" +
                        "Re-read coding or vehicle info to verify.",
                        "Erase Model Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(
                        $"The ECU did not act on the command within {CommandTimeout.TotalSeconds:0} seconds, so it was cleared again; nothing changed.\r\n\r\n" +
                        "The ECU is probably locked (its calibration's unlock field is not set), or the selected firmware is wrong.",
                        "Erase Model Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (CcpTimeoutException ex)
            {
                MessageBox.Show($"The ECU did not answer over CCP: {ex.Message}\r\n\r\nSwitch the ignition on and check the connection.",
                    "Erase Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
