using System.Diagnostics;

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
                            case 0x11: // throttle position
                                if (data.Length > idx + 1)
                                {
                                    // convert data[idx+1] to an unsigned 8-bit integer
                                    int throttlePosition = data[idx + 1];
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
                            default:
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
                            case 0x18: // octane rating 1
                            case 0x19: // octane rating 2
                            case 0x1A: // octane rating 3
                            case 0x1B: // octane rating 4
                            case 0x4D: // octane rating 5
                            case 0x4E: // octane rating 6
                                if (data.Length > idx + 2)
                                {
                                    int octaneRating = data[idx + 2];
                                    LiveDataReading reading = new()
                                    {
                                        name = $"Octane Rating {data[idx + 1]:X2}",
                                        value_f = octaneRating,
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
                                        name = "Accelerator Pedal Position",
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
                                        name = "Manifold Temperature",
                                        value_f = manifoldTemp,
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
            return results;
        }
    }
}
