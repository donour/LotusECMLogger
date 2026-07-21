using SAE.J2534;
using System.Diagnostics;
using System.Text;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Reads static and learned vehicle information from the Lotus ECM over a temporary
    /// J2534 ISO15765 session: Mode 0x09 identification PIDs (VIN, calibration ID, CVN, ECU
    /// name, in-use performance tracking), Mode 0x22 extended identification, per-cylinder
    /// octane scalers, and regional fuel-learn state.
    /// </summary>
    public class VehicleInfoService : IVehicleInfoService
    {
        public List<VehicleParameterReading> LoadVehicleData()
        {
            var readings = new List<VehicleParameterReading>();

            // Temporary connection scoped to this load; disposed before the caller runs any
            // probes that need their own separate CAN session.
            using var session = J2534Session.Open();
            var channel = session.OpenIso15765();

            // Setup message filter for the Lotus ECM
            channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

            var iso15765Service = new Iso15765Service(channel);

            // Query for available PIDs on service 0x09
            var availablePIDs = iso15765Service.GetSupportedPIDs(OBDIIMode.RequestVehicleInformation);

            // Mode 0x22 extended identification (serial, hardware, crypto flags, type, cal version)
            readings.AddRange(QueryMode22ExtendedInfo(channel));

            // Load values for all available Mode 0x09 PIDs
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
                            readings.Add(reading);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to read PID 0x{pid:X2}: {ex.Message}");
                }
            }

            readings.AddRange(QueryOctaneScalers(channel));

            readings.AddRange(QueryFuelLearnState(channel));

            return readings;
        }

        private static VehicleParameterReading? ParseVehicleInfoPID(int pid, byte[] data)
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

        private static VehicleParameterReading? ParseVIN(byte[] data)
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

        private static VehicleParameterReading? ParseCalibrationID(byte[] data)
        {
            if (data.Length >= 10)
            {
                var calId = Encoding.UTF8.GetString(data);

                return new VehicleParameterReading
                {
                    Name = "Calibration ID",
                    Value = calId,
                    Unit = ""
                };
            }
            return null;
        }

        private static VehicleParameterReading? ParseCalibrationVerificationNumbers(byte[] data)
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

        private static VehicleParameterReading? ParseECUName(byte[] data)
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

        private static VehicleParameterReading? ParseInUsePerformanceTracking(byte[] data, string name)
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

        private static List<VehicleParameterReading> QueryMode22ExtendedInfo(J2534Channel channel)
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
        private static byte[]? ReadMode22Payload(J2534Channel channel, byte pid, int payloadLength)
        {
            try
            {
                byte[] request = [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, pid];
                channel.SendMessage(request);

                for (int i = 0; i < 10; i++)
                {
                    var response = channel.ReadMessages(1, 250);
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
                Debug.WriteLine($"Failed to read Mode22 PID 0x02{pid:X2}: {ex.Message}");
            }
            return null;
        }

        private static List<VehicleParameterReading> QueryOctaneScalers(J2534Channel channel)
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
                        var response = channel.ReadMessages(1, 250);
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
                    Debug.WriteLine($"Failed to read {name}: {ex.Message}");
                }
            }
            return results;
        }

        // Reads the regional fuel-learn state via Mode 0x22 DIDs and returns a one-shot snapshot.
        private static List<VehicleParameterReading> QueryFuelLearnState(J2534Channel channel)
        {
            var results = new List<VehicleParameterReading>();

            // Zone trims: single offset-128 byte, 0x80 = 0%, ~0.391% per count.
            var zonePIDs = new (string Name, byte Pid)[]
            {
                ("Fuel Learn Zone 2 Bank 1", 0x48),
                ("Fuel Learn Zone 3 Bank 1", 0x49),
                ("Fuel Learn Zone 2 Bank 2", 0x5A),
                ("Fuel Learn Zone 3 Bank 2", 0x5B),
            };
            foreach (var (name, pid) in zonePIDs)
            {
                var bytes = ReadMode22Payload(channel, pid, 1);
                if (bytes != null)
                {
                    double correctionPct = (sbyte)(bytes[0] - 0x80) * 500.0 / 128 / 10;
                    results.Add(new VehicleParameterReading
                    {
                        Name = name,
                        Value = Math.Round(correctionPct, 1).ToString(),
                        Unit = "%"
                    });
                }
            }

            // Idle additive trims: signed 16-bit, microseconds added to injector pulse width.
            var leanTimePIDs = new (string Name, byte Pid)[]
            {
                ("Fuel Learn Lean Time Bank 1", 0x2E),
                ("Fuel Learn Lean Time Bank 2", 0x55),
            };
            foreach (var (name, pid) in leanTimePIDs)
            {
                var bytes = ReadMode22Payload(channel, pid, 2);
                if (bytes != null)
                {
                    short leanTime = (short)((bytes[0] << 8) | bytes[1]);
                    results.Add(new VehicleParameterReading
                    {
                        Name = name,
                        Value = leanTime.ToString(),
                        Unit = "us"
                    });
                }
            }

            // Learn dwell/update timer: unsigned 16-bit.
            var timerBytes = ReadMode22Payload(channel, 0x3A, 2);
            if (timerBytes != null)
            {
                int fuelLearnTimer = (timerBytes[0] << 8) | timerBytes[1];
                results.Add(new VehicleParameterReading
                {
                    Name = "Fuel Learn Timer",
                    Value = fuelLearnTimer.ToString(),
                    Unit = ""
                });
            }

            return results;
        }
    }
}
