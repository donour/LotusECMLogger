using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    public partial class DTCControl : UserControl
    {
        private readonly IDtcService dtcService = new J2534DtcService();
        private readonly IFreezeFrameService freezeFrameService = new J2534FreezeFrameService();

        private bool isLoggerActive;
        private bool isBusy;

        public DTCControl()
        {
            InitializeComponent();
            SetupListViewColumns();
            SetupFreezeFrameColumns();
            GuiIcons.ApplyToButton(readCodesButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(readFreezeFrameButton, GuiIcons.Snapshots);
            GuiIcons.ApplyToButton(clearCodesButton, GuiIcons.Clear);
        }

        /// <summary>
        /// True while the main logger is running. Every DTC operation opens its own J2534
        /// session, which cannot coexist with active logging, so the actions are disabled
        /// meanwhile.
        /// </summary>
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

        // The J2534 device is exclusive, so a running operation disables every button,
        // not just its own.
        private void UpdateUIState()
        {
            bool enabled = !isLoggerActive && !isBusy;
            readCodesButton.Enabled = enabled;
            readFreezeFrameButton.Enabled = enabled;
            clearCodesButton.Enabled = enabled;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            UpdateUIState();
        }

        private void SetupListViewColumns()
        {
            dtcListView.Columns.Clear();
            dtcListView.Columns.Add("Code", 80);
            dtcListView.Columns.Add("Description", 520);
            dtcListView.Columns.Add("Category", 110);
            dtcListView.Columns.Add("Type", 90);
        }

        private void SetupFreezeFrameColumns()
        {
            freezeFrameListView.Columns.Clear();
            freezeFrameListView.Columns.Add("Parameter", 180);
            freezeFrameListView.Columns.Add("Value", 280);
            freezeFrameListView.Columns.Add("Raw Data", 140);
        }

        private async void readCodesButton_Click(object sender, EventArgs e)
        {
            SetBusy(true);
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
                SetBusy(false);
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

        private async void readFreezeFrameButton_Click(object sender, EventArgs e)
        {
            SetBusy(true);
            readFreezeFrameButton.Text = "Reading...";
            statusLabel.Text = "Reading freeze frame...";
            freezeFrameListView.Items.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => freezeFrameService.ReadFreezeFrame());

                if (!success)
                {
                    statusLabel.Text = "Error reading freeze frame";
                    MessageBox.Show($"Failed to read freeze frame: {errorMessage}", "Read Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!result.FrameStored)
                {
                    statusLabel.Text = "No freeze frame stored";
                    return;
                }

                PopulateFreezeFrame(result);
            }
            finally
            {
                SetBusy(false);
                readFreezeFrameButton.Text = "Read Freeze Frame";
            }
        }

        private void PopulateFreezeFrame(FreezeFrameResult result)
        {
            if (result.TriggeringDtc is DiagnosticTroubleCode dtc)
            {
                string description = DtcDescriptionCatalog.TryGetDescription(dtc.Code) ?? "—";
                var dtcItem = new ListViewItem("Triggering DTC");
                dtcItem.SubItems.Add($"{dtc.Code} — {description}");
                dtcItem.SubItems.Add($"{dtc.Raw >> 8:X2} {dtc.Raw & 0xFF:X2}");
                dtcItem.ToolTipText = $"{dtc.Code} — {description}";
                freezeFrameListView.Items.Add(dtcItem);
            }

            foreach (var entry in result.Entries)
            {
                var item = new ListViewItem(entry.Name);
                item.SubItems.Add(entry.Value ?? "—");
                item.SubItems.Add(entry.RawHex);
                if (!entry.IsDecoded)
                    item.ForeColor = SystemColors.GrayText;
                item.ToolTipText = $"{entry.Name}: {entry.Value ?? entry.RawHex}";
                freezeFrameListView.Items.Add(item);
            }

            string status = $"Freeze frame: {result.TriggeringDtc?.Code} — {result.Entries.Count} parameter(s)";
            if (result.Warnings.Count > 0)
                status += $" — {result.Warnings.Count} PID(s) unavailable";
            statusLabel.Text = status;
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

            SetBusy(true);
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
                freezeFrameListView.Items.Clear();
                statusLabel.Text = "Codes cleared — read again to check for permanent codes";
            }
            finally
            {
                SetBusy(false);
                clearCodesButton.Text = "Clear Codes";
            }
        }
    }
}
