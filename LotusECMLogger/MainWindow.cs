using LotusECMLogger.Services;
using LotusECMLogger.Controls;
using System.Diagnostics;

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

            ApplyTabIcons();

            // Handle form closing to ensure logger is stopped
            this.FormClosing += MainWindow_FormClosing;

            // Wire up logging config editor event (references obdLoggerControl, must be done in code)
            loggingConfigEditorControl.ConfigurationSaved += configName => obdLoggerControl?.RefreshAvailableConfigurations(configName);

            // Add OBD Logger control to Live Data tab
            try
            {
                obdLoggerControl = new OBDLoggerControl
                {
                    Dock = DockStyle.Fill
                };
                obdLoggerControl.LoggerStateChanged += OnLoggerStateChanged;
                obdLoggerControl.RefreshRateUpdated += OnRefreshRateUpdated;
                loggerTab.Controls.Clear();
                loggerTab.Controls.Add(obdLoggerControl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Logger tab: {ex.Message}", "Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize ECU Coding tab: {ex.Message}", "Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize T6 RMA tab: {ex.Message}", "Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Add high-speed CAN channel logging control
            try
            {
                var hsService = new HighSpeedLogService();
                var hsControl = new HighSpeedLogControl(hsService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = false
                };
                highSpeedLogTab.Controls.Clear();
                highSpeedLogTab.Controls.Add(hsControl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize High-Speed Log tab: {ex.Message}", "Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Add ABS/ESP diagnostics control
            try
            {
                var absService = new J2534AbsService();
                var absControl = new AbsControl(absService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = false
                };
                absTab.Controls.Clear();
                absTab.Controls.Add(absControl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize ABS tab: {ex.Message}", "Startup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

                vehicleInfoControl.IsLoggerActive = isLogging;

                var rmaControl = t6RmaTab.Controls.OfType<T6RMAControl>().FirstOrDefault();
                if (rmaControl != null)
                    rmaControl.IsLoggerActive = isLogging;

                var hsControl = highSpeedLogTab.Controls.OfType<HighSpeedLogControl>().FirstOrDefault();
                if (hsControl != null)
                    hsControl.IsLoggerActive = isLogging;

                var absControl = absTab.Controls.OfType<AbsControl>().FirstOrDefault();
                if (absControl != null)
                    absControl.IsLoggerActive = isLogging;

                // Erasing model info issues an RMA write, which conflicts with active logging.
                eraseModelInfoToolStripMenuItem.Enabled = !isLogging;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnLoggerStateChanged] {ex}");
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

        private void EraseModelInfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var dialog = new EraseModelInfoDialog();
            dialog.ShowDialog(this);
        }

        private void UserGuideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var helpDialog = new HelpDialog();
            helpDialog.ShowDialog(this);
        }

        private void ApplyTabIcons()
        {
            var tabColor = SystemColors.ControlText;

            var mainIcons = GuiIcons.BuildImageList(20, tabColor,
                GuiIcons.VehicleInfo,
                GuiIcons.LiveData,
                GuiIcons.EcuCoding,
                GuiIcons.Dtc,
                GuiIcons.RmaLogging,
                GuiIcons.LiveTuning,
                GuiIcons.HighSpeedLog);
            mainTabControl.ImageList = mainIcons;
            vehicleInfoTab.ImageIndex = 0;
            liveDataTab.ImageIndex    = 1;
            codingDataTab.ImageIndex  = 2;
            dtcTab.ImageIndex         = 3;
            t6RmaTab.ImageIndex       = 4;
            liveTuningTab.ImageIndex  = 5;
            highSpeedLogTab.ImageIndex = 6;

            var loggingIcons = GuiIcons.BuildImageList(20, tabColor,
                GuiIcons.LoggerTab,
                GuiIcons.ConfigTab);
            loggingTabControl.ImageList = loggingIcons;
            loggerTab.ImageIndex     = 0;
            configEditorTab.ImageIndex = 1;
        }
    }
}
