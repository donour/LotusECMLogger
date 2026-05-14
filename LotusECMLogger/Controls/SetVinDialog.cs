using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    internal partial class SetVinDialog : Form
    {
        // ISO 3779: VINs are 17 characters drawn from A-Z and 0-9, excluding
        // I, O and Q to prevent visual confusion with 1 and 0.
        private const string AllowedChars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        private const string LotusWmi = "SCC";
        private const int WmiLength = 3;
        private const int VinSuffixLength = 14;
        private static readonly Color OkColor = Color.FromArgb(0, 128, 0);
        private static readonly Color ErrorColor = Color.FromArgb(176, 0, 0);

        private readonly IVinSetService _vinSetService;

        public string Vin => (wmiTextBox.Text + vinSuffixTextBox.Text).Trim().ToUpperInvariant();

        public SetVinDialog(IVinSetService vinSetService, string? initialFullVin = null)
        {
            _vinSetService = vinSetService ?? throw new ArgumentNullException(nameof(vinSetService));

            InitializeComponent();

            wmiTextBox.Text = LotusWmi;
            if (initialFullVin?.Length == 17)
            {
                vinSuffixTextBox.Text = initialFullVin.Substring(WmiLength).ToUpperInvariant();
                vinSuffixTextBox.SelectionStart = vinSuffixTextBox.TextLength;
            }

            rulesLabel.Text =
                "VIN remainder requirements:\r\n" +
                "  • Exactly 14 characters\r\n" +
                "  • Letters A–Z (excluding I, O, Q) and digits 0–9\r\n" +
                "  • No spaces or punctuation";

            warningLabel.Text =
                "⚠ Warning: Lotus firmware checks for acceptable VINs.\r\n" +
                $"Engine must be off. The first 3 characters ({LotusWmi}) are the Lotus WMI and cannot be changed.";

            vinSuffixTextBox.TextChanged += (_, _) =>
            {
                // CharacterCasing only upper-cases keystrokes, not programmatic Text assignments
                // (paste from clipboard goes through it, but defensive cleanup costs nothing).
                var upper = vinSuffixTextBox.Text.ToUpperInvariant();
                if (upper != vinSuffixTextBox.Text)
                {
                    int caret = vinSuffixTextBox.SelectionStart;
                    vinSuffixTextBox.Text = upper;
                    vinSuffixTextBox.SelectionStart = caret;
                }
                UpdateValidationState();
            };
            UpdateValidationState();
        }

        private void UpdateValidationState()
        {
            var (valid, message) = ValidateSuffix(vinSuffixTextBox.Text.Trim().ToUpperInvariant());
            statusLabel.Text = (valid ? "✓ " : "✗ ") + message;
            statusLabel.ForeColor = valid ? OkColor : ErrorColor;
            programButton.Enabled = valid;
        }

        private static (bool valid, string message) ValidateSuffix(string suffix)
        {
            if (suffix.Length == 0)
                return (false, "Enter the remaining 14 VIN characters");

            if (suffix.Length != VinSuffixLength)
                return (false, $"VIN remainder must be {VinSuffixLength} characters (currently {suffix.Length})");

            for (int i = 0; i < suffix.Length; i++)
            {
                char c = suffix[i];
                if (!AllowedChars.Contains(c))
                {
                    // Position is 1-indexed within the FULL VIN, so add WmiLength.
                    int fullPosition = WmiLength + i + 1;
                    string reason = c is 'I' or 'O' or 'Q'
                        ? $"'{c}' is not permitted (I, O, Q are forbidden)"
                        : $"'{c}' is not a valid VIN character";
                    return (false, $"Position {fullPosition}: {reason}");
                }
            }

            return (true, "VIN format is valid");
        }

        private void ProgramButton_Click(object? sender, EventArgs e)
        {
            var vin = Vin;
            var (valid, message) = ValidateSuffix(vinSuffixTextBox.Text.Trim().ToUpperInvariant());
            if (!valid)
            {
                MessageBox.Show(this, message, "Invalid VIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"Program VIN '{vin}' to the ECU?\r\n\r\n" +
                $"The first 3 characters ({LotusWmi}) are the Lotus WMI and cannot be changed by Mode 0x3B. " +
                "Only positions 4–17 will be written.\r\n\r\n" +
                "The engine must be off. This change is written to ECU EEPROM and persists across power cycles.",
                "Confirm VIN Programming",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                programButton.Enabled = false;
                cancelButton.Enabled = false;
                vinSuffixTextBox.Enabled = false;
                statusLabel.Text = "Programming VIN...";
                statusLabel.ForeColor = SystemColors.ControlText;
                Cursor = Cursors.WaitCursor;

                var (success, error) = _vinSetService.SetVin(vin);

                if (success)
                {
                    MessageBox.Show(this, "VIN programmed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    MessageBox.Show(this, $"Failed to program VIN: {error}", "Programming Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                vinSuffixTextBox.Enabled = true;
                cancelButton.Enabled = true;
                UpdateValidationState();
            }
        }
    }
}
