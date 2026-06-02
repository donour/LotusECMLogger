using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Maps a firmware/calibration version to the RAM address of its <c>coding_cmd</c> register.
    /// The ECU's 333 ms coding handler polls that one-byte register and, while unlocked, executes
    /// the requested command (e.g. 0x04 = erase model info). The address is firmware-specific, so
    /// the user selects the matching version before an RMA write is issued.
    /// </summary>
    public class CodingCommandTarget
    {
        /// <summary>Calibration version as reported by the ECU (CAL_prog_version), e.g. "C132E0278".</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>Friendly label shown in the selector, e.g. "C132E0278 (GT430)".</summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>RAM address of <c>coding_cmd</c> in hex, without "0x" prefix, e.g. "40002770".</summary>
        [JsonPropertyName("codingCmdAddress")]
        public string CodingCmdAddress { get; set; } = string.Empty;

        /// <summary>Parsed <see cref="CodingCmdAddress"/>.</summary>
        public uint CodingCmdAddressValue => Convert.ToUInt32(CodingCmdAddress, 16);

        public override string ToString() =>
            string.IsNullOrWhiteSpace(DisplayName) ? Version : DisplayName;
    }

    /// <summary>Container for the coding-command targets configuration file.</summary>
    public class CodingCommandTargetsConfig
    {
        [JsonPropertyName("targets")]
        public List<CodingCommandTarget> Targets { get; set; } = new();

        /// <summary>
        /// Loads the coding-command targets from <c>config/coding/codingCommands.json</c> next to
        /// the executable. Returns an empty list (never throws) when the file is missing or invalid.
        /// </summary>
        public static List<CodingCommandTarget> Load()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "coding", "codingCommands.json");
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"Coding-command config not found at {path}");
                    return new List<CodingCommandTarget>();
                }

                var config = JsonSerializer.Deserialize<CodingCommandTargetsConfig>(File.ReadAllText(path));
                return config?.Targets ?? new List<CodingCommandTarget>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load coding-command config: {ex.Message}");
                return new List<CodingCommandTarget>();
            }
        }
    }
}
