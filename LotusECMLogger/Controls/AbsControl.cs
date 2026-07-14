using System.ComponentModel;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Diagnostics UI for the Bosch ESP8 ABS/ESP module. Currently exposes a single read-only
    /// action: read the module's ECU identification and display it. Future programming and
    /// diagnostic features will be added here.
    /// </summary>
    public partial class AbsControl : UserControl
    {
        private readonly IAbsService absService;
        private bool isLoggerActive;

        public AbsControl(IAbsService absService)
        {
            this.absService = absService ?? throw new ArgumentNullException(nameof(absService));
            InitializeComponent();
            SetupListViewColumns();
            GuiIcons.ApplyToButton(readInfoButton, GuiIcons.Read);
        }

        /// <summary>
        /// True while the main logger is running. The ABS read opens its own J2534 session,
        /// which cannot coexist with active logging, so the read button is disabled meanwhile.
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

        private void SetupListViewColumns()
        {
            infoListView.Columns.Clear();
            infoListView.Columns.Add("Field", 220);
            infoListView.Columns.Add("Value", 520);
        }

        private void UpdateUIState()
        {
            readInfoButton.Enabled = !isLoggerActive;
            if (isLoggerActive)
                statusLabel.Text = "Stop logging to read from the ABS module.";
        }

        private async void readInfoButton_Click(object sender, EventArgs e)
        {
            if (isLoggerActive)
                return;

            readInfoButton.Enabled = false;
            readInfoButton.Text = "Reading...";
            statusLabel.Text = "Reading ABS module information...";
            infoListView.Items.Clear();

            try
            {
                var (success, errorMessage, result) = await Task.Run(() => absService.ReadModuleInfo());

                if (!success)
                {
                    statusLabel.Text = "Could not read ABS module";
                    MessageBox.Show($"Failed to read ABS module information:\n\n{errorMessage}",
                        "ABS Read Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                foreach (var field in result.Fields)
                {
                    var item = new ListViewItem(field.Key);
                    item.SubItems.Add(field.Value);
                    infoListView.Items.Add(item);
                }

                statusLabel.Text = result.Fields.Count == 0
                    ? "ABS module returned no identification data"
                    : "ABS module information read";
            }
            finally
            {
                readInfoButton.Text = "Read Info";
                // Re-enable only if logging did not start while the read was in flight.
                readInfoButton.Enabled = !isLoggerActive;
            }
        }
    }
}
