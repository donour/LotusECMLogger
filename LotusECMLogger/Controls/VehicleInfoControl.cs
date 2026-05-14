using LotusECMLogger.Services;
using SAE.J2534;
using System.Collections;
using System.Text;

namespace LotusECMLogger.Controls
{
    public partial class VehicleInfoControl : UserControl
    {
        private readonly IVehicleInfoService _vehicleInfoService;
        private readonly IObdResetService _resetService;
        private readonly IVinSetService _vinSetService;
        private List<VehicleParameterReading> vehicleDataSnapshot = [];

        private bool _isLoggerActive;
        public bool IsLoggerActive
        {
            get => _isLoggerActive;
            set
            {
                _isLoggerActive = value;
                resetButton.Enabled = !value;
                setVinButton.Enabled = !value;
            }
        }

        public VehicleInfoControl()
        {
            InitializeComponent();
            _vehicleInfoService = new VehicleInfoService();
            _resetService = new J2534ObdResetService();
            _vinSetService = new J2534VinSetService();
            SetupListViewColumns();
            GuiIcons.ApplyToButton(readDataButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(resetButton, GuiIcons.UpdateRestore);
            GuiIcons.ApplyToButton(setVinButton, GuiIcons.Write);
        }

        private void SetupListViewColumns()
        {
            vehicleInfoView.Columns.Clear();
            vehicleInfoView.Columns.Add("Parameter", 200);
            vehicleInfoView.Columns.Add("Value", 150);
            vehicleInfoView.Columns.Add("Unit", 150);
        }

        private void readDataButton_Click(object sender, EventArgs e)
        {
            LoadVehicleData();
        }

        private void setVinButton_Click(object? sender, EventArgs e)
        {
            if (_isLoggerActive)
            {
                MessageBox.Show("Cannot set VIN while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var currentVin = vehicleDataSnapshot
                .FirstOrDefault(r => r.Name == "Vehicle Identification Number")?.Value?.Trim();

            using var dialog = new SetVinDialog(_vinSetService, currentVin);
            var result = dialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                LoadVehicleData();
            }
        }

        private void resetButton_Click(object? sender, EventArgs e)
        {
            if (_isLoggerActive)
            {
                MessageBox.Show("Cannot perform reset while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you need to perform an OBD-II learned data reset?\n\n" +
                "This operation cannot be reversed and may affect drivability until the ECU relearns.\n\n" +
                "Confirm to proceed.",
                "Confirm OBD-II Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                resetButton.Enabled = false;
                resetButton.Text = "Resetting...";

                var (success, error) = _resetService.PerformLearningReset();
                if (success)
                    MessageBox.Show("OBD-II learned data reset request sent successfully.", "Reset Sent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show($"Failed to send reset: {error}", "Reset Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                resetButton.Enabled = !_isLoggerActive;
                resetButton.Text = "Perform Reset";
            }
        }

        // TODO: LoadVehicleData runs all J2534 work (connection, filter, queries) on the UI thread, freezing the app.
        // Move to Task.Run() and use Invoke/BeginInvoke for status label updates and the final ListView refresh.
        //
        // TODO: VehicleInfoService is instantiated but never called — all protocol work is duplicated inline here.
        // Consolidate: move the full implementation (mode 0x09, mode 0x22, octane scalers) into VehicleInfoService
        // and have this control delegate to it. VehicleInfoService currently has stubs (TPS Target/Actual) that
        // do not match the real parsing and should be removed.
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

                    statusLabel.Text = "Reading extended ECU info...";
                    vehicleDataSnapshot.AddRange(QueryMode22ExtendedInfo(channel));

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

                    statusLabel.Text = "Reading octane scalers...";
                    vehicleDataSnapshot.AddRange(QueryOctaneScalers(channel));

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
                item.SubItems.Add(reading.Value);
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
                0x02 => ParseVIN(data),
                0x04 => ParseCalibrationID(data),
                0x06 => ParseCalibrationVerificationNumbers(data),
                0x05 => ParseInUsePerformanceTracking(data, "Compression Ignition IPT"),
                0x0A => ParseECUName(data),
                0x0C => ParseInUsePerformanceTracking(data, "Spark Ignition IPT 3"),
                _ => null
            };
        }

        private VehicleParameterReading? ParseVIN(byte[] data)
        {
            if (data.Length == 17) // VIN is 17 characters, plus header
            {
                var vin = Encoding.UTF8.GetString(data);

                return new VehicleParameterReading
                {
                    Name = "Vehicle Identification Number",
                    Value = vin,
                    Unit = ""
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseCalibrationID(byte[] data)
        {
            if (data.Length >= 10)
            {
                var calId= Encoding.UTF8.GetString(data);

                return new VehicleParameterReading
                {
                    Name = "Calibration ID",
                    Value = calId,
                    Unit = ""
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseCalibrationVerificationNumbers(byte[] data)
        {
            if (data.Length >= 4) // CVN is 4 bytes
            {
                // CVN is 4 bytes starting at offset 6
                uint cvn = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);

                return new VehicleParameterReading
                {
                    Name = "Calibration Verification Numbers",
                    Value = $"0x{cvn:X8}",
                    Unit = ""
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseECUName(byte[] data)
        {
            if (data.Length >= 10)
            {
                var ecuName = Encoding.UTF8.GetString(data);

                return new VehicleParameterReading
                {
                    Name = "ECU Name",
                    Value = ecuName,
                    Unit = ""
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
                    Value = ipt.ToString(),
                    Unit = "IPT"
                };
            }
            return null;
        }

        private List<VehicleParameterReading> QueryMode22ExtendedInfo(SAE.J2534.Channel channel)
        {
            var results = new List<VehicleParameterReading>();

            // ECU_serial_number: PID 0x020E, 4 bytes
            var serialBytes = ReadMode22Payload(channel, 0x0E, 4);
            if (serialBytes != null)
                results.Add(new VehicleParameterReading
                {
                    Name = "ECU Serial Number",
                    Value = BitConverter.ToString(serialBytes).Replace("-", " "),
                    Unit = ""
                });

            // hardware_number: PID 0x020F, 4 bytes
            var hwBytes = ReadMode22Payload(channel, 0x0F, 4);
            if (hwBytes != null)
                results.Add(new VehicleParameterReading
                {
                    Name = "Hardware Number",
                    Value = BitConverter.ToString(hwBytes).Replace("-", " "),
                    Unit = ""
                });

            // crypto_flags: PID 0x0210, 4 bytes
            var cryptoBytes = ReadMode22Payload(channel, 0x10, 4);
            if (cryptoBytes != null)
                results.Add(new VehicleParameterReading
                {
                    Name = "Crypto Flags",
                    Value = BitConverter.ToString(cryptoBytes).Replace("-", " "),
                    Unit = ""
                });

            // ECU_type: PID 0x0211, 4 bytes
            var typeBytes = ReadMode22Payload(channel, 0x11, 4);
            if (typeBytes != null)
                results.Add(new VehicleParameterReading
                {
                    Name = "ECU Type",
                    Value = BitConverter.ToString(typeBytes).Replace("-", " "),
                    Unit = ""
                });

            // CAL_prog_version: char[32], 4 bytes per PID across 8 PIDs.
            // PID 0x20 is the supported-PID bitmap and is intentionally skipped.
            byte[] calVersionPids = [0x1C, 0x1D, 0x1E, 0x1F, 0x21, 0x22, 0x23, 0x24];
            var versionBytes = new List<byte>();
            foreach (var pid in calVersionPids)
            {
                var chunk = ReadMode22Payload(channel, pid, 4);
                if (chunk != null) versionBytes.AddRange(chunk);
            }
            if (versionBytes.Count > 0)
                results.Add(new VehicleParameterReading
                {
                    Name = "CAL Program Version",
                    Value = Encoding.ASCII.GetString([.. versionBytes]).TrimEnd('\0'),
                    Unit = ""
                });

            return results;
        }

        // Sends a Mode 22 request with PID [0x02, pid] and returns payloadLength bytes
        // starting at data[7], or null if the ECU does not respond with a matching positive response.
        private byte[]? ReadMode22Payload(SAE.J2534.Channel channel, byte pid, int payloadLength)
        {
            try
            {
                byte[] request = [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, pid];
                channel.SendMessage(request);

                for (int i = 0; i < 10; i++)
                {
                    var response = channel.GetMessages(1, 250);
                    if (response.Messages.Length > 0)
                    {
                        var data = response.Messages[0].Data;
                        // 4 ISO-TP header + 0x62 + 2 PID bytes + payload
                        if (data.Length >= 7 + payloadLength &&
                            data[4] == 0x62 && data[5] == 0x02 && data[6] == pid)
                            return data[7..(7 + payloadLength)];
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read Mode22 PID 0x02{pid:X2}: {ex.Message}");
            }
            return null;
        }

        private List<VehicleParameterReading> QueryOctaneScalers(SAE.J2534.Channel channel)
        {
            var results = new List<VehicleParameterReading>();
            var scalerPIDs = new (string Name, byte Pid)[]
            {
                ("Octane Scaler Cyl 1", 0x18),
                ("Octane Scaler Cyl 2", 0x19),
                ("Octane Scaler Cyl 3", 0x1A),
                ("Octane Scaler Cyl 4", 0x1B),
                ("Octane Scaler Cyl 5", 0x4D),
                ("Octane Scaler Cyl 6", 0x4E),
            };

            foreach (var (name, pid) in scalerPIDs)
            {
                try
                {
                    byte[] request = [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, pid];
                    channel.SendMessage(request);

                    for (int i = 0; i < 10; i++)
                    {
                        var response = channel.GetMessages(1, 250);
                        if (response.Messages.Length > 0)
                        {
                            var data = response.Messages[0].Data;
                            if (data.Length >= 9 &&
                                data[4] == 0x62 && data[5] == 0x02 && data[6] == pid)
                            {
                                int rawValue = (data[7] << 8) | data[8];
                                double percent = rawValue / 65535.0 * 100.0;
                                results.Add(new VehicleParameterReading
                                {
                                    Name = name,
                                    Value = Math.Round(percent, 1).ToString(),
                                    Unit = "%"
                                });
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read {name}: {ex.Message}");
                }
            }
            return results;
        }
    }
}
