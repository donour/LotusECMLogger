using SAE.J2534;
using System.ComponentModel;
using System.Diagnostics;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            try { 
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
                J2534OBDLogger logger = new J2534OBDLogger();
                //MessageBox.Show($"Output file: {logger.getOutputFilename()}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                logger.connect();
                //MessageFilter FlowControlFilter = new MessageFilter()
                //{
                //    FilterType = Filter.FLOW_CONTROL_FILTER,
                //    Mask = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
                //    Pattern = new byte[] { 0x00, 0x00, 0x07, 0xE8 },
                //    FlowControl = new byte[] { 0x00, 0x00, 0x07, 0xE0 }
                //};

                //string DllFileName = APIFactory.GetAPIinfo().First().Filename;

                //using (API API = APIFactory.GetAPI(DllFileName))
                //using (Device Device = API.GetDevice())
                //using (Channel Channel = Device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE))
                //{
                //    byte[] ecm_obd_head = { 0x00, 0x00, 0x07, 0xE0 };
                //    byte[] obd_tps_request = { 0x01, 0x11 };

                //    Channel.StartMsgFilter(FlowControlFilter);
                //    double voltage = Channel.MeasureBatteryVoltage() / 1000.0;
                //    Debug.WriteLine($"Voltage is {voltage}");
                //    Channel.SendMessage(new byte[] { 0x00, 0x00, 0x07, 0xE0, 0x01, 0x11 });
                //    GetMessageResults Response = Channel.GetMessages(2,1000);
                //    if (Response.Messages.Length == 0)
                //    {
                //        MessageBox.Show("Error: No data");
                //        return;
                //    }
                //    else
                //    {
                //        Debug.WriteLine($"Message received: {Response.Messages.Length}");
                //        // print out the content of Reponse data in hexidecimal

                //        for (int i = 0; i < Response.Messages.Length; i++)
                //        {
                //            Debug.Write($"Message {i}: Timestamp: {Response.Messages[i].Timestamp}, Length: {Response.Messages[i].Data.Length}");
                //            for (int j=0; j < Response.Messages[i].Data.Length; j++)
                //            {
                //                Debug.Write($" {Response.Messages[i].Data[j]:X2}");
                //            }
                //            Debug.WriteLine("");
                //        }
                //    }
                //}


            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ((Button)sender).Enabled = true;
            }
        }
    }
}
