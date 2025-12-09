using SAE.J2534;
using System.ComponentModel;
using LotusECMLogger.Services;
using LotusECMLogger.Controls;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private J2534OBDLogger logger;
        private Dictionary<String, float> liveData = [];
        private DateTime lastUpdateTime = DateTime.Now;
        private double lastListViewUpdate = 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string selectedObdConfigName = "NO CONFIG";

        private bool _loggerEnabled = false;
        public bool loggerEnabled
        {
            get => _loggerEnabled;
            set
            {
                if (_loggerEnabled != value)
                {
                    _loggerEnabled = value;
                    OnPropertyChanged(nameof(loggerEnabled));
                    // Update button states
                    startLogger_button.Enabled = !value;
                    stopLogger_button.Enabled = value;
                    // Propagate to modular ECU Coding control if present
                    try
                    {
                        var ecuControl = codingDataTab.Controls.OfType<EcuCodingControl>().FirstOrDefault();
                        if (ecuControl != null)
                            ecuControl.IsLoggerActive = value;
                        var resetControl = obdResetTab.Controls.OfType<ObdResetControl>().FirstOrDefault();
                        if (resetControl != null)
                            resetControl.IsLoggerActive = value;
                        var rmaControl = t6RmaTab.Controls.OfType<T6RMAControl>().FirstOrDefault();
                        if (rmaControl != null)
                            rmaControl.IsLoggerActive = value;
                    }
                    catch {
                        // TODO: don't ignore errors
                            }
                    // Enable vehicle data loading only when logging is not active
                    //loadVehicleDataButton.Enabled = !value;
                }
            }
        }

        public LoggerWindow()
        {
            InitializeComponent();
            // Populate OBD config menu
            PopulateObdConfigMenu();
            // Initialize ListView columns
            InitializeListView();
            // dummy logger to avoid null reference exceptions
            logger = new J2534OBDLogger("unused", Logger_DataLogged, Logger_ExceptionOccurred, new OBDConfiguration());
            // Handle form closing to ensure logger is stopped
            this.FormClosing += LoggerWindow_FormClosing;
            // Initial button state
            loggerEnabled = false;

            // VehicleInfoControl is now handled by the designer

            // Replace ECU Coding tab content with modular control
            try
            {
                var ecuService = new J2534EcuCodingService();
                var ecuControl = new EcuCodingControl(ecuService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = loggerEnabled
                };
                codingDataTab.Controls.Clear();
                codingDataTab.Controls.Add(ecuControl);
            }
            catch {
                // TODO don't ignore exceptions
            }

            // Add OBD reset control
            try
            {
                var resetService = new J2534ObdResetService();
                var resetControl = new ObdResetControl(resetService)
                {
                    Dock = DockStyle.Fill,
                    IsLoggerActive = loggerEnabled
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
                    IsLoggerActive = loggerEnabled
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
        /// Handle form closing event to ensure logger is safely stopped
        /// </summary>
        private void LoggerWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Stop the logger before the form closes
            logger?.Stop();
            logger?.Dispose();
        }

        private void InitializeListView()
        {
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);

            codingDataView.Columns.Add("Option", 200);
            codingDataView.Columns.Add("Value", 100);
        }

        private void PopulateObdConfigMenu()
        {
            obdConfigToolStripMenuItem.DropDownItems.Clear();
            var configs = OBDConfigurationLoader.GetAvailableConfigurations();
            if (configs.Count == 0)
            {
                var noneItem = new ToolStripMenuItem("No configs found") { Enabled = false };
                obdConfigToolStripMenuItem.DropDownItems.Add(noneItem);
                selectedObdConfigName = "NO CONFIG";
                return;
            }
            foreach (var config in configs)
            {
                var item = new ToolStripMenuItem(config);
                item.Click += ObdConfigMenuItem_Click;
                obdConfigToolStripMenuItem.DropDownItems.Add(item);
            }
            // Default to first config
            selectedObdConfigName = configs[0];
            ((ToolStripMenuItem)obdConfigToolStripMenuItem.DropDownItems[0]).Checked = true;
        }

        private void ObdConfigMenuItem_Click(object? sender, EventArgs e)
        {
            foreach (ToolStripMenuItem item in obdConfigToolStripMenuItem.DropDownItems)
                item.Checked = false;
            if (sender == null) return;

            var clicked = (ToolStripMenuItem)sender;
            clicked.Checked = true;
            selectedObdConfigName = clicked.Text ?? "NO CONFIG";
        }

        private void startLogger_button_Click(object sender, EventArgs e)
        {
            try
            {
                liveData.Clear();
                loggerEnabled = true;
                var outfn = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                // Use selected OBD config
                if (string.IsNullOrWhiteSpace(selectedObdConfigName))
                {
                    MessageBox.Show("Please select an OBD configuration before starting the logger.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                OBDConfiguration config = OBDConfiguration.LoadFromConfig(selectedObdConfigName);
                logger = new J2534OBDLogger(outfn, Logger_DataLogged, Logger_ExceptionOccurred, config);
                logger.Start();
                currentLogfileName.Text = outfn;
            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                loggerEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load OBD configuration: {ex.Message}", "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                loggerEnabled = false;
            }
        }

        private void stopLogger_button_Click(object sender, EventArgs e)
        {
            logger?.Stop();
            loggerEnabled = false;
            currentLogfileName.Text = "No Log File";
        }

        private void Logger_DataLogged(List<LiveDataReading> data)
        {
            // Check if form is disposed or being disposed
            if (IsDisposed || Disposing)
                return;
                
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => Logger_DataLogged(data)));
                }
                catch (ObjectDisposedException)
                {
                    // Form was disposed while trying to invoke - ignore
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Form handle was destroyed - ignore
                    return;
                }
                return;
            }
            
            // Double-check after invoke to handle race conditions
            if (IsDisposed || Disposing)
                return;
            
            foreach (var r in data)
            {
                liveData[r.name] = (float)r.value_f;
            }
            
            UpdateListView();

            DateTime now = DateTime.Now;
            float refreshRate = logger.LogFileToUIRatio*1000 / (float)(now - lastUpdateTime).TotalMilliseconds;
            refreshRateLabel.Text = $"Refresh Rate: {refreshRate:F1} hz";
            lastUpdateTime = now;
        }

        private void UpdateListView()
        {
            // rate limit list view upates
            DateTime now = DateTime.Now;
            if (now.Millisecond - lastListViewUpdate > 66)
            {
                return;
            }
            else
            {
                lastListViewUpdate = now.Millisecond;
            }

            // create collection of listView items fom LiveData dictionary
            ListViewItem[] items = [.. liveData.Select(kvp => new ListViewItem([kvp.Key, kvp.Value.ToString("F2")]))];

            liveDataView.BeginUpdate();
            liveDataView.Items.Clear();
            liveDataView.Items.AddRange(items);
            liveDataView.EndUpdate();
        }

        private void Logger_ExceptionOccurred(Exception ex)
        {
            // Check if form is disposed or being disposed
            if (IsDisposed || Disposing)
                return;
                
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => Logger_ExceptionOccurred(ex)));
                }
                catch (ObjectDisposedException)
                {
                    // Form was disposed while trying to invoke - ignore
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Form handle was destroyed - ignore
                    return;
                }
                return;
            }

            // Double-check after invoke to handle race conditions
            if (IsDisposed || Disposing)
                return;

            // Stop the logger and reset UI state
            logger?.Stop();
            loggerEnabled = false;
            currentLogfileName.Text = "No Log File";

            // Show error message to user
            string errorMessage = ex switch
            {
                J2534Exception j2534Ex => $"J2534 Interface Error: {j2534Ex.Message}",
                TimeoutException => "Communication timeout with ECM. Please check connections.",
                UnauthorizedAccessException => "Unable to access log file. Check file permissions.",
                IOException ioEx => $"File I/O Error: {ioEx.Message}",
                _ => $"Unexpected error: {ex.Message}"
            };

            MessageBox.Show(errorMessage, "Logger Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void CptToCrpConverterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var converterForm = new CptToCrpConverterForm();
            converterForm.ShowDialog(this);
        }

    }
}
