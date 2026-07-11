using SAE.J2534;
using LotusECMLogger.Services;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LotusECMLogger.Controls
{
    public partial class OBDLoggerControl : UserControl
    {
        // Used to prevent system sleep while logging is active
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000u;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001u;

        public readonly int LogFileToUIRatio = 8; // UI update every 8th log entry

        /// <summary>
        /// Event fired when logger state changes (started/stopped)
        /// </summary>
        public event Action<bool>? LoggerStateChanged;

        /// <summary>
        /// Event fired when refresh rate is updated
        /// </summary>
        public event Action<float>? RefreshRateUpdated;

        // Holds the latest value along with the running min/max for a parameter.
        // These statistics are display-only and are never written to the log file.
        private sealed class LiveDataStat
        {
            public float Current;
            public float Min;
            public float Max;
        }

        private J2534LoggingService? logger;
        private ConcurrentDictionary<string, LiveDataStat> liveData = new();
        private DateTime lastUpdateTime = DateTime.Now;
        private DateTime lastListViewUpdate = DateTime.MinValue;
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

            GuiIcons.ApplyToButton(startLoggerButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopLoggerButton, GuiIcons.Stop);

            // Populate OBD config menu
            RefreshAvailableConfigurations();

            // Initial button state
            IsLogging = false;

            Dock = DockStyle.Fill;
        }

        private void InitializeListView()
        {
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);
            liveDataView.Columns.Add("Minimum", 100);
            liveDataView.Columns.Add("Maximum", 100);
        }

        public void RefreshAvailableConfigurations(string? preferredConfigName = null)
        {
            obdConfigComboBox.Items.Clear();
            var configs = MultiECUConfigurationLoader.GetAvailableConfigurations();
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

            var selectedConfig = preferredConfigName;
            if (string.IsNullOrWhiteSpace(selectedConfig) || !configs.Contains(selectedConfig))
            {
                selectedConfig = configs.Contains(selectedObdConfigName) ? selectedObdConfigName : configs[0];
            }

            obdConfigComboBox.SelectedItem = selectedConfig;
            selectedObdConfigName = selectedConfig;
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
                var outfn = LoggerPaths.TimestampedCsvPath("LiveData");

                if (string.IsNullOrWhiteSpace(selectedObdConfigName))
                {
                    MessageBox.Show("Please select an OBD configuration before starting the logger.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Load configuration (supports both legacy single-ECU and new multi-ECU formats)
                MultiECUConfiguration config = MultiECUConfiguration.LoadFromConfig(selectedObdConfigName);
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
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        // Marshals an action to the UI thread, silently dropping it if the control
        // is disposed or the window handle is gone before or after the cross-thread hop.
        private void SafeUIInvoke(Action action)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { Invoke(action); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            if (IsDisposed || Disposing)
                return;

            action();
        }

        private void Logger_DataLogged(List<LiveDataReading> data)
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            SafeUIInvoke(() =>
            {
                DateTime now = DateTime.Now;
                float refreshRate = LogFileToUIRatio * 1000 / (float)(now - lastUpdateTime).TotalMilliseconds;
                lastUpdateTime = now;

                foreach (var r in data)
                {
                    float value = (float)r.value_f;
                    if (liveData.TryGetValue(r.name, out var stat))
                    {
                        stat.Current = value;
                        if (value < stat.Min) stat.Min = value;
                        if (value > stat.Max) stat.Max = value;
                    }
                    else
                    {
                        liveData[r.name] = new LiveDataStat { Current = value, Min = value, Max = value };
                    }
                }

                UpdateListView();
                RefreshRateUpdated?.Invoke(refreshRate);
            });
        }

        private void UpdateListView()
        {
            DateTime now = DateTime.Now;
            if ((now - lastListViewUpdate).TotalMilliseconds < 66)
                return;
            lastListViewUpdate = now;

            var snapshot = liveData.ToList();
            ListViewItem[] items = [.. snapshot.Select(kvp => new ListViewItem([
                kvp.Key,
                kvp.Value.Current.ToString("F2"),
                kvp.Value.Min.ToString("F2"),
                kvp.Value.Max.ToString("F2")]))];

            liveDataView.BeginUpdate();
            liveDataView.Items.Clear();
            liveDataView.Items.AddRange(items);
            liveDataView.EndUpdate();
        }

        private void Logger_ExceptionOccurred(Exception ex)
        {
            SafeUIInvoke(() =>
            {
                logger?.Stop();
                IsLogging = false;
                currentLogfileName.Text = "No Log File";
                SetThreadExecutionState(ES_CONTINUOUS);

                string errorMessage = ex switch
                {
                    J2534Exception j2534Ex => $"J2534 Interface Error: {j2534Ex.Message}",
                    TimeoutException => "Communication timeout with ECM. Please check connections.",
                    UnauthorizedAccessException => "Unable to access log file. Check file permissions.",
                    IOException ioEx => $"File I/O Error: {ioEx.Message}",
                    _ => $"Unexpected error: {ex.Message}"
                };

                MessageBox.Show(errorMessage, "Logger Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }
    }
}
