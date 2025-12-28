using System.Text.Json;
using System.Text.Json.Serialization;

namespace LotusECMLogger
{
    /// <summary>
    /// JSON structure for multi-ECU configuration file
    /// </summary>
    public class MultiECUConfigurationJson
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// List of ECU groups (new multi-ECU format)
        /// </summary>
        public List<ECUGroupJson>? Ecus { get; set; }

        /// <summary>
        /// Legacy single-ECU header (for backward compatibility)
        /// </summary>
        [JsonConverter(typeof(IntArrayByteConverter))]
        public byte[]? EcmHeader { get; set; }

        /// <summary>
        /// Legacy single-ECU requests (for backward compatibility)
        /// </summary>
        public List<OBDRequestJson>? Requests { get; set; }

        /// <summary>
        /// Determine if this is a multi-ECU configuration
        /// </summary>
        [JsonIgnore]
        public bool IsMultiECU => Ecus != null && Ecus.Count > 0;
    }

    /// <summary>
    /// JSON structure for an ECU group
    /// </summary>
    public class ECUGroupJson
    {
        public string Name { get; set; } = "ECU";
        public uint RequestId { get; set; } = 0x7E0;
        public uint ResponseId { get; set; } = 0x7E8;
        public List<OBDRequestJson> Requests { get; set; } = [];
    }

    /// <summary>
    /// Loads multi-ECU configurations from JSON files with backward compatibility
    /// </summary>
    public static class MultiECUConfigurationLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Load configuration from JSON file (auto-detects legacy vs multi-ECU format)
        /// </summary>
        public static MultiECUConfiguration LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}");

            try
            {
                var jsonText = File.ReadAllText(filePath);
                var jsonConfig = JsonSerializer.Deserialize<MultiECUConfigurationJson>(jsonText, JsonOptions);

                if (jsonConfig == null)
                    throw new InvalidOperationException("Failed to deserialize configuration file");

                return ConvertFromJson(jsonConfig);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Invalid JSON in configuration file '{filePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load configuration by name from config directory
        /// </summary>
        public static MultiECUConfiguration LoadByName(string configName)
        {
            // Try config directory first
            var configFile = Path.Combine("config", $"{configName}.json");
            if (File.Exists(configFile))
            {
                return LoadFromFile(configFile);
            }

            // Try relative to executable
            var exeDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) ?? "";
            configFile = Path.Combine(exeDir, "config", $"{configName}.json");
            if (File.Exists(configFile))
            {
                return LoadFromFile(configFile);
            }

            throw new FileNotFoundException($"Configuration '{configName}' not found in config directory");
        }

        /// <summary>
        /// Convert JSON structure to MultiECUConfiguration
        /// </summary>
        private static MultiECUConfiguration ConvertFromJson(MultiECUConfigurationJson jsonConfig)
        {
            var config = new MultiECUConfiguration
            {
                Name = jsonConfig.Name,
                Description = jsonConfig.Description
            };

            if (jsonConfig.IsMultiECU)
            {
                // New multi-ECU format
                foreach (var ecuJson in jsonConfig.Ecus!)
                {
                    var ecuDef = new ECUDefinition
                    {
                        Name = ecuJson.Name,
                        RequestId = ecuJson.RequestId,
                        ResponseId = ecuJson.ResponseId
                    };

                    var group = new ECURequestGroup
                    {
                        ECU = ecuDef,
                        Requests = ConvertRequests(ecuJson.Requests)
                    };

                    config.ECUGroups.Add(group);
                }
            }
            else if (jsonConfig.Requests != null)
            {
                // Legacy single-ECU format - convert to multi-ECU
                var header = jsonConfig.EcmHeader ?? [0x00, 0x00, 0x07, 0xE0];
                uint requestId = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                uint responseId = requestId + 8;

                var ecuDef = new ECUDefinition
                {
                    Name = "ECM",
                    RequestId = requestId,
                    ResponseId = responseId
                };

                var group = new ECURequestGroup
                {
                    ECU = ecuDef,
                    Requests = ConvertRequests(jsonConfig.Requests)
                };

                config.ECUGroups.Add(group);
            }

            return config;
        }

        /// <summary>
        /// Convert JSON requests to IOBDRequest objects
        /// </summary>
        private static List<IOBDRequest> ConvertRequests(List<OBDRequestJson> jsonRequests)
        {
            var requests = new List<IOBDRequest>();

            foreach (var jsonRequest in jsonRequests)
            {
                IOBDRequest request = jsonRequest.Type.ToLower() switch
                {
                    "mode01" => CreateMode01Request(jsonRequest),
                    "mode22" => CreateMode22Request(jsonRequest),
                    _ => throw new InvalidOperationException($"Unknown request type: {jsonRequest.Type}")
                };

                requests.Add(request);
            }

            return requests;
        }

        private static Mode01Request CreateMode01Request(OBDRequestJson jsonRequest)
        {
            if (jsonRequest.Pids == null || jsonRequest.Pids.Length == 0)
                throw new InvalidOperationException($"Mode01 request '{jsonRequest.Name}' must have PIDs");

            return new Mode01Request(jsonRequest.Name, jsonRequest.Pids);
        }

        private static Mode22Request CreateMode22Request(OBDRequestJson jsonRequest)
        {
            if (!jsonRequest.PidHigh.HasValue || !jsonRequest.PidLow.HasValue)
                throw new InvalidOperationException($"Mode22 request '{jsonRequest.Name}' must have PidHigh and PidLow");

            return new Mode22Request(jsonRequest.Name, jsonRequest.PidHigh.Value, jsonRequest.PidLow.Value);
        }

        /// <summary>
        /// Save multi-ECU configuration to JSON file
        /// </summary>
        public static void SaveToFile(MultiECUConfiguration config, string filePath)
        {
            var jsonConfig = new MultiECUConfigurationJson
            {
                Name = config.Name,
                Description = config.Description,
                Ecus = []
            };

            foreach (var group in config.ECUGroups)
            {
                var ecuJson = new ECUGroupJson
                {
                    Name = group.ECU.Name,
                    RequestId = group.ECU.RequestId,
                    ResponseId = group.ECU.ResponseId,
                    Requests = []
                };

                foreach (var request in group.Requests)
                {
                    var jsonRequest = new OBDRequestJson
                    {
                        Name = request.Name,
                        Type = request switch
                        {
                            Mode01Request => "Mode01",
                            Mode22Request => "Mode22",
                            _ => "Unknown"
                        }
                    };

                    switch (request)
                    {
                        case Mode01Request mode01:
                            jsonRequest.Pids = mode01.PIDs;
                            break;
                        case Mode22Request mode22:
                            jsonRequest.PidHigh = mode22.PIDHigh;
                            jsonRequest.PidLow = mode22.PIDLow;
                            break;
                    }

                    ecuJson.Requests.Add(jsonRequest);
                }

                jsonConfig.Ecus.Add(ecuJson);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonText = JsonSerializer.Serialize(jsonConfig, options);
            File.WriteAllText(filePath, jsonText);
        }
    }
}
