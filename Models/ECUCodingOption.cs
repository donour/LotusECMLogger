using System;
using System.Collections.Generic;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Represents a single ECU coding option with its possible values and current setting
    /// </summary>
    public class ECUCodingOption
    {
        /// <summary>
        /// Name of the coding option
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Current value as a string
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Raw numeric value from the ECU
        /// </summary>
        public int RawValue { get; set; }

        /// <summary>
        /// Array of possible values this option can take
        /// </summary>
        public string[] PossibleValues { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Category of the option for grouping purposes
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Whether this option is a boolean (True/False) value
        /// </summary>
        public bool IsBoolean => PossibleValues.Length == 2 && 
                                PossibleValues[0].Equals("False", StringComparison.OrdinalIgnoreCase) && 
                                PossibleValues[1].Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this option is numeric (has no predefined values)
        /// </summary>
        public bool IsNumeric => PossibleValues.Length == 0;

        /// <summary>
        /// Creates a new ECUCodingOption instance
        /// </summary>
        public ECUCodingOption() { }

        /// <summary>
        /// Creates a new ECUCodingOption instance with specified values
        /// </summary>
        public ECUCodingOption(string name, string value, int rawValue, string[] possibleValues, string category)
        {
            Name = name;
            Value = value;
            RawValue = rawValue;
            PossibleValues = possibleValues;
            Category = category;
        }

        /// <summary>
        /// Determine the category for a coding option based on its name
        /// </summary>
        public static string DetermineCategory(string optionName)
        {
            return optionName.ToLowerInvariant() switch
            {
                var s when s.Contains("transmission") || s.Contains("gear") || s.Contains("clutch") => "Transmission",
                var s when s.Contains("oil") || s.Contains("cooling") || s.Contains("temperature") => "Engine",
                var s when s.Contains("abs") || s.Contains("brake") || s.Contains("esp") || s.Contains("traction") => "Braking",
                var s when s.Contains("air") || s.Contains("climate") || s.Contains("heat") || s.Contains("ventilation") => "Climate",
                var s when s.Contains("door") || s.Contains("mirror") || s.Contains("camera") => "Body",
                var s when s.Contains("instrument") || s.Contains("display") || s.Contains("cluster") => "Instruments",
                var s when s.Contains("sport") || s.Contains("race") || s.Contains("launch") => "Performance",
                var s when s.Contains("sensor") => "Sensors",
                _ => "Other"
            };
        }

        /// <summary>
        /// Convert from T6eCodingDecoder option
        /// </summary>
        public static ECUCodingOption FromDecoderOption(string name, string value, int rawValue, string[] possibleValues)
        {
            return new ECUCodingOption
            {
                Name = name,
                Value = value,
                RawValue = rawValue,
                PossibleValues = possibleValues ?? Array.Empty<string>(),
                Category = DetermineCategory(name)
            };
        }
    }
} 