using System;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Represents a single reading from the ECU with timestamp and optional unit
    /// </summary>
    public class LiveReading
    {
        /// <summary>
        /// Name of the parameter being measured
        /// </summary>
        public string ParameterName { get; set; } = string.Empty;

        /// <summary>
        /// The measured value
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Optional unit of measurement (e.g., "RPM", "°C", "%")
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// When the reading was taken
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Formatted value with unit if available
        /// </summary>
        public string FormattedValue => Unit != null ? $"{Value:F2} {Unit}" : $"{Value:F2}";

        /// <summary>
        /// Creates a new LiveReading instance
        /// </summary>
        public LiveReading()
        {
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Creates a new LiveReading instance with specified values
        /// </summary>
        public LiveReading(string parameterName, double value, string? unit = null)
        {
            ParameterName = parameterName;
            Value = value;
            Unit = unit;
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Convert from the old LiveDataReading format
        /// </summary>
        public static LiveReading FromLiveDataReading(LiveDataReading oldReading)
        {
            return new LiveReading
            {
                ParameterName = oldReading.name,
                Value = oldReading.value_f,
                Unit = DetermineUnit(oldReading.name),
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Determine the appropriate unit based on the parameter name
        /// </summary>
        private static string? DetermineUnit(string parameterName)
        {
            return parameterName.ToLowerInvariant() switch
            {
                var s when s.Contains("temperature") => "°C",
                var s when s.Contains("speed") => "km/h",
                var s when s.Contains("pressure") => "kPa",
                var s when s.Contains("position") || s.Contains("load") => "%",
                var s when s.Contains("rpm") || s.Contains("engine speed") => "RPM",
                var s when s.Contains("timing") || s.Contains("vvti") => "°",
                var s when s.Contains("torque") => "Nm",
                var s when s.Contains("ratio") => ":1",
                _ => null
            };
        }
    }
} 