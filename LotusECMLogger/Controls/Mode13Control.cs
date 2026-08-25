using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Reads every trouble code the engine ECU holds — current, confirmed and TPMS — in one
    /// round-trip through the Lotus proprietary service 0x13. The response carries no group
    /// markers, so the codes are presented as a single de-duplicated list alongside the raw
    /// bytes. See <c>T6-mode13-programming.md</c>.
    /// </summary>
    public partial class Mode13Control : UserControl
    {
        private readonly IMode13DtcService mode13Service = new J2534Mode13DtcService();

        // Both forms are shown with the CAN frame they produce: this service exists only in the
        // Lotus firmware, so which bytes went out matters when a car does not answer.
        private static readonly (string Label, Mode13RequestForm Form)[] RequestForms =
        [
            ("Report all — 03 13 FF 00", Mode13RequestForm.ReportAll),
            ("Bare service — 01 13", Mode13RequestForm.BareService),
        ];

        private bool isLoggerActive;
        private bool isBusy;

        public Mode13Control()
        {
            InitializeComponent();
            SetupListViewColumns();
            noteLabel.Text = "Service 0x13 returns current, confirmed and TPMS codes as one list — " +
                "the ECU does not mark which group a code came from.";
            requestFormComboBox.Items.AddRange(RequestForms.Select(f => (object)f.Label).ToArray());
            requestFormComboBox.SelectedIndex = 0;
            GuiIcons.ApplyToButton(readAllButton, GuiIcons.Read);
        }

        /// <summary>
        /// True while the main logger is running. The read opens its own J2534 session, which
        /// cannot coexist with active logging, so the action is disabled meanwhile.
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

        private void UpdateUIState()
        {
            bool enabled = !isLoggerActive && !isBusy;
            readAllButton.Enabled = enabled;
            requestFormComboBox.Enabled = enabled;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            UpdateUIState();
        }

        private void SetupListViewColumns()
        {
            codesListView.Columns.Clear();
            codesListView.Columns.Add("Code", 80);
            codesListView.Columns.Add("Description", 520);
            codesListView.Columns.Add("Category", 110);
            codesListView.Columns.Add("Raw", 70);
        }

        private async void readAllButton_Click(object sender, EventArgs e)
        {
            var form = RequestForms[Math.Max(requestFormComboBox.SelectedIndex, 0)].Form;

            SetBusy(true);
            readAllButton.Text = "Reading...";
            statusLabel.Text = "Reading all trouble codes...";
            codesListView.Items.Clear();
            rawResponseTextBox.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => mode13Service.ReadAllCodes(form));

                if (!success)
                {
                    statusLabel.Text = "Error reading codes";
                    MessageBox.Show(
                        $"Failed to read trouble codes: {errorMessage}\n\n" +
                        "Service 0x13 is a Lotus extension rather than a standard OBD-II service; " +
                        "an ECU that does not implement it will reject the request or stay silent.",
                        "Read Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var dtc in result.Codes)
                    AddCodeRow(dtc);

                rawResponseTextBox.Text = result.RawHex;
                statusLabel.Text = BuildReadStatus(result);
            }
            finally
            {
                SetBusy(false);
                readAllButton.Text = "Read All Codes";
            }
        }

        private void AddCodeRow(DiagnosticTroubleCode dtc)
        {
            string description = DtcDescriptionCatalog.TryGetDescription(dtc.Code) ?? "—";

            var item = new ListViewItem(dtc.Code);
            item.SubItems.Add(description);
            item.SubItems.Add(dtc.Category.ToString());
            item.SubItems.Add($"{dtc.Raw:X4}");
            // Descriptions with several alternate readings run past the column; the row tooltip is
            // the only way to see the whole thing, since ListView has no per-subitem tooltip.
            item.ToolTipText = $"{dtc.Code} — {description}";
            codesListView.Items.Add(item);
        }

        private static string BuildReadStatus(Mode13ReadResult result)
        {
            if (result.ReportedCodeCount == 0)
                return "No trouble codes";

            string text = $"{result.Codes.Count} trouble code(s)";
            int duplicates = result.ReportedCodeCount - result.Codes.Count;
            // A fault present in both the current and confirmed sets is reported twice; saying so
            // explains why the count is lower than the raw bytes suggest.
            if (duplicates > 0)
                text += $" — {duplicates} duplicate(s) collapsed";
            if (DtcDescriptionCatalog.Count == 0)
                text += " — code descriptions unavailable";
            return text;
        }
    }
}
