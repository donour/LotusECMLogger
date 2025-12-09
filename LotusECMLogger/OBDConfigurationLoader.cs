using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LotusECMLogger
{
    /// <summary>
    /// Minimal converter for byte arrays as readable integer arrays [0, 0, 7, 224]
    /// </summary>
    public class IntArrayByteConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<byte> bytes = [];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                if (reader.TokenType == JsonTokenType.Number) bytes.Add((byte)reader.GetInt32());
            return bytes.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (byte b in value) writer.WriteNumberValue(b);
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// JSON structure for configuration file
    /// </summary>
    public class OBDConfigurationJson
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        [JsonConverter(typeof(IntArrayByteConverter))]
        public byte[] EcmHeader { get; set; } = Array.Empty<byte>();
        
        public List<OBDRequestJson> Requests { get; set; } = new();
    }

    /// <summary>
    /// JSON structure for OBD request
    /// </summary>
    public class OBDRequestJson
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        
        // Mode01 specific
        [JsonConverter(typeof(IntArrayByteConverter))]
        public byte[]? Pids { get; set; }
        
        // Mode22 specific
        public byte? PidHigh { get; set; }
        public byte? PidLow { get; set; }
    }

    /// <summary>
    /// Loads OBD configurations from JSON files
    /// </summary>
    public static class OBDConfigurationLoader
    {
        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        /// <param name="filePath">Path to JSON configuration file</param>
        /// <returns>Loaded OBD configuration</returns>
        /// <exception cref="FileNotFoundException">Configuration file not found</exception>
        /// <exception cref="JsonException">Invalid JSON format</exception>
        /// <exception cref="InvalidOperationException">Invalid configuration data</exception>
        public static OBDConfiguration LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Configuration file not found: {filePath}");

            try
            {
                var jsonText = File.ReadAllText(filePath);
                var jsonConfig = JsonSerializer.Deserialize<OBDConfigurationJson>(jsonText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });

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
        /// Load configuration from embedded resource or config directory
        /// </summary>
        /// <param name="configName">Configuration name (e.g., "lotus-default")</param>
        /// <returns>Loaded OBD configuration</returns>
        public static OBDConfiguration LoadByName(string configName)
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
        /// Get list of available configuration files
        /// </summary>
        /// <returns>List of available configuration names</returns>
        public static List<string> GetAvailableConfigurations()
        {
            var configs = new List<string>();
            
            var configDir = "config";
            if (Directory.Exists(configDir))
            {
                var files = Directory.GetFiles(configDir, "*.json");
                configs.AddRange(files.Select(f => Path.GetFileNameWithoutExtension(f)));
            }

            return configs;
        }

        /// <summary>
        /// Convert JSON structure to OBDConfiguration
        /// </summary>
        private static OBDConfiguration ConvertFromJson(OBDConfigurationJson jsonConfig)
        {
            var config = new OBDConfiguration();
            
            // Set ECM header if provided
            if (jsonConfig.EcmHeader.Length > 0)
            {
                config.SetECMHeader(jsonConfig.EcmHeader);
            }

            // Convert requests
            foreach (var jsonRequest in jsonConfig.Requests)
            {
                IOBDRequest request = jsonRequest.Type.ToLower() switch
                {
                    "mode01" => CreateMode01Request(jsonRequest),
                    "mode22" => CreateMode22Request(jsonRequest),
                    _ => throw new InvalidOperationException($"Unknown request type: {jsonRequest.Type}")
                };

                config.Requests.Add(request);
            }

            return config;
        }

        /// <summary>
        /// Create Mode01 request from JSON
        /// </summary>
        private static Mode01Request CreateMode01Request(OBDRequestJson jsonRequest)
        {
            if (jsonRequest.Pids == null || jsonRequest.Pids.Length == 0)
                throw new InvalidOperationException($"Mode01 request '{jsonRequest.Name}' must have PIDs");

            return new Mode01Request(jsonRequest.Name, jsonRequest.Pids);
        }

        /// <summary>
        /// Create Mode22 request from JSON
        /// </summary>
        private static Mode22Request CreateMode22Request(OBDRequestJson jsonRequest)
        {
            if (!jsonRequest.PidHigh.HasValue || !jsonRequest.PidLow.HasValue)
                throw new InvalidOperationException($"Mode22 request '{jsonRequest.Name}' must have PidHigh and PidLow");

            return new Mode22Request(jsonRequest.Name, jsonRequest.PidHigh.Value, jsonRequest.PidLow.Value);
        }

        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        /// <param name="config">Configuration to save</param>
        /// <param name="filePath">Output file path</param>
        /// <param name="name">Configuration name</param>
        /// <param name="description">Configuration description</param>
        public static void SaveToFile(OBDConfiguration config, string filePath, string name = "", string description = "")
        {
            var jsonConfig = new OBDConfigurationJson
            {
                Name = string.IsNullOrEmpty(name) ? "Custom Configuration" : name,
                Description = string.IsNullOrEmpty(description) ? "User-defined configuration" : description,
                EcmHeader = config.ECMHeader,
                Requests = []
            };

            foreach (var request in config.Requests)
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

                jsonConfig.Requests.Add(jsonRequest);
            }

            JsonSerializerOptions jsonSerializerOptions = new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonSerializerOptions options = jsonSerializerOptions;

            var jsonText = JsonSerializer.Serialize(jsonConfig, options);
            File.WriteAllText(filePath, jsonText);
        }
    }
} 