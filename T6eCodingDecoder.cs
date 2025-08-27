using System;
using System.Collections.Generic;
using System.Linq;

namespace LotusECMLogger
{
    /// <summary>
    /// Decodes T6e ECU coding data from two 4-byte arrays (total 8 bytes, 64 bits)
    /// Provides structured access to vehicle configuration options
    /// </summary>
    public class T6eCodingDecoder
    {
        // TODO are these individual fields needed now?
        private readonly byte[] _codingDataHigh;
        private readonly byte[] _codingDataLow;
        private readonly ulong _bitField;

        // Constants for boolean options
        private static readonly string[] FALSE_TRUE = { "False", "True" };

        /// <summary>
        /// Coding option definition structure
        /// </summary>
        private class CodingOption(int bitPosition, int bitMask, string name, string[] options)
        {
            public int BitPosition { get; set; } = bitPosition;
            public int BitMask { get; set; } = bitMask;
            public string Name { get; set; } = name;
            public string[] Options { get; set; } = options;
        }

        /// <summary>
        /// All coding options defined for the T6e ECU
        /// </summary>
        private static readonly CodingOption[] _codingOptions =
        [
            new CodingOption(63, 1, "Oil Cooling System", ["Standard", "Additional"]),
            new CodingOption(60, 3, "Heating Ventilation Air Conditioning", ["None", "Heater Only", "Air Conditioning", "Climate Control"]),
            new CodingOption(57, 7, "Cruise System", ["None", "Basic", "Adaptive"]),
            new CodingOption(54, 7, "ESP Variant Unknown", [.. Enumerable.Range(0, 7).Select(n => $"Variant{n}")]),
            new CodingOption(52, 1, "Wheel Profile", ["18/19 inch", "19/20 inch"]),
            new CodingOption(49, 7, "Number of Gears", ["1","2","3","4","5","6","7","8"]),
            new CodingOption(48, 1, "Close Ratio Gearset", FALSE_TRUE),
            new CodingOption(45, 7, "Transmission Type", ["Manual", "Auto", "MMT"]),
            new CodingOption(43, 1, "Speed Units", ["MPH", "KPH"]),
            new CodingOption(36, 127, "Fuel Tank Capacity", [.. Enumerable.Range(0, 127).Select(n => n.ToString())]),
            new CodingOption(35, 1, "Rear Fog Fitted", FALSE_TRUE),
            new CodingOption(34, 1, "Japan Seatbelt Warning", FALSE_TRUE),
            new CodingOption(33, 1, "Symbol Display", ["ECE(ROW)", "SAE(FED)"]),
            new CodingOption(32, 1, "Driver Position", ["LHD", "RHD"]),
            new CodingOption(30, 1, "Exhaust Bypass Valve Override", FALSE_TRUE),
            new CodingOption(29, 1, "DPM Switch", FALSE_TRUE),
            new CodingOption(28, 1, "Seat Heaters", FALSE_TRUE),
            new CodingOption(27, 1, "Exhaust Silencer Bypass Valve", FALSE_TRUE),
            new CodingOption(26, 1, "Auxiliary Cooling Fan", FALSE_TRUE),
            new CodingOption(25, 1, "Speed Alert Buzzer", FALSE_TRUE),
            new CodingOption(24, 1, "TC/ESP Button", FALSE_TRUE),
            new CodingOption(23, 1, "Sport Button", FALSE_TRUE),
            new CodingOption(21, 3, "Clutch Input", ["None", "Switch", "Potentiometer"]),
            new CodingOption(15, 1, "Body Control Module", FALSE_TRUE),
            new CodingOption(14, 1, "Transmission Control Unit", FALSE_TRUE),
            new CodingOption(13, 1, "Tyre Pressure Monitoring System", FALSE_TRUE),
            new CodingOption(12, 1, "Steering Angle Sensor", FALSE_TRUE),
            new CodingOption(11, 1, "Yaw Rate Sensor", FALSE_TRUE),
            new CodingOption(10, 1, "Instrument Cluster", ["MY08", "MY11/12"]),
            new CodingOption(9, 1, "Anti-Lock Braking System", FALSE_TRUE),
            new CodingOption(8, 1, "Launch Mode", FALSE_TRUE),
            new CodingOption(7, 1, "Race Mode", FALSE_TRUE),
            new CodingOption(6, 1, "Speed Limiter", FALSE_TRUE),
            new CodingOption(5, 1, "Reverse Camera", FALSE_TRUE),
            new CodingOption(4, 1, "Powerfold Mirrors", FALSE_TRUE),
            new CodingOption(2, 1, "TPS Config Unknown", FALSE_TRUE),
            new CodingOption(1, 1, "Central Door Locking", FALSE_TRUE),
            new CodingOption(0, 1, "Oil Sump System", ["Standard", "Upgrade"])
        ];

