using System.Diagnostics;
using System.Collections.Generic;

namespace LotusECMLogger
{
    internal class LiveDataReading
    {
        public String name = "None";
        public double value_f;
        public override string ToString()
        {
            return $"<{name}: {value_f}>";
        }

        // This should be set up at app start from the config
        public static Dictionary<(byte, byte), Mode22DecodeRule> Mode22Decoders = new();
        public class Mode22DecodeRule
        {
            public string Name;
            public int Start;
            public int Length;
            public string Formula;
        }

        public static List<LiveDataReading> ParseCanResponse(byte[] data)
        {
            List<LiveDataReading> results = [];

            // determine if the response if from the ECU because is has id 0x7e8
            if (data.Length <= 4 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x07 || data[3] != 0xE8)
            {
                return results; // non ECU data
            }
            int obd_mode = data[4] - 0x40;
            int idx = 5;

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
                                    int timingAdvance = data[idx + 1] / 2 - 64; // convert to degrees BTDC
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
                            default:
                                idx++;
                                break;
                        }
                    }
                    break;
                case 0x22:
                    if (data.Length > idx + 1)
                    {
                        byte pidHigh = data[idx];
                        byte pidLow = data[idx + 1];
                        if (Mode22Decoders.TryGetValue((pidHigh, pidLow), out var rule))
                        {
                            // Extract bytes
                            if (data.Length > idx + rule.Start + rule.Length - 1)
                            {
                                byte[] bytes = new byte[rule.Length];
                                for (int i = 0; i < rule.Length; i++)
                                    bytes[i] = data[idx + rule.Start + i];
                                double value = EvaluateFormula(rule.Formula, bytes);
                                results.Add(new LiveDataReading { name = rule.Name, value_f = value });
                            }
                        }
                        else
                        {
                            // fallback: log or skip
                        }
                    }
                    break;
                default:
                    Debug.WriteLine($"Unknown OBD-II mode: {obd_mode:X2}");
                    break;
            }
            return results;
        }

        // Only supports formulas like (A << 8 | B) / 4.0 and (A << 8 | B)
        private static double EvaluateFormula(string formula, byte[] bytes)
        {
            if (formula.Contains("(A << 8 | B) / 4.0"))
            {
                int val = (bytes[0] << 8) | bytes[1];
                return val / 4.0;
            }
            if (formula.Contains("(A << 8 | B)"))
            {
                int val = (bytes[0] << 8) | bytes[1];
                return val;
            }
            // fallback
            return 0;
        }
    }
}
