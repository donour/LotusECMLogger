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

            readings.AddRange(QueryLearnedValues(channel));

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

        /// <summary>
        /// Learned Mode 22 values shown on the vehicle information tab. Only the label, unit and
        /// rounding live here: the PID's payload width and how its bytes become a number both come
        /// from <see cref="ObdPidDecoders.Mode22"/>, so this view and the live-data log cannot
        /// disagree about what a PID means. They did once -- over the octane cylinder order.
        /// </summary>
        private static IEnumerable<(byte PidLow, string Name, string Unit, int Decimals)> LearnedValueChannels()
        {
            foreach (var (pid, cylinder) in OctaneScaler.CylinderByPid)
                yield return (pid, $"Octane Scaler Cyl {cylinder}", "%", 1);

            yield return (0x48, "Fuel Learn Zone 2 Bank 1", "%", 1);
            yield return (0x49, "Fuel Learn Zone 3 Bank 1", "%", 1);
            yield return (0x5A, "Fuel Learn Zone 2 Bank 2", "%", 1);
            yield return (0x5B, "Fuel Learn Zone 3 Bank 2", "%", 1);

            // Idle additive trims: microseconds added to injector pulse width.
            yield return (0x2E, "Fuel Learn Lean Time Bank 1", "us", 0);
            yield return (0x55, "Fuel Learn Lean Time Bank 2", "us", 0);

            yield return (0x3A, "Fuel Learn Timer", "", 0);
        }

        /// <summary>Reads the learned octane scalers and regional fuel-learn state.</summary>
        private static List<VehicleParameterReading> QueryLearnedValues(J2534Channel channel)
        {
            var results = new List<VehicleParameterReading>();

            foreach (var (pidLow, name, unit, decimals) in LearnedValueChannels())
            {
                if (ReadMode22Value(channel, pidLow) is double value)
                {
                    results.Add(new VehicleParameterReading
                    {
                        Name = name,
                        Value = Math.Round(value, decimals).ToString(),
                        Unit = unit,
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Reads one Mode 22 PID and decodes it with the same table the live-data logger uses. The
        /// payload width comes from that table too, so it is stated once rather than restated here.
        /// Null when the ECU does not answer or the PID has no shared decoder.
        /// </summary>
        private static double? ReadMode22Value(J2534Channel channel, byte pidLow)
        {
            ushort pid = (ushort)(0x0200 | pidLow);
            if (!ObdPidDecoders.Mode22.TryGetValue(pid, out var decoder))
                return null;

            byte[]? payload = ReadMode22Payload(channel, pidLow, decoder.Width);
            if (payload == null)
                return null;

            var readings = ObdPidDecoders.DecodeMode22Payload(pid, payload);
            return readings.Count == 1 ? readings[0].value_f : null;
        }
    }
}