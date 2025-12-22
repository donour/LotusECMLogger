using System.Text.Json.Serialization;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Represents a memory configuration preset for live tuning
    /// </summary>
    public class MemoryPreset
    {
        /// <summary>
        /// Display name of the preset
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Base memory address in hexadecimal (without 0x prefix)
        /// </summary>
        [JsonPropertyName("baseAddress")]
        public string BaseAddress { get; set; } = "40000000";

        /// <summary>
        /// Length in bytes (decimal)
        /// </summary>
        [JsonPropertyName("length")]
        public int Length { get; set; }

        /// <summary>
        /// Optional description of the memory region
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Returns the display text for the preset
        /// </summary>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Gets the full display text with address and length information
        /// </summary>
        public string GetDetailedDescription()
        {
            return $"{Name} - 0x{BaseAddress} ({Length} bytes)";
        }
    }

    /// <summary>
    /// Container for memory preset configuration file
    /// </summary>
    public class MemoryPresetsConfig
    {
        /// <summary>
        /// List of available memory presets
        /// </summary>
        [JsonPropertyName("presets")]
        public List<MemoryPreset> Presets { get; set; } = new();
    }
}