        public T6eCodingDecoder(ulong bitfield)
        {
            // TODO this is duplicated in the other constructor, refactor into a method
            //bitfield = bitfield & 0xfcffffff;
            //bitfield = bitfield | 0x00c00000;

            _bitField = bitfield;
            _codingDataLow = new byte[4];
            _codingDataHigh = new byte[4];
            // Split the 64-bit value into two 4-byte arrays
            for (int i = 0; i < 4; i++)
            {
                _codingDataLow[i] = (byte)(_bitField >> (i * 8));
                _codingDataHigh[i] = (byte)(_bitField >> ((i + 4) * 8));
            }
        }

        /// <summary>
        /// Initialize the decoder with two 4-byte arrays of coding data
        /// </summary>
        /// <param name="codingDataLow">Lower 4 bytes of coding data</param>
        /// <param name="codingDataHigh">Higher 4 bytes of coding data</param>
        /// <exception cref="ArgumentException">Thrown if either array is not exactly 4 bytes</exception>
        public T6eCodingDecoder(byte[] codingDataLow, byte[] codingDataHigh)
        {
            if (codingDataLow == null || codingDataLow.Length != 4)
            {
                throw new ArgumentException("Lower coding data must be exactly 4 bytes", nameof(codingDataLow));
            }

            if (codingDataHigh == null || codingDataHigh.Length != 4)
            {
                throw new ArgumentException("Higher coding data must be exactly 4 bytes", nameof(codingDataHigh));
            }
            //codingDataHigh[0] = (byte)(codingDataHigh[0] & 0xFC);
            //codingDataHigh[1] = (byte)(CodingDataHigh[1] | 0xC0);
            //_codingDataLow = (byte[])codingDataLow.Clone();
            //_codingDataHigh = (byte[])codingDataHigh.Clone();
            
            // Convert 8 bytes to 64-bit value for easier bit manipulation
            // High bytes (bits 32-63)
            ulong highBits = ((ulong)codingDataHigh[3] << 24) | 
                           ((ulong)codingDataHigh[2] << 16) | 
                           ((ulong)codingDataHigh[1] << 8) | 
                           codingDataHigh[0];
            
            // Low bytes (bits 0-31)
            ulong lowBits = ((ulong)codingDataLow[3] << 24) | 
                          ((ulong)codingDataLow[2] << 16) | 
                          ((ulong)codingDataLow[1] << 8) | 
                          codingDataLow[0];
            
            // Combine into 64-bit field
            _bitField = (highBits << 32) | lowBits;

            // TODO, add other validation rules from S2, Exige, and Emira
            S1EvoraValidateCoding(_bitField);

        }

        /// <summary>
        /// Get the lower 4 bytes of coding data
        /// </summary>
        public byte[] CodingDataLow => (byte[])_codingDataLow.Clone();

        /// <summary>
        /// Get the higher 4 bytes of coding data
        /// </summary>
        public byte[] CodingDataHigh => (byte[])_codingDataHigh.Clone();

        /// <summary>
        /// Get the lower 4 bytes of coding data for writing to ECU
        /// </summary>
        public byte[] GetLowBytes() => (byte[])_codingDataLow.Clone();

        /// <summary>
        /// Get the higher 4 bytes of coding data for writing to ECU
        /// </summary>
        public byte[] GetHighBytes() => (byte[])_codingDataHigh.Clone();

