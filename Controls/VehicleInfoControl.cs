using LotusECMLogger.Services;
using SAE.J2534;

namespace LotusECMLogger.Controls
{
    public partial class VehicleInfoControl : UserControl
    {
        private readonly IVehicleInfoService _vehicleInfoService;
        private List<VehicleParameterReading> vehicleDataSnapshot = [];

        public VehicleInfoControl()
        {
            InitializeComponent();
            _vehicleInfoService = new VehicleInfoService();
        }

        private void readDataButton_Click(object sender, EventArgs e)
        {
            LoadVehicleData();
        }

        private void LoadVehicleData()
        {
            try
            {
                readDataButton.Enabled = false;
                readDataButton.Text = "Loading...";
                statusLabel.Text = "Connecting to vehicle...";

                // Clear previous data
                vehicleDataSnapshot.Clear();

                // Create J2534 connection and ISO15765 service
                using (var api = APIFactory.GetAPI(APIFactory.GetAPIinfo().First().Filename))
                using (var device = api.GetDevice())
                using (var channel = device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE))
                {
                    // Setup message filter for Lotus ECM
                    var flowControlFilter = new SAE.J2534.MessageFilter
                    {
                        FilterType = Filter.FLOW_CONTROL_FILTER,
                        Mask = [0xFF, 0xFF, 0xFF, 0xFF],
                        Pattern = [0x00, 0x00, 0x07, 0xE8],
                        FlowControl = [0x00, 0x00, 0x07, 0xE0]
                    };
                    channel.StartMsgFilter(flowControlFilter);

                    // Create ISO15765 service
                    var iso15765Service = new Iso15765Service(channel);

                    statusLabel.Text = "Querying supported PIDs...";

                    // Query for available PIDs on service 0x09
                    var availablePIDs = iso15765Service.GetSupportedPIDs(OBDIIMode.RequestVehicleInformation);

                    statusLabel.Text = $"Loading data for {availablePIDs.Count} PIDs...";

                    // Load values for all available PIDs
                    foreach (var pid in availablePIDs)
                    {
                        try
                        {
                            var pidData = iso15765Service.GetPID(OBDIIMode.RequestVehicleInformation, pid);
                            if (pidData != null && pidData.Length > 0)
                            {
                                var reading = ParseVehicleInfoPID(pid, pidData);
                                if (reading != null)
                                {
                                    vehicleDataSnapshot.Add(reading);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to read PID 0x{pid:X2}: {ex.Message}");
                        }
                    }
                }

                // Update the UI
                UpdateVehicleInfoView();

                statusLabel.Text = $"Loaded {vehicleDataSnapshot.Count} vehicle data points";
                MessageBox.Show($"Successfully loaded {vehicleDataSnapshot.Count} vehicle data points!",
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error loading data";
                MessageBox.Show($"Error loading vehicle data: {ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                readDataButton.Enabled = true;
                readDataButton.Text = "Load Vehicle Data";
            }
        }

        private void UpdateVehicleInfoView()
        {
            vehicleInfoView.BeginUpdate();
            vehicleInfoView.Items.Clear();

            foreach (var reading in vehicleDataSnapshot)
            {
                var item = new ListViewItem(reading.Name);
                item.SubItems.Add(reading.Value.ToString("F2"));
                item.SubItems.Add(reading.Unit);

                vehicleInfoView.Items.Add(item);
            }

            vehicleInfoView.EndUpdate();
        }

        private VehicleParameterReading? ParseVehicleInfoPID(int pid, byte[] data)
        {
            // Parse based on PID
            return pid switch
            {
                0x02 => ParseVIN(data),           // VIN
                //0x02 => ParseCalibrationID(data), // Calibration ID
                //0x03 => ParseCalibrationVerificationNumbers(data), // CVN
                //0x04 => ParseInUsePerformanceTracking(data, "Compression Ignition IPT"),
                //0x05 => ParseECUName(data),       // ECU Name
                //0x06 => ParseInUsePerformanceTracking(data, "Spark Ignition IPT"),
                //0x07 => ParseInUsePerformanceTracking(data, "Compression Ignition IPT 2"),
                //0x08 => ParseInUsePerformanceTracking(data, "Spark Ignition IPT 2"),
                //0x09 => ParseECUName(data),       // ECU Name 2
                //0x0A => ParseInUsePerformanceTracking(data, "Compression Ignition IPT 3"),
                //0x0B => ParseInUsePerformanceTracking(data, "Spark Ignition IPT 3"),
                _ => null
            };
        }

        private VehicleParameterReading? ParseVIN(byte[] data)
        {
            if (data.Length == 17) // VIN is 17 characters, plus header
            {
                // TODO this seems needless complicatd
                var vinChars = new char[17];
                for (int i = 0; i < 17; i++)
                {
                    vinChars[i] = (char)data[i];
                }
                var vin = new string(vinChars);

                return new VehicleParameterReading
                {
                    Name = "Vehicle Identification Number",
                    Value = 0, // VIN is text, not numeric
                    Unit = vin
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseCalibrationID(byte[] data)
        {
            if (data.Length >= 10)
            {
                // Calibration ID is typically ASCII text
                var calIdChars = new char[Math.Min(16, data.Length - 6)];
                for (int i = 0; i < calIdChars.Length; i++)
                {
                    calIdChars[i] = (char)data[i + 6];
                }
                var calId = new string(calIdChars).TrimEnd('\0');

                return new VehicleParameterReading
                {
                    Name = "Calibration ID",
                    Value = 0,
                    Unit = calId
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseCalibrationVerificationNumbers(byte[] data)
        {
            if (data.Length >= 14) // CVN is 4 bytes
            {
                // CVN is 4 bytes starting at offset 6
                uint cvn = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9]);

                return new VehicleParameterReading
                {
                    Name = "Calibration Verification Numbers",
                    Value = cvn,
                    Unit = "CVN"
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseECUName(byte[] data)
        {
            if (data.Length >= 10)
            {
                // ECU Name is typically ASCII text
                var ecuNameChars = new char[Math.Min(16, data.Length - 6)];
                for (int i = 0; i < ecuNameChars.Length; i++)
                {
                    ecuNameChars[i] = (char)data[i + 6];
                }
                var ecuName = new string(ecuNameChars).TrimEnd('\0');

                return new VehicleParameterReading
                {
                    Name = "ECU Name",
                    Value = 0,
                    Unit = ecuName
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseInUsePerformanceTracking(byte[] data, string name)
        {
            if (data.Length >= 10)
            {
                // IPT data is typically 4 bytes
                uint ipt = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9]);

                return new VehicleParameterReading
                {
                    Name = name,
                    Value = ipt,
                    Unit = "IPT"
                };
            }
            return null;
        }
    }
}
