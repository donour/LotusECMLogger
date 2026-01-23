using System.ComponentModel;
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


        private static void proccessO2Sensor(byte[] data, List<LiveDataReading> results, int idx, int sensorNum)
        {
            int A = data[idx + 1];
            int B = data[idx + 2];
            int C = data[idx + 3];
            int D = data[idx + 4];
            double lambda = (2.0 / 65536.0) * ((A << 8) | B);
            double voltage = (8.0 / 65536.0) * ((C << 8) | D);
            LiveDataReading lambdaReading = new()
            {
                name = "O2SensorLambda"+sensorNum,
                value_f = lambda,
            };
            results.Add(lambdaReading);
            LiveDataReading voltageReading = new()
            {
                name = "O2SensorVoltage"+sensorNum,
                value_f = voltage,
            };
            results.Add(voltageReading);
        }


        /// <summary>
        /// Parse standard OBD-II response (Mode 01, 09, 22)
        /// </summary>
        private static List<LiveDataReading> ParseStandardObdResponse(byte[] data, ECUDefinition? ecu, bool prefixWithEcuName)
        {
            List<LiveDataReading> results = [];
            string prefix = (prefixWithEcuName && ecu != null) ? $"{ecu.Name}:" : "";
            string? ecuSource = ecu?.Name;
            int obd_mode = data[4] - 0x40;
            int idx = 5;

            int cyl_num = 6;
            int bank_num = 2;

            switch (obd_mode)
            {
                case 0x01:
                    while (idx < data.Length)
                    {
                        switch (data[idx])
                        {
                            case 0x05: // coolant temperature
                                if (data.Length > idx + 1)
                                {
                                    int coolantTemp = data[idx + 1] - 40; // convert to Celsius
                                    LiveDataReading reading = new()
                                    {
                                        name = "Coolant Temperature",
                                        value_f = coolantTemp,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x06: // short term fuel trim bank 1
                                if (data.Length > idx + 1)
                                {
                                    float shortTermFuelTrimBank1 = data[idx + 1] / 1.28f - 100.0f; // convert to percentage
                                    LiveDataReading reading = new()
                                    {
                                        name = "Short Term Fuel Trim Bank 1",
                                        value_f = shortTermFuelTrimBank1,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x07: // long term fuel trim bank 1
                                if (data.Length > idx + 1)
                                {
                                    float longTermFuelTrimBank1 = data[idx + 1] / 1.28f - 100.0f; // convert to percentage
                                    LiveDataReading reading = new()
                                    {
                                        name = "Lont Term Fuel Trim Bank 1",
                                        value_f = longTermFuelTrimBank1,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x08: // short term fuel trim bank 2
                                if (data.Length > idx + 1)
                                {
                                    float shortTermFuelTrimBank2 = data[idx + 1] / 1.28f - 100.0f; // convert to percentage
                                    LiveDataReading reading = new()
                                    {
                                        name = "Short Term Fuel Trim Bank 2",
                                        value_f = shortTermFuelTrimBank2,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x09: // long term fuel trim bank 2
                                if (data.Length > idx + 1)
                                {
                                    float longTermFuelTrimBank2 = data[idx + 1] / 1.28f - 100.0f; // convert to percentage
                                    LiveDataReading reading = new()
                                    {
                                        name = "Lont Term Fuel Trim Bank 2",
                                        value_f = longTermFuelTrimBank2,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x0A:
                                if (data.Length > idx + 1)
                                {
                                    float fuel_pressure_bar = data[idx + 1] * 3f / 100f;
                                    LiveDataReading reading = new()
                                    {
                                        name = "FuellPressure(bar)",
                                        value_f = fuel_pressure_bar
                                    };
                                }
                                idx += 2;
                                break;
                            case 0x0B: // intake manifold absolute pressure
                                if (data.Length > idx + 1)
                                {
                                    int intakePressure = data[idx + 1]; // kPa
                                    LiveDataReading reading = new()
                                    {
                                        name = "Intake Manifold Pressure",
                                        value_f = intakePressure,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x0C: // engine speed
                                if (data.Length > idx + 2)
                                {
                                    int engineSpeed = (data[idx + 1] << 8) | data[idx + 2];
                                    LiveDataReading reading = new()
                                    {
                                        name = "Engine Speed",
                                        value_f = engineSpeed / 4,
                                    };
                                    results.Add(reading);
                                }
                                idx += 3;
                                break;
                            case 0x0D: // vehicle speed
                                if (data.Length > idx + 1)
                                {
                                    // convert data[idx+1] to an unsigned 8-bit integer
                                    int vehicleSpeed = data[idx + 1];
                                    LiveDataReading reading = new()
                                    {
                                        name = "Vehicle Speed",
                                        value_f = vehicleSpeed,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x0E: // timing advance
                                if (data.Length > idx + 1)
                                {
                                    // convert data[idx+1] to an unsigned 8-bit integer
                                    float timingAdvance = data[idx + 1] / 2.0f - 64.0f; // convert to degrees BTDC
                                    LiveDataReading reading = new()
                                    {
                                        name = "Timing Advance",
                                        value_f = timingAdvance,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x0F: // intake air temperature (IAT)
                                if (data.Length > idx + 1)
                                {
                                    int iat = data[idx + 1] - 40; // OBD-II: A - 40
                                    LiveDataReading reading = new()
                                    {
                                        name = "Intake Air Temperature",
                                        value_f = iat,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;
                            case 0x10: // MAF
                                if (data.Length > idx + 2)
                                {
                                    float maf_gps = ((data[idx + 1] << 8) + data[idx + 2]) / 100.0f;
                                    LiveDataReading reading = new()
                                    {
                                        name = "maf (g/s)",
                                        value_f = maf_gps,
                                    };
                                    results.Add(reading);
                                }
                                idx += 3;
                                break;                                 
                            case 0x11: // throttle position
                                if (data.Length > idx + 1)
                                {
                                    // TODO: 77 is the max value that i have seen but it might not be portable
                                    int throttlePosition = data[idx + 1] * 100/77;
                                    LiveDataReading reading = new()
                                    {
                                        name = "Throttle Position",
                                        value_f = throttlePosition * 100 / 255, // convert to percentage
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;

                            case 0x24:// oxygen sensor 1 (bank 1, sensor 1) ABCD => (AB lambda) and (CD voltage)
                                if (data.Length > idx + 4)
                                {
                                    proccessO2Sensor(data, results, idx,1);
                                }

                                idx += 5;
                                break;
                            case 0x25:
                                if (data.Length > idx + 4)
                                {
                                    proccessO2Sensor(data, results, idx, 2);
                                }

                                idx += 5;
                                break;
                            case 0x28:
                                if (data.Length > idx + 4)
                                {
                                    proccessO2Sensor(data, results, idx, 5);
                                }

                                idx += 5;
                                break;
                            case 0x29:
                                if (data.Length > idx + 4)
                                {
                                    proccessO2Sensor(data, results, idx, 9);
                                }

                                idx += 5;
                                break;

                            case 0x43: // absolute load value
                                if (data.Length > idx + 2)
                                {
                                    int absoluteLoad = ((data[idx + 1] << 8) | data[idx + 2]) * 100 / 255; // convert to percentage
                                    LiveDataReading reading = new()
                                    {
                                        name = "Absolute Load",
                                        value_f = absoluteLoad,
                                    };
                                    results.Add(reading);
                                }
                                idx += 3;
                                break;
                            case 0x44: // commanded equivalence ratio (air-fuel)
                                if (data.Length > idx + 2)
                                {
                                    int A = data[idx + 1];
                                    int B = data[idx + 2];
                                    double eqRatio = ((A << 8) | B) / 32768.0;
                                    LiveDataReading reading = new()
                                    {
                                        name = "Commanded Equivalence Ratio",
                                        value_f = eqRatio,
                                    };
                                    results.Add(reading);
                                }
                                idx += 3;
                                break;
                            case 0x46: // ambient air temperature
                                if (data.Length > idx + 1)
                                {
                                    int ambientTemp = data[idx + 1] - 40; // convert to Celsius
                                    LiveDataReading reading = new()
                                    {
                                        name = "Ambient Air Temperature",
                                        value_f = ambientTemp,
                                    };
                                    results.Add(reading);
                                }
                                idx += 2;
                                break;

                            default:
                                Debug.WriteLine($"Unknown OBD Mode01: {data[idx]:X2}");
                                idx++;
                                break;
                        }
                    }
                    break;
                case 0x09: // request vehicle info
                    while (idx < data.Length)
                    {
                        switch (data[idx])
                        {
                            case 0: //supported pids 01-20
                                if (data.Length > idx + 4)
                                {
                                    uint supportedPIDs = (uint)((data[idx + 1] << 24) | (data[idx + 2] << 16) | (data[idx + 3] << 8) | data[idx + 4]);
                                    LiveDataReading reading = new()
                                    {
                                        name = "SupportedPIDs_01_20",
                                        value_l = supportedPIDs,
                                    };
                                    results.Add(reading);
                                }
                                idx += 5;
                                break;
                            default:
                                Debug.WriteLine($"Unknown OBD Mode09: {data[idx]:X2}");
                                idx++;
                                break;
                        }
                    }
                    break;
                case 0x22:
                    if (data[idx] == 0x02)
                    {
                        switch (data[idx + 1])
                        {
                            case 2: // purge DC
                                if (data.Length > idx + 1)
                                {
                                    int purgeDC = data[idx + 1] * 100 / 255; 
                                    LiveDataReading reading = new()
                                    {
                                        name = "PurgeDutyCycle",
                                        value_f = purgeDC,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 5: // inj pulse time (b1)
                                bank_num = 1;
                                goto case 0x17;
                            case 0x17:
                                if (data.Length > idx + 4)
                                {
                                    int injPulseTimeB1 = (data[idx + 2] << 16) | (data[idx + 3] << 8) | data[idx + 4];
                                    LiveDataReading reading = new()
                                    {
                                        name = $"InjectorPulseTimeBank{bank_num}(us)",
                                        value_f = injPulseTimeB1,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x2A: // load
                                if (data.Length > idx + 2)
                                {
                                    float load = data[idx + 2];
                                    LiveDataReading reading = new()
                                    {
                                        name = "load_pct",
                                        value_f = load,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x13: // afr target
                                if (data.Length > idx + 2)
                                {
                                    int fuelRate = data[idx + 2];
                                    LiveDataReading reading = new()
                                    {
                                        name = "AFR Target",
                                        value_f = fuelRate * 0.01, // %
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x31: //knock spark retard
                                if (data.Length > idx + 5)
                                {
                                    double cyl1Retard = data[idx + 2] / 4.0;
                                    double cyl2Retard = data[idx + 3] / 4.0;
                                    double cyl3Retard = data[idx + 4] / 4.0;
                                    double cyl4Retard = data[idx + 5] / 4.0;
                                    LiveDataReading reading1 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 1",
                                        value_f = cyl1Retard
                                    };
                                    results.Add(reading1);
                                    LiveDataReading reading2 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 2",
                                        value_f = cyl2Retard
                                    };
                                    results.Add(reading2);
                                    LiveDataReading reading3 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 3",
                                        value_f = cyl3Retard
                                    };
                                    results.Add(reading3);
                                    LiveDataReading reading4 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 4",
                                        value_f = cyl4Retard
                                    };
                                    results.Add(reading4);
                                }
                                break;
                            case 0x56:
                                if (data.Length > idx + 3)
                                {
                                    double cyl5Retard = data[idx + 2] / 4.0;
                                    double cyl6Retard = data[idx + 3] / 4.0;
                                    LiveDataReading reading5 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 5",
                                        value_f = cyl5Retard
                                    };
                                    results.Add(reading5);
                                    LiveDataReading reading6 = new()
                                    {
                                        name = "KnockSparkRetardCylinder 6",
                                        value_f = cyl6Retard
                                    };
                                    results.Add(reading6);
                                }
                                break;

                            case 0x34:
                                cyl_num = 1;
                                goto case 0x58;
                            case 0x35:
                                cyl_num = 3;
                                goto case 0x58;
                            case 0x36:
                                cyl_num = 4;
                                goto case 0x58;
                            case 0x37: 
                                cyl_num = 2;
                                goto case 0x58;
                            case 0x57:
                                cyl_num = 5;
                                goto case 0x58;
                            case 0x58: 
                                if (data.Length > idx + 3)
                                {
                                    int misfire = (data[idx + 2] << 8) | data[idx + 3];
                                    LiveDataReading reading = new()
                                    {
                                        name = $"MisfireCylinder{cyl_num}",
                                        value_f = misfire,
                                    };
                                    results.Add(reading);
                                }                                    
                                break;

                            case 0x18:
                                cyl_num = 1;
                                goto case 0x4E;
                            case 0x19:
                                cyl_num = 3;
                                goto case 0x4E;
                            case 0x1A:
                                cyl_num = 4;
                                goto case 0x4E;
                            case 0x1B:
                                cyl_num = 2;
                                goto case 0x4E;
                            case 0x4D:
                                cyl_num = 5;
                                goto case 0x4E;
                            case 0x4E:
                                if (data.Length > idx + 3)
                                {
                                    int octaneRating = ((data[idx + 2] << 8) | data[idx+3]);
                                    LiveDataReading reading = new()
                                    {
                                        name = $"OctaneRatingCylinder{cyl_num}",
                                        value_f = octaneRating * 100.0/65536,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x3B: 
                                if (data.Length > idx + 3)
                                {
                                    int tps = (data[idx + 2] << 8) | data[idx + 3];
                                    LiveDataReading reading = new()
                                    {
                                        name = "TPSTarget",
                                        value_f = tps * 100.0 / 1024, // convert to percentage
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x45: //TPS
                                if (data.Length > idx + 3)
                                {
                                    int tps = (data[idx + 2] << 8) | data[idx + 3];
                                    LiveDataReading reading = new()
                                    {
                                        name = "TPSActual",
                                        value_f = tps * 100.0 / 1024, // convert to percentage
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x46: //accel pedal position
                                if (data.Length > idx + 3)
                                {
                                    int pedal = (data[idx + 2] << 8) | data[idx + 3];
                                    LiveDataReading reading = new()
                                    {
                                        name = "AcceleratorPedalPosition",
                                        value_f = pedal * 100.0 / 1024, // convert to percentage
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x72: // manifold temperature
                                if (data.Length > idx + 2)
                                {
                                    int manifoldTemp = data[idx + 2] * 5 / 8 - 40;
                                    LiveDataReading reading = new()
                                    {
                                        name = "ManifoldTempC",
                                        value_f = manifoldTemp,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x6A: // engine torque
                                if (data.Length > idx + 2)
                                {
                                    byte[] bytes = [data[idx + 3], data[idx + 2]];
                                    int torque = BitConverter.ToInt16(bytes, 0);
                                    LiveDataReading reading = new()
                                    {
                                        name = "TorqueNM",
                                        value_f = torque,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x08: // VVTi B1 intake position
                                if (data.Length > idx + 2)
                                {
                                    byte[] bytes = [data[idx + 3], data[idx + 2]];
                                    int vvti_b1i = BitConverter.ToInt16(bytes, 0);
                                    LiveDataReading reading = new()
                                    {
                                        name = "VVTI B1 intake (deg)",
                                        value_f = vvti_b1i / 4.0,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x4B: // VVTi B2 intake position
                                if (data.Length > idx + 2)
                                {
                                    byte[] bytes = [data[idx + 3], data[idx + 2]];
                                    int vvti_b2i = BitConverter.ToInt16(bytes, 0);
                                    LiveDataReading reading = new()
                                    {
                                        name = "VVTI B2 intake (deg)",
                                        value_f = vvti_b2i / 4.0,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x50: // VVTI B1 exhaust position
                                if (data.Length > idx + 2)
                                {
                                    byte[] bytes = [data[idx + 3], data[idx + 2]];
                                    int vvti_b1e = BitConverter.ToInt16(bytes, 0);
                                    LiveDataReading reading = new()
                                    {
                                        name = "VVTI B1 exhaust (deg)",
                                        value_f = vvti_b1e / 4.0,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0x51: // VVTI B2 exhaust position
                                if (data.Length > idx + 2)
                                {
                                    byte[] bytes = [data[idx + 3], data[idx + 2]];
                                    int vvti_b2e = BitConverter.ToInt16(bytes, 0);
                                    LiveDataReading reading = new()
                                    {
                                        name = "VVTI B2 exhaust (deg)",
                                        value_f = vvti_b2e / 4.0,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0xC7:
                                if (data.Length > idx + 1)
                                {
                                    int transTemp = data[idx + 2] * 5 / 8 - 40;
                                    LiveDataReading reading = new()
                                    {
                                        name = "TransFluidTempC",
                                        value_f = transTemp,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            case 0xC9:
                                if (data.Length > idx + 1)
                                {
                                    int dc = data[idx + 2] * 100/255;
                                    LiveDataReading reading = new()
                                    {
                                        name = "ChargecoolerDutycycle",
                                        value_f = dc,
                                    };
                                    results.Add(reading);
                                }
                                break;
                            default:
                                Debug.WriteLine($"Unknown OBD-II mode 22 submode: {data[idx + 1]:X2}");
                                break;
                        }
                    }
                    break;

                default:
                    Debug.WriteLine($"Unknown OBD-II mode: {obd_mode:X2}");
                    break;

            }

            // Apply prefix and ecuSource to all readings if in multi-ECU mode
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
    }
}