        /// <summary>
        /// Get the complete 8-byte coding data array
        /// </summary>
        public byte[] CodingData
        {
            get
            {
                var result = new byte[8];
                Array.Copy(_codingDataLow, 0, result, 0, 4);
                Array.Copy(_codingDataHigh, 0, result, 4, 4);
                return result;
            }
        }

        /// <summary>
        /// Get the raw bit field value
        /// </summary>
        public ulong BitField => _bitField;

        /// <summary>
        /// Extract a value from the bit field at the specified position with the given mask
        /// </summary>
        /// <param name="bitPosition">Bit position (0-63)</param>
        /// <param name="bitMask">Bit mask for the field</param>
        /// <returns>Extracted value</returns>
        private int ExtractValue(int bitPosition, int bitMask)
        {
            return (int)((_bitField >> bitPosition) & (ulong)bitMask);
        }

        /// <summary>
        /// Validates that the coding data values are legal
        /// </summary>
        /// <param name="codingDataLow">Lower 4 bytes of coding data</param>
        /// <param name="codingDataHigh">Higher 4 bytes of coding data</param>
        /// <exception cref="ArgumentException">Thrown if coding data contains invalid values</exception>
        private static void S1EvoraValidateCoding(ulong bitfield)
        {
            if (bitfield == 0) {
                return;
            }

            // Convert byte arrays to 32-bit values for bit operations (little endian)
            uint codingHigh = (uint)(bitfield >> 32);
            uint codingLow = (uint)(bitfield & 0xFFFFFFFF);

            // Condition 1: (COD_base[0] >> 0x1c & 7) < 3
            if ((codingHigh >> 0x1c & 7) >= 3)
            {
                throw new ArgumentException("Invalid coding data: bits 28-30 of codingDataHigh must be < 3");
            }

            // Condition 2: (COD_base[0] >> 0x19 & 7) < 2
            if ((codingHigh >> 0x19 & 7) >= 2)
            {
                throw new ArgumentException("Invalid coding data: bits 25-27 of codingDataHigh must be < 2");
            }

            // Condition 3: ((COD_base[0] >> 0x16 & 7) == 3 || (COD_base[0] >> 0x16 & 7) == 1)
            uint bits22to24 = codingHigh >> 0x16 & 7;
            if (bits22to24 != 3 && bits22to24 != 1)
            {
                throw new ArgumentException($"Invalid coding data: ({bits22to24}). Bits 22-24 of codingDataHigh must be 1 or 3");
            }

            // Condition 4: ((COD_base[0] >> 0xd & 7) != 1 || (COD_base[0] >> 0x16 & 7) == 3)
            uint bits13to15 = codingHigh >> 0xd & 7;
            if (bits13to15 == 1 && bits22to24 != 3)
            {
                throw new ArgumentException($"Invalid coding data({bits13to15}): if bits 13-15 of codingDataHigh are 1, then bits 22-24 must be 3");
            }

            // Condition 5: ((COD_base[0] >> 0xd & 7) != 0 || (COD_base[1] >> 0x15 & 3) == 2)
            if (bits13to15 == 0 && (codingLow >> 0x15 & 3) != 2)
            {
                throw new ArgumentException("Invalid coding data: if bits 13-15 of codingDataHigh are 0, then bits 21-22 of codingDataLow must be 2");
            }

            // Condition 6: ((COD_base[1] >> 10 & 1) != 0 && (COD_base[1] >> 9 & 1) != 0)
            if ((codingLow >> 10 & 1) == 0 || (codingLow >> 9 & 1) == 0)
            {
                throw new ArgumentException("Invalid coding data: bits 9 and 10 of codingDataLow must both be set");
            }
        }

        /// <summary>
        /// Get the decoded value for a specific option
        /// </summary>
        /// <param name="optionName">Name of the option to retrieve</param>
        /// <returns>Decoded value as string</returns>
        public string GetOptionValue(string optionName)
        {
            var option = _codingOptions.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                throw new ArgumentException($"Unknown option: {optionName}", nameof(optionName));
            }

            int value = ExtractValue(option.BitPosition, option.BitMask);
            
