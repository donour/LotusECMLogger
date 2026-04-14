namespace LotusECMLogger.Controls
{
    internal partial class AddRequestDialog : Form
    {
        public AddRequestDialog()
        {
            InitializeComponent();
            typeComboBox.SelectedIndex = 0;
            UpdateTypeSpecificFields();
        }

        public EditableRequestRow BuildRow()
        {
            return new EditableRequestRow
            {
                Type = typeComboBox.SelectedItem?.ToString() ?? "Mode01",
                Name = nameTextBox.Text.Trim(),
                Description = descriptionTextBox.Text.Trim(),
                Category = categoryTextBox.Text.Trim(),
                Unit = unitTextBox.Text.Trim(),
                PidsText = pidsTextBox.Text.Trim(),
                PidHighText = pidHighTextBox.Text.Trim(),
                PidLowText = pidLowTextBox.Text.Trim()
            };
        }

        private void TypeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateTypeSpecificFields();
        }

        private void UpdateTypeSpecificFields()
        {
            var isMode01 = string.Equals(typeComboBox.SelectedItem?.ToString(), "Mode01", StringComparison.OrdinalIgnoreCase);
            pidsTextBox.Enabled = isMode01;
            pidsLabel.Enabled = isMode01;
            pidHighTextBox.Enabled = !isMode01;
            pidLowTextBox.Enabled = !isMode01;
            pidHighLabel.Enabled = !isMode01;
            pidLowLabel.Enabled = !isMode01;

            if (string.IsNullOrWhiteSpace(nameTextBox.Text) || nameTextBox.Text.StartsWith("New Mode", StringComparison.Ordinal))
            {
                nameTextBox.Text = isMode01 ? "New Mode01 Request" : "New Mode22 Request";
                nameTextBox.SelectionStart = nameTextBox.TextLength;
            }
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(nameTextBox.Text))
            {
                MessageBox.Show(this, "Request name cannot be empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.Equals(typeComboBox.SelectedItem?.ToString(), "Mode01", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(pidsTextBox.Text))
                {
                    MessageBox.Show(this, "Mode01 requests need one or more PIDs.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else if (string.IsNullOrWhiteSpace(pidHighTextBox.Text) || string.IsNullOrWhiteSpace(pidLowTextBox.Text))
            {
                MessageBox.Show(this, "Mode22 requests need PID High and PID Low values.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
