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
            // dummy logger to avoid null reference exceptions
            logger = new J2534OBDLogger("unused", Logger_DataLogged);
        }

        private void buttonTestRead_Click(object sender, EventArgs e)
        {
            try
            {
                liveData.Clear();

                ((Button)sender).Enabled = false;
                var outfn = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                logger = new J2534OBDLogger(outfn, Logger_DataLogged);
                logger.start();
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
            logger?.stop();
            stopLogger_button.Enabled = false;
            startLogger_button.Enabled = true;
            currentLogfileName.Text = "No Log File";
        }

        private void Logger_DataLogged(LiveDataReading data)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Logger_DataLogged(data)));
                return;
            }
            liveData[data.name] = (float)data.value_f;
            var bdata = from row in liveData.Keys select new { Sensor = row, Value = liveData[row] };
            liveDataView.DataSource = bdata.ToList();
        }

        private void aboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }
    }
}
