using System.Collections.Frozen;
using System.Diagnostics;

namespace LotusECMLogger
{
    internal class LiveDataReading
    {
        public String name = "None";
        public double value_f;
        public long value_l;
        public string? ecuSource; // Optional: which ECU this reading came from

        public override string ToString()
        {
            return $"<{name}: {value_f}>";
        }

        /// <summary>
        /// Parse CAN response from any ECU (legacy method - assumes ECM 0x7E8)
        /// </summary>
        public static List<LiveDataReading> ParseCanResponse(byte[] data)
        {
            return ParseCanResponse(data, null);
        }

        /// <summary>
        /// Parse CAN response with ECU context for multi-ECU logging
        /// </summary>
        /// <param name="data">Raw CAN message data</param>
        /// <param name="ecu">ECU definition (null for legacy single-ECU mode)</param>
        /// <param name="prefixWithEcuName">Whether to prefix reading names with ECU name</param>
        public static List<LiveDataReading> ParseCanResponse(byte[] data, ECUDefinition? ecu, bool prefixWithEcuName = false)
        {
            List<LiveDataReading> results = [];

            if (data.Length <= 4)
                return results;

            // Check if response matches expected ECU
            uint responseId = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);

            if (ecu != null)
            {
                // Multi-ECU mode: check against specific ECU
                if (responseId != ecu.ResponseId)
                    return results;
            }
            else
            {
                // Legacy mode: only accept ECM responses (0x7E8)
                if (responseId != 0x7E8)
                    return results;
            }

            // Check for AEM UEGO specific parsing
            if (ecu != null && ecu.Name.Contains("UEGO", StringComparison.OrdinalIgnoreCase))
            {
                results = ParseAemUegoResponse(data, ecu, prefixWithEcuName);
                return results;
            }

            // Standard OBD-II parsing
            results = ParseStandardObdResponse(data, ecu, prefixWithEcuName);
            return results;
        }

        /// <summary>
        /// Parse AEM X-Series UEGO specific response
        /// </summary>
        private static List<LiveDataReading> ParseAemUegoResponse(byte[] data, ECUDefinition ecu, bool prefixWithEcuName)
        {
            List<LiveDataReading> results = [];
            string prefix = prefixWithEcuName ? $"{ecu.Name}:" : "";

            // AEM UEGO typically sends lambda/AFR data in a specific format
            // Common AEM X-Series CAN format: Lambda is sent as 2 bytes (0-65535 = 0.5-1.523 lambda)
            // Or AFR sent as 2 bytes where value / 10 = AFR

            if (data.Length >= 6)
            {
                int obdMode = data[4] - 0x40;

                if (obdMode == 0x01 && data.Length >= 7)
                {
                    // Mode 01 response from UEGO
                    byte pid = data[5];

                    if (pid == 0x24 && data.Length >= 10)
                    {
                        // PID 0x24: O2 Sensor 1 (Bank 1, Sensor 1) - Air-Fuel Equivalence Ratio (lambda) and Voltage
                        // Bytes: A, B for lambda; C, D for voltage
                        // Lambda = (2/65536) * (256*A + B)
                        // Voltage = (8/65536) * (256*C + D)
                        int A = data[6];
                        int B = data[7];
                        int C = data[8];
                        int D = data[9];

                        double lambda = (2.0 / 65536.0) * ((A << 8) | B);
                        double voltage = (8.0 / 65536.0) * ((C << 8) | D);

                        results.Add(new LiveDataReading
                        {
                            name = $"{prefix}Lambda",
                            value_f = lambda,
                            ecuSource = ecu.Name
                        });

                        // Also provide AFR (assuming gasoline stoich of 14.7)
                        double afr = lambda * 14.7;
                        results.Add(new LiveDataReading
                        {
                            name = $"{prefix}AFR",
                            value_f = afr,
                            ecuSource = ecu.Name
                        });

                        results.Add(new LiveDataReading
                        {
                            name = $"{prefix}O2 Voltage",
                            value_f = voltage,
                            ecuSource = ecu.Name
                        });
                    }
                }
                else if (obdMode == 0x22 && data.Length >= 8)
                {
                    // Mode 22 response from UEGO
                    // Parse based on PID
                    byte pidHigh = data[5];
                    byte pidLow = data[6];

                    // Generic lambda/AFR parsing - adjust based on actual AEM protocol
                    if (data.Length >= 9)
                    {
                        int rawValue = (data[7] << 8) | data[8];

                        // Lambda calculation (typical AEM: 0-65535 maps to 0.5-1.523)
                        double lambda = 0.5 + (rawValue / 65535.0) * 1.023;
                        results.Add(new LiveDataReading
                        {
                            name = $"{prefix}Lambda",
                            value_f = lambda,
                            ecuSource = ecu.Name
                        });

                        // Also provide AFR (assuming gasoline stoich of 14.7)
                        double afr = lambda * 14.7;
                        results.Add(new LiveDataReading
                        {
                            name = $"{prefix}AFR",
                            value_f = afr,
                            ecuSource = ecu.Name
                        });
                    }
                }
            }

            return results;
        }


