using SAE.J2534;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;
        J2534OBDLogger logger;

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
        }
        private void scanForDevice()
        {
            String DllFn = APIFactory.GetAPIinfo().First().Filename;
            using API API = APIFactory.GetAPI(DllFn);
            using Device device = API.GetDevice();
            J2534DeviceName = device.ToString() ?? "None";
        }

        private void buttonRefreshDevice_Click(object sender, EventArgs e)
        {
            try
            {
                scanForDevice();
            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void buttonTestRead_Click(object sender, EventArgs e)
        {
            try
            {
                ((Button)sender).Enabled = false;
                var outfn = $"{Environment.SpecialFolder.MyDocuments}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                currentLogfileName.Text = outfn;

                logger = new J2534OBDLogger(outfn);
                logger.start();
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
    }
}
