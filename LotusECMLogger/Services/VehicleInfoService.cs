using SAE.J2534;
using System.Diagnostics;

namespace LotusECMLogger.Services
{
    public class VehicleInfoService : IVehicleInfoService
    {
        public List<VehicleParameterReading> LoadVehicleData()
        {
            var vehicleDataSnapshot = new List<VehicleParameterReading>();

            try
            {
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
                ExecuteMode01Requests(channel, ecmHeader, vehicleDataSnapshot);

                // Execute Mode 0x22 requests
                ExecuteMode22Requests(channel, ecmHeader, vehicleDataSnapshot);

                return vehicleDataSnapshot;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading vehicle data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Execute Mode 0x01 (Service $01) OBD-II requests for standard vehicle data
        /// </summary>
        private void ExecuteMode01Requests(Channel channel, byte[] ecmHeader, List<VehicleParameterReading> vehicleDataSnapshot)
        {
            // Standard Mode 0x01 PIDs - these will be provided by user later
            // For now, create a basic set of common PIDs
            var mode01Requests = new List<(string Name, byte Pid)>
            {
                ("Engine RPM", 0x0C),
            };

            foreach (var (name, pid) in mode01Requests)
            {
                try
                {
                    // Build and send the request
                    byte[] message = BuildMode01Message(ecmHeader, pid);
                    channel.SendMessages([message]);

                    // Read response with timeout
                    var response = channel.GetMessages(1, 500); // 500ms timeout
                    if (response.Messages.Length > 0)
                    {
                        var readings = ParseMode01Response(response.Messages[0].Data, name, pid);
                        if (readings != null)
                        {
                            vehicleDataSnapshot.Add(readings);
                        }
                    }

                    // Small delay between requests to avoid overwhelming ECU
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to execute Mode 01 request {name}: {ex.Message}");
                }
            }
        }

        private byte[] BuildMode01Message(byte[] ecmHeader, byte pid)
        {
            var message = new byte[ecmHeader.Length + 3];
            Array.Copy(ecmHeader, message, ecmHeader.Length);
            message[ecmHeader.Length] = 0x01; // Service ID for Mode 01
            message[ecmHeader.Length + 1] = pid; // PID
            message[ecmHeader.Length + 2] = 0x00; // Terminator
            return message;
        }

        private VehicleParameterReading? ParseMode01Response(byte[] data, string name, byte pid)
        {
            // Check if response is valid (from ECM with correct header)
            if (data.Length <= 4 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x07 || data[3] != 0xE8)
            {
                return null;
            }

            // Check if this is a Mode 01 response
            if (data.Length < 6 || data[4] != 0x41) // 0x41 = Mode 01 response
            {
                return null;
            }

            // Check if PID matches
            if (data[5] != pid)
            {
                return null;
            }

            // Parse based on PID
            return pid switch
            {
                0x0C => ParseEngineRPM(data), // Engine RPM
                _ => null
            };
        }

        private VehicleParameterReading? ParseEngineRPM(byte[] data)
        {
            if (data.Length >= 8)
            {
                // Engine RPM: ((A*256)+B)/4
                int rpm = ((data[6] << 8) | data[7]) / 4;
                return new VehicleParameterReading
                {
                    Name = "Engine RPM",
                    Value = rpm.ToString(),
                    Unit = "RPM"
                };
            }
            return null;
        }

        /// <summary>
        /// Execute Mode 0x22 (Service $22) manufacturer-specific OBD-II requests
        /// </summary>
        private void ExecuteMode22Requests(Channel channel, byte[] ecmHeader, List<VehicleParameterReading> vehicleDataSnapshot)
        {
            // Mode 0x22 requests - these will be provided by user later
            // For now, create a basic set of Lotus-specific requests
            var mode22Requests = new List<(string Name, byte SubMode, byte Pid)>
            {
                ("TPS Target", 0x02, 0x3B),
                ("TPS Actual", 0x02, 0x45),
            };

            foreach (var (name, subMode, pid) in mode22Requests)
            {
                try
                {
                    // Build and send the request
                    byte[] message = BuildMode22Message(ecmHeader, subMode, pid);
                    channel.SendMessages([message]);

                    // Read response with timeout
                    var response = channel.GetMessages(1, 500); // 500ms timeout
                    if (response.Messages.Length > 0)
                    {
                        var readings = ParseMode22Response(response.Messages[0].Data, name, subMode, pid);
                        if (readings != null)
                        {
                            vehicleDataSnapshot.Add(readings);
                        }
                    }

                    // Small delay between requests to avoid overwhelming ECU
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to execute Mode 22 request {name}: {ex.Message}");
                }
            }
        }

        private byte[] BuildMode22Message(byte[] ecmHeader, byte subMode, byte pid)
        {
            var message = new byte[ecmHeader.Length + 4];
            Array.Copy(ecmHeader, message, ecmHeader.Length);
            message[ecmHeader.Length] = 0x22; // Service ID for Mode 22
            message[ecmHeader.Length + 1] = subMode; // Sub-mode
            message[ecmHeader.Length + 2] = pid; // PID
            message[ecmHeader.Length + 3] = 0x00; // Terminator
            return message;
        }

        private VehicleParameterReading? ParseMode22Response(byte[] data, string name, byte subMode, byte pid)
        {
            // Check if response is valid (from ECM with correct header)
            if (data.Length <= 4 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x07 || data[3] != 0xE8)
            {
                return null;
            }

            // Check if this is a Mode 22 response
            if (data.Length < 7 || data[4] != 0x62) // 0x62 = Mode 22 response
            {
                return null;
            }

            // Check if sub-mode and PID match
            if (data[5] != subMode || data[6] != pid)
            {
                return null;
            }

            // Parse based on PID
            return (subMode, pid) switch
            {
                (0x02, 0x3B) => ParseTPSTarget(data), // TPS Target
                (0x02, 0x45) => ParseTPSActual(data), // TPS Actual
                _ => null
            };
        }

        private VehicleParameterReading? ParseTPSTarget(byte[] data)
        {
            if (data.Length >= 10)
            {
                // TPS Target: (A*256 + B) * 100 / 1024
                int rawValue = (data[7] << 8) | data[8];
                double tpsTarget = rawValue * 100.0 / 1024.0;
                return new VehicleParameterReading
                {
                    Name = "TPS Target",
                    Value = Math.Round(tpsTarget, 2).ToString(),
                    Unit = "%"
                };
            }
            return null;
        }

        private VehicleParameterReading? ParseTPSActual(byte[] data)
        {
            if (data.Length >= 10)
            {
                // TPS Actual: (A*256 + B) * 100 / 1024
                int rawValue = (data[7] << 8) | data[8];
                double tpsActual = rawValue * 100.0 / 1024.0;
                return new VehicleParameterReading
                {
                    Name = "TPS Actual",
                    Value = Math.Round(tpsActual, 2).ToString(),
                    Unit = "%"
                };
            }
            return null;
        }
    }
}