            if (option.Options != null && value < option.Options.Length)
            {
                return option.Options[value];
            }
            
            return value.ToString();
        }

        /// <summary>
        /// Get the raw numeric value for a specific option
        /// </summary>
        /// <param name="optionName">Name of the option to retrieve</param>
        /// <returns>Raw numeric value</returns>
        public int GetOptionRawValue(string optionName)
        {
            var option = _codingOptions.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                throw new ArgumentException($"Unknown option: {optionName}", nameof(optionName));
            }

            return ExtractValue(option.BitPosition, option.BitMask);
        }

        /// <summary>
        /// Get all available option names
        /// </summary>
        /// <returns>Array of option names</returns>
        public string[] GetAvailableOptions()
        {
            return _codingOptions.Select(o => o.Name).ToArray();
        }

        /// <summary>
        /// Get the possible values for a specific option
        /// </summary>
        /// <param name="optionName">Name of the option</param>
        /// <returns>Array of possible values, or null if numeric</returns>
        public string[] GetOptionPossibleValues(string optionName)
        {
            var option = _codingOptions.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                throw new ArgumentException($"Unknown option: {optionName}", nameof(optionName));
            }

            return option.Options;
        }

        /// <summary>
        /// Check if an option is numeric (no fixed choices)
        /// </summary>
        /// <param name="optionName">Name of the option</param>
        /// <returns>True if the option is numeric</returns>
        public bool IsOptionNumeric(string optionName)
        {
            var option = _codingOptions.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                throw new ArgumentException($"Unknown option: {optionName}", nameof(optionName));
            }

            return option.Options == null;
        }

        /// <summary>
        /// Set the value for a specific option and return a new decoder with the updated value
        /// </summary>
        /// <param name="optionName">Name of the option to set</param>
        /// <param name="value">New value (string or numeric)</param>
        /// <returns>New T6eCodingDecoder with updated value</returns>
        public T6eCodingDecoder SetOptionValue(string optionName, object value)
        {
            var option = _codingOptions.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                throw new ArgumentException($"Unknown option: {optionName}", nameof(optionName));
            }

            int numericValue;
            
            if (option.Options != null)
            {
                // Option has predefined choices
                if (value is string stringValue)
                {
                    var index = Array.IndexOf(option.Options, stringValue);
                    if (index == -1)
                    {
                        throw new ArgumentException($"Invalid value '{stringValue}' for option '{optionName}'. Valid values: {string.Join(", ", option.Options)}", nameof(value));
                    }
                    numericValue = index;
                }
                else if (value is int intValue)
                {
                    if (intValue < 0 || intValue >= option.Options.Length)
                    {
                        throw new ArgumentException($"Invalid numeric value {intValue} for option '{optionName}'. Valid range: 0-{option.Options.Length - 1}", nameof(value));
                    }
                    numericValue = intValue;
                }
                else
                {
                    throw new ArgumentException($"Value must be string or int for option '{optionName}'", nameof(value));
                }
            }
            else
            {
                // Numeric option
                if (value is int intValue)
                {
                    numericValue = intValue;
                }
                else if (value is string stringValue && int.TryParse(stringValue, out int parsedValue))
                {
                    numericValue = parsedValue;
                }
                else
                {
                    throw new ArgumentException($"Value must be numeric for option '{optionName}'", nameof(value));
                }

                // Validate range
                if (numericValue < 0 || numericValue > option.BitMask)
                {
                    throw new ArgumentException($"Numeric value {numericValue} out of range for option '{optionName}'. Valid range: 0-{option.BitMask}", nameof(value));
                }
            }

            // Calculate new bit field
            ulong newBitField = _bitField;
            
            // Clear the bits for this option
            ulong clearMask = ~((ulong)option.BitMask << option.BitPosition);
            newBitField &= clearMask;
            
            // Set the new value
            ulong setValue = ((ulong)numericValue & (ulong)option.BitMask) << option.BitPosition;
            newBitField |= setValue;

            return new T6eCodingDecoder(newBitField);
        }

        /// <summary>
        /// Get all decoded options as a dictionary
        /// </summary>
        /// <returns>Dictionary of option names to decoded values</returns>
        public Dictionary<string, string> GetAllOptions()
        {
            var result = new Dictionary<string, string>();
            foreach (var option in _codingOptions)
            {
                result[option.Name] = GetOptionValue(option.Name);
            }
            return result;
        }

        /// <summary>
        /// Get all raw numeric values as a dictionary
        /// </summary>
        /// <returns>Dictionary of option names to raw numeric values</returns>
        public Dictionary<string, int> GetAllRawValues()
        {
            var result = new Dictionary<string, int>();
            foreach (var option in _codingOptions)
            {
                result[option.Name] = GetOptionRawValue(option.Name);
            }
            return result;
        }

        // Individual property accessors for convenience
        public string OilCoolingSystem => GetOptionValue("Oil Cooling System");
        public string HeatingVentilationAirConditioning => GetOptionValue("Heating Ventilation Air Conditioning");
        public string CruiseSystem => GetOptionValue("Cruise System");
        public string WheelProfile => GetOptionValue("Wheel Profile");
        public int NumberOfGears => GetOptionRawValue("Number of Gears");
        public string CloseRatioGearset => GetOptionValue("Close Ratio Gearset");
        public string TransmissionType => GetOptionValue("Transmission Type");
        public string SpeedUnits => GetOptionValue("Speed Units");
        public int FuelTankCapacity => GetOptionRawValue("Fuel Tank Capacity");
        public string RearFogFitted => GetOptionValue("Rear Fog Fitted");
        public string JapanSeatbeltWarning => GetOptionValue("Japan Seatbelt Warning");
        public string SymbolDisplay => GetOptionValue("Symbol Display");
        public string DriverPosition => GetOptionValue("Driver Position");
        public string ExhaustBypassValveOverride => GetOptionValue("Exhaust Bypass Valve Override");
        public string DpmSwitch => GetOptionValue("DPM Switch");
        public string SeatHeaters => GetOptionValue("Seat Heaters");
        public string ExhaustSilencerBypassValve => GetOptionValue("Exhaust Silencer Bypass Valve");
        public string AuxiliaryCoolingFan => GetOptionValue("Auxiliary Cooling Fan");
        public string SpeedAlertBuzzer => GetOptionValue("Speed Alert Buzzer");
        public string TcEspButton => GetOptionValue("TC/ESP Button");
        public string SportButton => GetOptionValue("Sport Button");
        public string ClutchInput => GetOptionValue("Clutch Input");
        public string BodyControlModule => GetOptionValue("Body Control Module");
        public string TransmissionControlUnit => GetOptionValue("Transmission Control Unit");
        public string TyrePressureMonitoringSystem => GetOptionValue("Tyre Pressure Monitoring System");
        public string SteeringAngleSensor => GetOptionValue("Steering Angle Sensor");
        public string YawRateSensor => GetOptionValue("Yaw Rate Sensor");
        public string InstrumentCluster => GetOptionValue("Instrument Cluster");
        public string AntiLockBrakingSystem => GetOptionValue("Anti-Lock Braking System");
        public string LaunchMode => GetOptionValue("Launch Mode");
        public string RaceMode => GetOptionValue("Race Mode");
        public string SpeedLimiter => GetOptionValue("Speed Limiter");
        public string ReverseCamera => GetOptionValue("Reverse Camera");
        public string PowerfoldMirrors => GetOptionValue("Powerfold Mirrors");
        public string CentralDoorLocking => GetOptionValue("Central Door Locking");
        public string OilSumpSystem => GetOptionValue("Oil Sump System");

        /// <summary>
        /// Convert the decoded data to a formatted string representation
        /// </summary>
        /// <returns>Formatted string with all options</returns>
        public override string ToString()
        {
            var options = GetAllOptions();
            var lines = options.Select(kvp => $"{kvp.Key}: {kvp.Value}");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Get a summary of the coding data in hexadecimal format
        /// </summary>
        /// <returns>Hexadecimal representation of the coding data</returns>
        public string ToHexString()
        {
            return BitConverter.ToString(CodingData).Replace("-", " ");
        }
    }
} 