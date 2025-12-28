using SAE.J2534;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    public partial class OBDLoggerControl : UserControl
    {
        public readonly int LogFileToUIRatio = 8; // UI update every 8th log entry

        /// <summary>
        /// Event fired when logger state changes (started/stopped)
        /// </summary>
        public event Action<bool>? LoggerStateChanged;

        /// <summary>
        /// Event fired when refresh rate is updated
        /// </summary>
        public event Action<float>? RefreshRateUpdated;

        private J2534LoggingService? logger;
        private Dictionary<string, float> liveData = [];
        private DateTime lastUpdateTime = DateTime.Now;
        private double lastListViewUpdate = 0;
        private string selectedObdConfigName = "NO CONFIG";

        private bool _isLogging = false;
        public bool IsLogging
        {
            get => _isLogging;
            private set
            {
                if (_isLogging != value)
                {
                    _isLogging = value;
                    startLoggerButton.Enabled = !value;
                    stopLoggerButton.Enabled = value;
                    LoggerStateChanged?.Invoke(value);
                }
            }
        }

        public OBDLoggerControl()
        {
            InitializeComponent();

            // Enable double buffering for smoother scrolling
            liveDataView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(liveDataView, true, null);

            // Initialize ListView columns
            InitializeListView();

            // Populate OBD config menu
            PopulateObdConfigComboBox();

            // Initial button state
            IsLogging = false;

            Dock = DockStyle.Fill;
        }

        private void InitializeListView()
        {
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);
        }

        private void PopulateObdConfigComboBox()
        {
            obdConfigComboBox.Items.Clear();
            var configs = OBDConfigurationLoader.GetAvailableConfigurations();
            if (configs.Count == 0)
            {
                obdConfigComboBox.Items.Add("No configs found");
                obdConfigComboBox.SelectedIndex = 0;
                obdConfigComboBox.Enabled = false;
                selectedObdConfigName = "NO CONFIG";
                return;
            }
            foreach (var config in configs)
            {
                obdConfigComboBox.Items.Add(config);
            }
            // Default to first config
            obdConfigComboBox.SelectedIndex = 0;
            selectedObdConfigName = configs[0];
        }

        private void ObdConfigComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (obdConfigComboBox.SelectedItem == null) return;
            selectedObdConfigName = obdConfigComboBox.SelectedItem.ToString() ?? "NO CONFIG";
        }

        private void StartLoggerButton_Click(object? sender, EventArgs e)
        {
            try
            {
                liveData.Clear();
                IsLogging = true;
                var outfn = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                if (string.IsNullOrWhiteSpace(selectedObdConfigName))
                {
                    MessageBox.Show("Please select an OBD configuration before starting the logger.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                OBDConfiguration config = OBDConfiguration.LoadFromConfig(selectedObdConfigName);
                logger = new J2534LoggingService(outfn, Logger_DataLogged, Logger_ExceptionOccurred, config);
                logger.Start();
                currentLogfileName.Text = outfn;
            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                IsLogging = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load OBD configuration: {ex.Message}", "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                IsLogging = false;
            }
        }

        private void StopLoggerButton_Click(object? sender, EventArgs e)
        {
            StopLogger();
        }

        /// <summary>
        /// Stops the logger. Can be called externally when form is closing.
        /// </summary>
        public void StopLogger()
        {
            logger?.Stop();
            IsLogging = false;
            currentLogfileName.Text = "No Log File";
        }

        private void Logger_DataLogged(List<LiveDataReading> data)
        {
            // Check if control is disposed or being disposed
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
                    return;
                }
                catch (InvalidOperationException)
                {
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
            float refreshRate = LogFileToUIRatio * 1000 / (float)(now - lastUpdateTime).TotalMilliseconds;
            RefreshRateUpdated?.Invoke(refreshRate);
            lastUpdateTime = now;
        }

        private void UpdateListView()
        {
            // rate limit list view updates
            DateTime now = DateTime.Now;
            if (now.Millisecond - lastListViewUpdate > 66)
            {
                return;
            }
            else
            {
                lastListViewUpdate = now.Millisecond;
            }

            // create collection of listView items from LiveData dictionary
            ListViewItem[] items = [.. liveData.Select(kvp => new ListViewItem([kvp.Key, kvp.Value.ToString("F2")]))];

            liveDataView.BeginUpdate();
            liveDataView.Items.Clear();
            liveDataView.Items.AddRange(items);
            liveDataView.EndUpdate();
        }

        private void Logger_ExceptionOccurred(Exception ex)
        {
            // Check if control is disposed or being disposed
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
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                return;
            }

            // Double-check after invoke to handle race conditions
            if (IsDisposed || Disposing)
                return;

            // Stop the logger and reset UI state
            logger?.Stop();
            IsLogging = false;
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
    }
}
