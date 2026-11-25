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
            ApplyTabIcons();
            GuiIcons.ApplyToButton(readCodesButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(readFreezeFrameButton, GuiIcons.Snapshots);
            GuiIcons.ApplyToButton(clearCodesButton, GuiIcons.Clear);
            GuiIcons.ApplyToButton(clearTcuCodesButton, GuiIcons.Clear);
        }

        // Matches how MainWindow decorates its own tab strips; this one is nested inside the
        // control, so it is out of that method's reach.
        private void ApplyTabIcons()
        {
            dtcTabControl.ImageList = GuiIcons.BuildImageList(20, SystemColors.ControlText,
                GuiIcons.Dtc,
                GuiIcons.Read);
            standardTab.ImageIndex = 0;
            mode13Tab.ImageIndex = 1;
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
                // The Mode 0x13 tab opens its own session too, so it shares the same lockout.
                mode13Control.IsLoggerActive = value;
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
            clearTcuCodesButton.Enabled = enabled;
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
            dtcListView.Columns.Add("Module", 55);
            dtcListView.Columns.Add("Description", 465);
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
            statusToolTip.SetToolTip(statusLabel, "");
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

                foreach (var row in DtcReadSummary.Rows(result))
                    AddCodeRow(row);

                statusLabel.Text = DtcReadSummary.Describe(result, DtcDescriptionCatalog.Count > 0);
                statusToolTip.SetToolTip(statusLabel, DtcReadSummary.DescribeDetail(result));
            }
            finally
            {
                SetBusy(false);
                readCodesButton.Text = "Read Codes";
            }
        }

        private void AddCodeRow(DtcRow row)
        {
            string description = DtcDescriptionCatalog.TryGetDescription(row.Code.Code) ?? "—";

            var item = new ListViewItem(row.Code.Code);
            item.SubItems.Add(row.Module);
            item.SubItems.Add(description);
            item.SubItems.Add(row.Code.Category.ToString());
            item.SubItems.Add(row.Type);
            // The row itself is the tag so a clear can tell which module and category it is.
            item.Tag = row;
            // Descriptions with several alternate readings run past the column; the row tooltip is
            // the only way to see the whole thing, since ListView has no per-subitem tooltip.
            item.ToolTipText = $"{row.Module} {row.Code.Code} — {description}";
            dtcListView.Items.Add(item);
        }

        private async void readFreezeFrameButton_Click(object sender, EventArgs e)
        {
            SetBusy(true);
            readFreezeFrameButton.Text = "Reading...";
            statusLabel.Text = "Reading freeze frame...";
            statusToolTip.SetToolTip(statusLabel, "");
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
                "Clear engine diagnostic trouble codes?\n\n" +
                "This also erases the engine freeze frame, readiness monitor results, and other " +
                "stored diagnostic values. Readiness monitors reset to \"not ready\" until their " +
                "drive cycles complete, which may affect emissions testing. Transmission codes " +
                "are not affected.\n\n" +
                "Confirm to proceed.",
                "Confirm Clear Engine Codes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            SetBusy(true);
            clearCodesButton.Text = "Clearing...";
            statusLabel.Text = "Clearing engine trouble codes...";
            statusToolTip.SetToolTip(statusLabel, "");

            try
            {
                var (success, errorMessage) = await Task.Run(() => dtcService.ClearCodes());

                if (!success)
                {
                    statusLabel.Text = "Error clearing engine codes";
                    MessageBox.Show($"Failed to clear engine trouble codes: {errorMessage}", "Clear Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Remove only what Mode 04 actually cleared: engine stored and pending codes.
                // Engine permanent codes survive, and transmission rows were not addressed.
                for (int i = dtcListView.Items.Count - 1; i >= 0; i--)
                {
                    if (DtcReadSummary.IsClearedByMode04(dtcListView.Items[i].Tag))
                        dtcListView.Items.RemoveAt(i);
                }
                freezeFrameListView.Items.Clear();
                statusLabel.Text = "Engine codes cleared — transmission codes not affected";
                statusToolTip.SetToolTip(statusLabel,
                    "Permanent engine codes cannot be cleared; read again to check for permanent codes.");
            }
            finally
            {
                SetBusy(false);
                clearCodesButton.Text = "Clear Engine Codes";
            }
        }

        private async void clearTcuCodesButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Clear transmission diagnostic trouble codes?\n\n" +
                "This asks the transmission control unit at 0x7E1 to clear its stored and pending " +
                "codes and stored diagnostic values (Mode 04). On some transmission modules this " +
                "also resets learned shift data, so the transmission may relearn its behaviour. " +
                "Engine codes and the engine freeze frame are not affected. Permanent codes cannot " +
                "be cleared directly.\n\n" +
                "Confirm to proceed.",
                "Confirm Clear Transmission Codes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            SetBusy(true);
            clearTcuCodesButton.Text = "Clearing...";
            statusLabel.Text = "Clearing transmission codes...";
            statusToolTip.SetToolTip(statusLabel, "");

            try
            {
                var (success, errorMessage) = await Task.Run(() => dtcService.ClearTcuCodes());

                if (!success)
                {
                    statusLabel.Text = "Error clearing transmission codes";
                    MessageBox.Show($"Failed to clear transmission codes: {errorMessage}", "Clear Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Mode 04 cannot clear permanent codes; keep those rows visible.
                for (int i = dtcListView.Items.Count - 1; i >= 0; i--)
                {
                    if (DtcReadSummary.IsClearedByTcuMode04(dtcListView.Items[i].Tag))
                        dtcListView.Items.RemoveAt(i);
                }
                statusLabel.Text = "Transmission codes cleared";
                statusToolTip.SetToolTip(statusLabel,
                    "Permanent transmission codes cannot be cleared; read again to check for permanent codes.");
            }
            finally
            {
                SetBusy(false);
                clearTcuCodesButton.Text = "Clear TCU Codes";
            }
        }
    }
}
