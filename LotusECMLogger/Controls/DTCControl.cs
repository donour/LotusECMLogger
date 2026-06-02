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
            // TODO: Implement DTC clearing functionality
            MessageBox.Show("Clear Codes functionality not yet implemented", "Not Implemented",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
