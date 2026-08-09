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
            dtcListView.Columns.Add("Code", 80);
            dtcListView.Columns.Add("Description", 520);
            dtcListView.Columns.Add("Category", 110);
            dtcListView.Columns.Add("Type", 90);
        }

        private async void readCodesButton_Click(object sender, EventArgs e)
        {
            readCodesButton.Enabled = false;
            readCodesButton.Text = "Reading...";
            statusLabel.Text = "Reading trouble codes...";
            dtcListView.Items.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => dtcService.ReadCodes());

                if (!success)
                {
                    statusLabel.Text = "Error reading codes";
                    MessageBox.Show($"Failed to read trouble codes: {errorMessage}", "Read Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var dtc in result.Stored)
                    AddCodeRow(dtc, "Stored");
                foreach (var dtc in result.Permanent)
                    AddCodeRow(dtc, "Permanent");

                statusLabel.Text = BuildReadStatus(result);
            }
            finally
            {
                readCodesButton.Enabled = true;
                readCodesButton.Text = "Read Codes";
            }
        }

        private void AddCodeRow(DiagnosticTroubleCode dtc, string type)
        {
            string description = DtcDescriptionCatalog.TryGetDescription(dtc.Code) ?? "—";

            var item = new ListViewItem(dtc.Code);
            item.SubItems.Add(description);
            item.SubItems.Add(dtc.Category.ToString());
            item.SubItems.Add(type);
            // Descriptions with several alternate readings run past the column; the row tooltip is
            // the only way to see the whole thing, since ListView has no per-subitem tooltip.
            item.ToolTipText = $"{dtc.Code} — {description}";
            dtcListView.Items.Add(item);
        }

        private static string BuildReadStatus(DtcReadResult result)
        {
            string text = result.Stored.Count == 0 && result.Permanent.Count == 0
                ? "No trouble codes"
                : $"{result.Stored.Count} stored, {result.Permanent.Count} permanent trouble code(s)";
            if (result.PermanentError != null)
                text += " — permanent codes unavailable";
            // Without this note a missing catalog just looks like a table full of unknown codes.
            if (DtcDescriptionCatalog.Count == 0)
                text += " — code descriptions unavailable";
            return text;
        }

        private async void clearCodesButton_Click(object sender, EventArgs e)
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
                var (success, errorMessage) = await Task.Run(() => dtcService.ClearCodes());

                if (!success)
                {
                    statusLabel.Text = "Error clearing codes";
                    MessageBox.Show($"Failed to clear trouble codes: {errorMessage}", "Clear Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                dtcListView.Items.Clear();
                statusLabel.Text = "Codes cleared — read again to check for permanent codes";
            }
            finally
            {
                clearCodesButton.Enabled = true;
                clearCodesButton.Text = "Clear Codes";
            }
        }
    }
}
