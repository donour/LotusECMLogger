using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    internal partial class SetVinDialog : Form
    {
        // ISO 3779: VINs are 17 characters drawn from A-Z and 0-9, excluding
        // I, O and Q to prevent visual confusion with 1 and 0.
        private const string AllowedChars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        private static readonly Color OkColor = Color.FromArgb(0, 128, 0);
        private static readonly Color ErrorColor = Color.FromArgb(176, 0, 0);

        private readonly IVinSetService _vinSetService;

        public string Vin => vinTextBox.Text.Trim().ToUpperInvariant();

        public SetVinDialog(IVinSetService vinSetService, string? initialVin = null)
        {
            _vinSetService = vinSetService ?? throw new ArgumentNullException(nameof(vinSetService));

            InitializeComponent();
            rulesLabel.Text =
                "VIN requirements:\r\n" +
                "  • Exactly 17 characters\r\n" +
                "  • Letters A–Z (excluding I, O, Q) and digits 0–9\r\n" +
                "  • No spaces or punctuation";

            warningLabel.Text =
                "⚠ Warning: Lotus firmware checks for acceptable VINs.\r\n" +
                "Engine must be off. The first 3 characters (WMI) cannot be changed.";

            if (!string.IsNullOrWhiteSpace(initialVin))
            {
                vinTextBox.Text = initialVin.Trim();
                vinTextBox.SelectionStart = vinTextBox.TextLength;
            }

            vinTextBox.TextChanged += (_, _) => UpdateValidationState();
            UpdateValidationState();
        }

        private void UpdateValidationState()
        {
            var (valid, message) = Validate(Vin);
            statusLabel.Text = (valid ? "✓ " : "✗ ") + message;
            statusLabel.ForeColor = valid ? OkColor : ErrorColor;
            programButton.Enabled = valid;
        }

        private static (bool valid, string message) Validate(string vin)
        {
            if (vin.Length == 0)
                return (false, "Enter a VIN");

            if (vin.Length != 17)
                return (false, $"VIN must be 17 characters (currently {vin.Length})");

            for (int i = 0; i < vin.Length; i++)
            {
                char c = vin[i];
                if (!AllowedChars.Contains(c))
                {
                    string reason = c is 'I' or 'O' or 'Q'
                        ? $"'{c}' is not permitted (I, O, Q are forbidden)"
                        : $"'{c}' is not a valid VIN character";
                    return (false, $"Position {i + 1}: {reason}");
                }
            }

            return (true, "VIN format is valid");
        }

        private void ProgramButton_Click(object? sender, EventArgs e)
        {
            var vin = Vin;
            var (valid, message) = Validate(vin);
            if (!valid)
            {
                MessageBox.Show(this, message, "Invalid VIN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"Program VIN '{vin}' to the ECU?\r\n\r\n" +
                "The engine must be off. This change is written to ECU EEPROM and persists across power cycles. " +
                "The Lotus firmware does not allow the first 3 characters (WMI) to be changed — they will be ignored.",
                "Confirm VIN Programming",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                programButton.Enabled = false;
                cancelButton.Enabled = false;
                vinTextBox.Enabled = false;
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
                vinTextBox.Enabled = true;
                cancelButton.Enabled = true;
                UpdateValidationState();
            }
        }
    }
}