        /// <summary>
        /// Parse a standard OBD-II response (Mode 01, 09 or 22) using the PID tables in
        /// <see cref="ObdPidDecoders"/>.
        /// </summary>
        private static List<LiveDataReading> ParseStandardObdResponse(byte[] data, ECUDefinition? ecu, bool prefixWithEcuName)
        {
            List<LiveDataReading> results = [];
            int obd_mode = data[4] - 0x40;

            switch (obd_mode)
            {
                case 0x01:
                    DecodePidSequence(data, results, ObdPidDecoders.Mode01,
                        ObdPidDecoders.Mode01StandardWidths, "Mode01");
                    break;
                case 0x09:
                    DecodePidSequence(data, results, ObdPidDecoders.Mode09, null, "Mode09");
                    break;
                case 0x22:
                    DecodeMode22(data, results);
                    break;
                default:
                    Debug.WriteLine($"Unknown OBD-II mode: {obd_mode:X2}");
                    break;
            }

            // Apply prefix and ecuSource to all readings if in multi-ECU mode
            string prefix = (prefixWithEcuName && ecu != null) ? $"{ecu.Name}:" : "";
            string? ecuSource = ecu?.Name;
            if (prefixWithEcuName || ecuSource != null)
            {
                foreach (var reading in results)
                {
                    if (prefixWithEcuName && !string.IsNullOrEmpty(prefix))
                    {
                        reading.name = $"{prefix}{reading.name}";
                    }
                    reading.ecuSource = ecuSource;
                }
            }

            return results;
        }

        /// <summary>
        /// Walks a single-byte-PID response -- [header][SID][pid][payload][pid][payload]... --
        /// decoding each PID and stepping the cursor by that PID's declared width.
        /// </summary>
        /// <remarks>
        /// Decoding stops at the first PID whose width is unknown. Where the next PID begins cannot be
        /// worked out without it, and guessing walks the cursor into the current PID's payload, where a
        /// data byte that happens to equal a PID number decodes into a reading the ECU never sent.
        /// <paramref name="skipWidths"/> keeps that outcome rare: it carries widths for standard PIDs
        /// this decoder does not interpret, so those are stepped over instead of ending the frame.
        /// </remarks>
        private static void DecodePidSequence(
            byte[] data,
            List<LiveDataReading> results,
            FrozenDictionary<byte, ObdPidDecoders.PidDecoder> decoders,
            FrozenDictionary<byte, byte>? skipWidths,
            string modeName)
        {
            int idx = 5;
            while (idx < data.Length)
            {
                byte pid = data[idx];

                if (decoders.TryGetValue(pid, out ObdPidDecoders.PidDecoder? decoder))
                {
                    // A truncated payload ends the frame: the bytes for the PIDs behind it are not
                    // there either, so there is nothing further to locate.
                    if (data.Length < idx + 1 + decoder.Width)
                        return;

                    decoder.Decode(data.AsSpan(idx + 1, decoder.Width), results);
                    idx += 1 + decoder.Width;
                }
                else if (skipWidths is not null && skipWidths.TryGetValue(pid, out byte width))
                {
                    idx += 1 + width;
                }
                else
                {
                    Debug.WriteLine($"Unknown OBD {modeName}: {pid:X2} - stopping frame decode");
                    return;
                }
            }
        }

        /// <summary>
        /// Decodes a Mode 22 response. Its PID is two bytes and one response carries one PID, so
        /// unlike Mode 01 there is no cursor to walk.
        /// </summary>
        private static void DecodeMode22(byte[] data, List<LiveDataReading> results)
        {
            const int idx = 5;
            if (data.Length < idx + 2)
                return;

            ushort pid = (ushort)((data[idx] << 8) | data[idx + 1]);
            if (!ObdPidDecoders.Mode22.TryGetValue(pid, out ObdPidDecoders.PidDecoder? decoder))
            {
                Debug.WriteLine($"Unknown OBD-II mode 22 PID: {pid:X4}");
                return;
            }

            if (data.Length >= idx + 2 + decoder.Width)
                decoder.Decode(data.AsSpan(idx + 2, decoder.Width), results);
        }
    }
}
