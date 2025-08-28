using SAE.J2534;
using System.ComponentModel;
using System.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
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
        private Dictionary<String, float> liveData = new Dictionary<string, float>();
        private DateTime lastUpdateTime = DateTime.Now;
        private T6eCodingDecoder originalCodingDecoder;
        private T6eCodingDecoder modifiedCodingDecoder;
        private Dictionary<string, Control> codingControls = new Dictionary<string, Control>();
        private List<LiveDataReading> vehicleDataSnapshot = new List<LiveDataReading>();

        public event PropertyChangedEventHandler PropertyChanged;
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
            catch { }
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

            codingDataView.Columns.Add("Option", 200);
            codingDataView.Columns.Add("Value", 100);
            
            vehicleInfoView.Columns.Add("Information", 300);
            vehicleInfoView.Columns.Add("Value", 200);
            vehicleInfoView.Columns.Add("Unit", 100);
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

        private void ObdConfigMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ToolStripMenuItem item in obdConfigToolStripMenuItem.DropDownItems)
                item.Checked = false;
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

        private void aboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }
        
        private void LoadVehicleDataButton_Click(object sender, EventArgs e)
        {
            // Safety check: prevent vehicle data loading while logging is active
            if (loggerEnabled)
            {
                MessageBox.Show("Cannot load vehicle data while logging is active. Please stop the logger first.", 
                    "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            try
            {
                loadVehicleDataButton.Enabled = false;
                loadVehicleDataButton.Text = "Loading...";
                
                // Clear previous data
                vehicleDataSnapshot.Clear();
                
                // Create temporary device connection for vehicle data loading
                string DllFileName = APIFactory.GetAPIinfo().First().Filename;
                API API = APIFactory.GetAPI(DllFileName);
                using Device device = API.GetDevice();
                using Channel channel = device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE);
                
                // Start message filter
                var flowControlFilter = new MessageFilter
                {
                    FilterType = Filter.FLOW_CONTROL_FILTER,
                    Mask = [0xFF, 0xFF, 0xFF, 0xFF],
                    Pattern = [0x00, 0x00, 0x07, 0xE8],
                    FlowControl = [0x00, 0x00, 0x07, 0xE0]
                };
                channel.StartMsgFilter(flowControlFilter);
                
                // Create ECM header for Lotus vehicles
                byte[] ecmHeader = [0x00, 0x00, 0x07, 0xE0];
                
                // Execute Mode 0x01 requests first
                ExecuteMode01Requests(channel, ecmHeader);
                
                // Execute Mode 0x22 requests
                ExecuteMode22Requests(channel, ecmHeader);
                
                // Update the vehicle info view with collected data
                UpdateVehicleInfoView();
                
                MessageBox.Show($"Successfully loaded {vehicleDataSnapshot.Count} vehicle data points!", 
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading vehicle data: {ex.Message}", "Load Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loadVehicleDataButton.Enabled = true;
                loadVehicleDataButton.Text = "Load Vehicle Data";
            }
        }
        
        /// <summary>
        /// Execute Mode 0x01 (Service $01) OBD-II requests for standard vehicle data
        /// </summary>
        private void ExecuteMode01Requests(Channel channel, byte[] ecmHeader)
        {
            // Standard Mode 0x01 PIDs - these will be provided by user later
            // For now, create a basic set of common PIDs
            var mode01Requests = new List<Mode01Request>
            {
                new Mode01Request("Engine RPM", 0x0C),
            };
            
            foreach (var request in mode01Requests)
            {
                try
                {
                    // Build and send the request
                    byte[] message = request.BuildMessage(ecmHeader);
                    channel.SendMessages([message]);
                    
                    // Read response with timeout
                    var response = channel.GetMessages(1, 500); // 500ms timeout
                    if (response.Messages.Length > 0)
                    {
                        var readings = LiveDataReading.ParseCanResponse(response.Messages[0].Data);
                        vehicleDataSnapshot.AddRange(readings);
                    }
                    
                    // Small delay between requests to avoid overwhelming ECU
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to execute Mode 01 request {request.Name}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Execute Mode 0x22 (Service $22) manufacturer-specific OBD-II requests
        /// </summary>
        private void ExecuteMode22Requests(Channel channel, byte[] ecmHeader)
        {
            // Mode 0x22 requests - these will be provided by user later
            // For now, create a basic set of Lotus-specific requests
            var mode22Requests = new List<Mode22Request>
            {
                new Mode22Request("TPS Target", 0x02, 0x3B),
                new Mode22Request("TPS Actual", 0x02, 0x45),
            };
            
            foreach (var request in mode22Requests)
            {
                try
                {
                    // Build and send the request
                    byte[] message = request.BuildMessage(ecmHeader);
                    channel.SendMessages([message]);
                    
                    // Read response with timeout
                    var response = channel.GetMessages(1, 500); // 500ms timeout
                    if (response.Messages.Length > 0)
                    {
                        var readings = LiveDataReading.ParseCanResponse(response.Messages[0].Data);
                        vehicleDataSnapshot.AddRange(readings);
                    }
                    
                    // Small delay between requests to avoid overwhelming ECU
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to execute Mode 22 request {request.Name}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Update the vehicle info ListView with the collected data snapshot
        /// </summary>
        private void UpdateVehicleInfoView()
        {
            vehicleInfoView.BeginUpdate();
            vehicleInfoView.Items.Clear();
            
            foreach (var reading in vehicleDataSnapshot)
            {
                var item = new ListViewItem(reading.name);
                item.SubItems.Add(reading.value_f.ToString("F2"));
                
                // Determine unit based on parameter name
                string unit = DetermineUnit(reading.name);
                item.SubItems.Add(unit);
                
                vehicleInfoView.Items.Add(item);
            }
            
            vehicleInfoView.EndUpdate();
        }
        
        /// <summary>
        /// Determine the appropriate unit for a given parameter name
        /// </summary>
        private static string DetermineUnit(string parameterName)
        {
            return parameterName.ToLowerInvariant() switch
            {
                var name when name.Contains("temperature") || name.Contains("temp") => "°C",
                var name when name.Contains("speed") && name.Contains("engine") => "RPM",
                var name when name.Contains("speed") && name.Contains("vehicle") => "km/h",
                var name when name.Contains("pressure") => "kPa",
                var name when name.Contains("position") || name.Contains("throttle") || name.Contains("pedal") => "%",
                var name when name.Contains("fuel") && name.Contains("trim") => "%",
                var name when name.Contains("advance") || name.Contains("retard") || name.Contains("vvti") => "°",
                var name when name.Contains("torque") => "Nm",
                var name when name.Contains("maf") => "g/s",
                var name when name.Contains("load") => "%",
                var name when name.Contains("pulse") => "μs",
                var name when name.Contains("misfire") => "count",
                var name when name.Contains("octane") => "rating",
                var name when name.Contains("duty") => "%",
                var name when name.Contains("ratio") => "λ",
                _ => ""
            };
        }
        
        /// <summary>
        /// Standalone method to read coding data from ECU without starting the logger
        /// </summary>
        private static T6eCodingDecoder GetCodingDataStandalone(Channel channel)
        {
            byte[] result_cod0 = [0, 0, 0, 0];
            byte[] result_cod1 = [0, 0, 0, 0];

            byte[][] codingRequest =
            [
                [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x63],
                [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x64]
            ];
            int done = 0;
            do
            {
                channel.SendMessages(codingRequest);
                GetMessageResults resp = channel.GetMessages(1, 100);
                if (resp.Messages.Length > 0)
                {
                    var data = resp.Messages[0].Data;
                    if (data.Length >= 11)
                    {
                        // These bytes are in the reverse order of the value at the sender.
                        /*
                          case 0x263:
                                obd_ii_response[3] = (byte)COD_base[1];
                                obd_ii_response[4] = COD_base[1]._2_1_;
                                uVar4 = 7;
                                obd_ii_response._5_2_ = CONCAT11(COD_base[1]._1_1_,COD_base[1]._0_1_);
                                break;
                          case 0x264:
                                obd_ii_response[3] = (byte)COD_base[0];
                                obd_ii_response[4] = COD_base[0]._2_1_;
                                uVar4 = 7;
                                obd_ii_response._5_2_ = CONCAT11(COD_base[0]._1_1_,COD_base[0]._0_1_);
                                break;
                         */
                        if (data[4] == 0x62 && data[5] == 0x02)
                        {
                            if (data[6] == 0x63)
                            {
                                result_cod1 = data[7..11];
                                done |= 1;
                            }
                            if (data[6] == 0x64)
                            {
                                result_cod0 = data[7..11];
                                done |= 2;
                            }
                        }
                    }
                }
            } while (done != 3);

            return new T6eCodingDecoder(result_cod1, result_cod0);
        }
        
        /// <summary>
        /// Standalone method to write coding data to ECU without using the logger
        /// </summary>
        private static (bool success, string errorMessage) WriteCodingToECUStandalone(T6eCodingDecoder codingDecoder)
        {
            try
            {
                // Create device connection for coding write
                string DllFileName = APIFactory.GetAPIinfo().First().Filename;
                API API = APIFactory.GetAPI(DllFileName);
                using Device device = API.GetDevice();

                // Use raw CAN approach (matching ECU expectations)
                return WriteRawCANCodingStandalone(codingDecoder, device);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to write coding: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Standalone method to write raw CAN coding data
        /// </summary>
        private static (bool success, string errorMessage) WriteRawCANCodingStandalone(T6eCodingDecoder codingDecoder, Device device)
        {
            // Coding data is written as one continous 8 byte message.
            /*
                  if (input_format == 0) {
                      COD_base[0] = CONCAT13(cod_new[3],CONCAT12(cod_new[2],CONCAT11(cod_new[1],*cod_new)));
                      COD_base[1] = CONCAT13(cod_new[7],CONCAT12(cod_new[6],CONCAT11(cod_new[5],cod_new[4])));
                    }
                    else {
                      COD_base[0] = *(uint32_t *)(cod_new + 4);
                      COD_base[1] = *(uint32_t *)cod_new;
                    }
            */
            try
            {
                // Use raw CAN protocol to send directly to 0x502
                using Channel canChannel = device.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);

                // Get the high and low bytes separately to match ECU expectations
                byte[] highBytes = codingDecoder.GetHighBytes();
                byte[] lowBytes = codingDecoder.GetLowBytes();

                // Create raw CAN message with ID 0x502 and 8 bytes of coding data
                byte[] canMessage = new byte[12]; // 4 bytes header + 8 bytes data

                // CAN header for 11-bit ID 0x502
                canMessage[0] = 0x00;
                canMessage[1] = 0x00;
                canMessage[2] = 0x05;
                canMessage[3] = 0x02;

                // ECU expects: high bytes first (0-3), then low bytes (4-7)
                Array.Copy(highBytes, 0, canMessage, 4, 4);  // Bytes 4-7: high bytes
                Array.Copy(lowBytes, 0, canMessage, 8, 4);   // Bytes 8-11: low bytes

                // TODO sent messages to other modules
                // Send the message
                canChannel.SendMessages([canMessage]);

                // Wait a bit for ECU to process
                Thread.Sleep(100);

                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, $"Raw CAN coding write failed: {ex.Message}");
            }
        }
    }
}
