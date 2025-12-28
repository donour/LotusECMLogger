using LotusECMLogger.Services;
using LotusECMLogger.Controls;

namespace LotusECMLogger
{
    public partial class MainWindow : Form
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private OBDLoggerControl? obdLoggerControl;

        public MainWindow()
        {
            InitializeComponent();

            // Handle form closing to ensure logger is stopped
            this.FormClosing += MainWindow_FormClosing;

            // Add OBD Logger control to Live Data tab
            try
            {
                obdLoggerControl = new OBDLoggerControl
                {
                    Dock = DockStyle.Fill
                };
                obdLoggerControl.LoggerStateChanged += OnLoggerStateChanged;
                obdLoggerControl.RefreshRateUpdated += OnRefreshRateUpdated;
                liveDataTab.Controls.Clear();
                liveDataTab.Controls.Add(obdLoggerControl);
            }
            catch
            {
                // TODO don't ignore exceptions
            }

            // Replace ECU Coding tab content with modular control
            try
            {
                var ecuService = new J2534EcuCodingService();
                var ecuControl = new EcuCodingControl(ecuService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = false
                };
                codingDataTab.Controls.Clear();
                codingDataTab.Controls.Add(ecuControl);
            }
            catch
            {
                // TODO don't ignore exceptions
            }

            // Add OBD reset control
            try
            {
                var resetService = new J2534ObdResetService();
                var resetControl = new ObdResetControl(resetService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = false
                };
                obdResetTab.Controls.Clear();
                obdResetTab.Controls.Add(resetControl);
            }
            catch
            {
                // TODO don't ignore exceptions
            }

            // Add T6 RMA logging control
            try
            {
                var rmaService = new T6RMAService();
                var rmaControl = new T6RMAControl(rmaService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = false
                };
                t6RmaTab.Controls.Clear();
                t6RmaTab.Controls.Add(rmaControl);
            }
            catch
            {
                // TODO don't ignore exceptions
            }
        }

        /// <summary>
        /// Handle logger state changes and propagate to other controls
        /// </summary>
        private void OnLoggerStateChanged(bool isLogging)
        {
            try
            {
                var ecuControl = codingDataTab.Controls.OfType<EcuCodingControl>().FirstOrDefault();
                if (ecuControl != null)
                    ecuControl.IsLoggerActive = isLogging;

                var resetControl = obdResetTab.Controls.OfType<ObdResetControl>().FirstOrDefault();
                if (resetControl != null)
                    resetControl.IsLoggerActive = isLogging;

                var rmaControl = t6RmaTab.Controls.OfType<T6RMAControl>().FirstOrDefault();
                if (rmaControl != null)
                    rmaControl.IsLoggerActive = isLogging;
            }
            catch
            {
                // TODO: don't ignore errors
            }
        }

        /// <summary>
        /// Handle refresh rate updates from the logger control
        /// </summary>
        private void OnRefreshRateUpdated(float refreshRate)
        {
            refreshRateLabel.Text = $"Refresh Rate: {refreshRate:F1} hz";
        }

        /// <summary>
        /// Handle form closing event to ensure logger is safely stopped
        /// </summary>
        private void MainWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Stop the logger before the form closes
            obdLoggerControl?.StopLogger();
        }

        private void AboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }

        private void CLIRunnerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var t6eFlasher = new T6EFlasher();
            t6eFlasher.ShowDialog(this);
        }

        private void UserGuideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var helpDialog = new HelpDialog();
            helpDialog.ShowDialog(this);
        }
    }
}
