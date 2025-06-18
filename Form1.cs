using System.ComponentModel;
using LotusECMLogger.ViewModels;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form
    {
        private readonly MainWindowViewModel _viewModel;
        private System.Windows.Forms.Timer _refreshTimer;

        public LoggerWindow()
        {
            InitializeComponent();

            // Initialize view model
            _viewModel = new MainWindowViewModel();
            
            // Set up timer for UI updates
            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 100 // 10Hz refresh rate
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            // Wire up view model property changed events
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.LiveDataViewModel.PropertyChanged += LiveDataViewModel_PropertyChanged;
            _viewModel.ECUCodingViewModel.PropertyChanged += ECUCodingViewModel_PropertyChanged;

            // Initialize OBD config menu
            PopulateObdConfigMenu();

            // Initialize list views
            InitializeListViews();

            // Handle form closing
            this.FormClosing += LoggerWindow_FormClosing;
        }

        private void InitializeListViews()
        {
            // Live Data ListView
            liveDataView.Columns.Clear();
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);
            liveDataView.Columns.Add("Time", 100);

            // ECU Coding ListView
            codingDataView.Columns.Clear();
            codingDataView.Columns.Add("Option", 200);
            codingDataView.Columns.Add("Value", 100);
            codingDataView.Columns.Add("Category", 100);
        }

        private void PopulateObdConfigMenu()
        {
            obdConfigToolStripMenuItem.DropDownItems.Clear();
            
            foreach (var config in _viewModel.AvailableConfigurations)
            {
                var item = new ToolStripMenuItem(config);
                item.Click += (s, e) =>
                {
                    _viewModel.SelectedConfiguration = config;
                    foreach (ToolStripMenuItem menuItem in obdConfigToolStripMenuItem.DropDownItems)
                    {
                        menuItem.Checked = menuItem.Text == config;
                    }
                };
                obdConfigToolStripMenuItem.DropDownItems.Add(item);
            }

            // Select first config by default
            if (obdConfigToolStripMenuItem.DropDownItems.Count > 0)
            {
                ((ToolStripMenuItem)obdConfigToolStripMenuItem.DropDownItems[0]).Checked = true;
                _viewModel.SelectedConfiguration = _viewModel.AvailableConfigurations.First();
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            UpdateLiveDataView();
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            refreshRateLabel.Text = $"Refresh Rate: {_viewModel.RefreshRate:F1} Hz";
            currentLogfileName.Text = _viewModel.CurrentLogFile ?? "No Log File";
        }

        private void UpdateLiveDataView()
        {
            var readings = _viewModel.LiveDataViewModel.FilteredReadings;
            
            liveDataView.BeginUpdate();
            liveDataView.Items.Clear();

            foreach (var reading in readings)
            {
                var item = new ListViewItem(new[]
                {
                    reading.ParameterName,
                    reading.FormattedValue,
                    reading.Timestamp.ToString("HH:mm:ss.fff")
                });
                liveDataView.Items.Add(item);
            }

            liveDataView.EndUpdate();
        }

        private void UpdateECUCodingView()
        {
            var options = _viewModel.ECUCodingViewModel.FilteredOptions;
            
            codingDataView.BeginUpdate();
            codingDataView.Items.Clear();

            foreach (var option in options)
            {
                var item = new ListViewItem(new[]
                {
                    option.Name,
                    option.Value,
                    option.Category
                });
                codingDataView.Items.Add(item);
            }

            codingDataView.EndUpdate();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.IsLogging):
                    startLogger_button.Enabled = !_viewModel.IsLogging;
                    stopLogger_button.Enabled = _viewModel.IsLogging;
                    break;
                case nameof(MainWindowViewModel.StatusMessage):
                    // Update status message if we add one
                    break;
            }
        }

        private void LiveDataViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LiveDataViewModel.FilteredReadings))
            {
                UpdateLiveDataView();
            }
        }

        private void ECUCodingViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ECUCodingViewModel.FilteredOptions))
            {
                UpdateECUCodingView();
            }
        }

        private void LoggerWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            _refreshTimer.Stop();
            _viewModel.Dispose();
        }

        private void startLogger_button_Click(object sender, EventArgs e)
        {
            try
            {
                _viewModel.StartLoggingCommand.Execute(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void stopLogger_button_Click(object sender, EventArgs e)
        {
            _viewModel.StopLoggingCommand.Execute(null);
        }

        private void aboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }
    }
}
