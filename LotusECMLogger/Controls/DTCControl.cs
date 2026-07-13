using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    public partial class DTCControl : UserControl
    {
        private readonly IDtcService dtcService = new J2534DtcService();

        public DTCControl()
        {
            InitializeComponent();
            SetupListViewColumns();
            GuiIcons.ApplyToButton(readCodesButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(clearCodesButton, GuiIcons.Clear);
        }

        private void SetupListViewColumns()
        {
            dtcListView.Columns.Clear();
            dtcListView.Columns.Add("Code", 120);
            dtcListView.Columns.Add("Category", 160);
        }

        private void readCodesButton_Click(object sender, EventArgs e)
        {
            readCodesButton.Enabled = false;
            readCodesButton.Text = "Reading...";
            statusLabel.Text = "Reading stored trouble codes...";
            dtcListView.Items.Clear();

            try
            {
                var (success, errorMessage, codes) = dtcService.ReadStoredCodes();

                if (!success)
                {
                    statusLabel.Text = "Error reading codes";
                    MessageBox.Show($"Failed to read trouble codes: {errorMessage}", "Read Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var dtc in codes)
                {
                    var item = new ListViewItem(dtc.Code);
                    item.SubItems.Add(dtc.Category.ToString());
                    dtcListView.Items.Add(item);
                }

                statusLabel.Text = codes.Count == 0
                    ? "No stored trouble codes"
                    : $"{codes.Count} stored trouble code(s)";
            }
            finally
            {
                readCodesButton.Enabled = true;
                readCodesButton.Text = "Read Codes";
            }
        }

        private void clearCodesButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all diagnostic trouble codes?\n\n" +
                "This also erases freeze frame data, readiness monitor results, and other stored " +
                "diagnostic values. Readiness monitors reset to \"not ready\" until their drive " +
                "cycles complete, which may affect emissions testing.\n\n" +
                "Confirm to proceed.",
                "Confirm Clear Codes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            clearCodesButton.Enabled = false;
            clearCodesButton.Text = "Clearing...";
            statusLabel.Text = "Clearing trouble codes...";

            try
            {
                var (success, errorMessage) = dtcService.ClearCodes();

                if (!success)
                {
                    statusLabel.Text = "Error clearing codes";
                    MessageBox.Show($"Failed to clear trouble codes: {errorMessage}", "Clear Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                dtcListView.Items.Clear();
                statusLabel.Text = "Trouble codes and freeze frames cleared";
            }
            finally
            {
                clearCodesButton.Enabled = true;
                clearCodesButton.Text = "Clear Codes";
            }
        }
    }
}
