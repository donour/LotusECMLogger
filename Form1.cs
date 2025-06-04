using SAE.J2534;
using System.ComponentModel;
using System.Text.Unicode;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private J2534OBDLogger logger;
        private Dictionary<String, float> liveData = new Dictionary<string, float>();
        private DateTime lastUpdateTime = DateTime.Now;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private String j2534DeviceName = "None";

        public String J2534DeviceName
        {
            get { return j2534DeviceName; }
            set { j2534DeviceName = value; OnPropertyChanged("J2534DeviceNAme"); }
        }

        public LoggerWindow()
        {
            InitializeComponent();
            
            // Initialize ListView columns
            InitializeListView();
            
            // dummy logger to avoid null reference exceptions
            logger = new J2534OBDLogger("unused", Logger_DataLogged, Logger_ExceptionOccurred);
            
            // Handle form closing to ensure logger is stopped
            this.FormClosing += LoggerWindow_FormClosing;
        }

        /// <summary>
        /// Handle form closing event to ensure logger is safely stopped
        /// </summary>
        private void LoggerWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop the logger before the form closes
            logger?.Stop();
            logger?.Dispose();
        }

        private void InitializeListView()
        {
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);
        }

        private void buttonTestRead_Click(object sender, EventArgs e)
        {
            try
            {
                liveData.Clear();

                ((Button)sender).Enabled = false;
                var outfn = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                logger = new J2534OBDLogger(outfn, Logger_DataLogged, Logger_ExceptionOccurred);
                logger.Start();
                currentLogfileName.Text = outfn;
                stopLogger_button.Enabled = true;

            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ((Button)sender).Enabled = true;
            }
        }

        private void stopLogger_button_Click(object sender, EventArgs e)
        {
            logger?.Stop();
            stopLogger_button.Enabled = false;
            startLogger_button.Enabled = true;
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
            refreshRateLabel.Text = $"Refresh Rate: {1000/((now - lastUpdateTime).TotalMilliseconds):F2} hz";
            lastUpdateTime = now;
        }

        private void UpdateListView()
        {
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
            stopLogger_button.Enabled = false;
            startLogger_button.Enabled = true;
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

        private void aboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }
    }
}
