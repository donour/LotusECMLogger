using System.Text.Json;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Loads and saves editable logging configurations while preserving JSON metadata.
    /// </summary>
    public static class LoggingConfigFileService
    {
        private const string ConfigSubDir = "config\\obdLogger";

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string GetPreferredConfigDirectory()
        {
            var workingDir = Path.GetFullPath(ConfigSubDir);
            if (Directory.Exists(workingDir))
            {
                return workingDir;
            }

            var executableDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigSubDir);
            if (Directory.Exists(executableDir))
            {
                return executableDir;
            }

            Directory.CreateDirectory(workingDir);
            return workingDir;
        }

        public static string? TryGetConfigPath(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                return null;
            }

            var candidatePaths = new[]
            {
                Path.Combine(GetPreferredConfigDirectory(), $"{configName}.json"),
                Path.Combine(Path.GetFullPath(ConfigSubDir), $"{configName}.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigSubDir, $"{configName}.json")
            };

            return candidatePaths
                .Select(Path.GetFullPath)
                .FirstOrDefault(File.Exists);
        }

        public static MultiECUConfigurationJson LoadEditableConfigFromName(string configName)
        {
            var filePath = TryGetConfigPath(configName);
            if (filePath == null)
            {
                throw new FileNotFoundException($"Configuration '{configName}' not found.");
            }

            return LoadEditableConfig(filePath);
        }

        public static MultiECUConfigurationJson LoadEditableConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            var jsonText = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<MultiECUConfigurationJson>(jsonText, ReadOptions);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration file.");
            }

            return NormalizeForEditor(config);
        }

        public static void SaveEditableConfig(MultiECUConfigurationJson config, string filePath)
        {
            var normalizedConfig = NormalizeBeforeSave(config);
            var jsonText = JsonSerializer.Serialize(normalizedConfig, WriteOptions);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, jsonText);
        }

        public static MultiECUConfigurationJson CreateDefaultConfig()
        {
            return new MultiECUConfigurationJson
            {
                Name = "New Logging Configuration",
                Description = "Custom logging profile",
                Ecus =
                [
                    new ECUGroupJson
                    {
                        Name = ECUDefinition.ECM.Name,
                        RequestId = ECUDefinition.ECM.RequestId,
                        ResponseId = ECUDefinition.ECM.ResponseId,
                        Requests = new List<OBDRequestJson>()
                    }
                ]
            };
        }

        private static MultiECUConfigurationJson NormalizeForEditor(MultiECUConfigurationJson config)
        {
            if (config.Ecus is { Count: > 0 })
            {
                return new MultiECUConfigurationJson
                {
                    Name = config.Name ?? string.Empty,
                    Description = config.Description ?? string.Empty,
                    Ecus = config.Ecus.Select(CloneEcuGroup).ToList()
                };
            }

            var header = config.EcmHeader is { Length: 4 }
                ? config.EcmHeader
                : ECUDefinition.ECM.GetRequestHeader();

            uint requestId = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
            uint responseId = requestId + 8;

            return new MultiECUConfigurationJson
            {
                Name = string.IsNullOrWhiteSpace(config.Name) ? "Logging Configuration" : config.Name,
                Description = config.Description,
                Ecus =
                [
                    new ECUGroupJson
                    {
                        Name = ECUDefinition.ECM.Name,
                        RequestId = requestId,
                        ResponseId = responseId,
                        Requests = (config.Requests ?? new List<OBDRequestJson>()).Select(CloneRequest).ToList()
                    }
                ]
            };
        }

        private static MultiECUConfigurationJson NormalizeBeforeSave(MultiECUConfigurationJson config)
        {
            var groups = (config.Ecus ?? new List<ECUGroupJson>())
                .Select(CloneEcuGroup)
                .ToList();

            if (groups.Count == 1)
            {
                var singleGroup = groups[0];
                return new MultiECUConfigurationJson
                {
                    Name = config.Name?.Trim() ?? string.Empty,
                    Description = config.Description?.Trim() ?? string.Empty,
                    EcmHeader = BuildHeader(singleGroup.RequestId),
                    Requests = singleGroup.Requests.Select(CloneRequest).ToList()
                };
            }

            return new MultiECUConfigurationJson
            {
                Name = config.Name?.Trim() ?? string.Empty,
                Description = config.Description?.Trim() ?? string.Empty,
                Ecus = groups
            };
        }

        private static ECUGroupJson CloneEcuGroup(ECUGroupJson group)
        {
            return new ECUGroupJson
            {
                Name = group.Name ?? string.Empty,
                RequestId = group.RequestId,
                ResponseId = group.ResponseId,
                Requests = group.Requests.Select(CloneRequest).ToList()
            };
        }

        private static OBDRequestJson CloneRequest(OBDRequestJson request)
        {
            return new OBDRequestJson
            {
                Type = request.Type ?? string.Empty,
                Name = request.Name ?? string.Empty,
                Description = request.Description ?? string.Empty,
                Category = request.Category ?? string.Empty,
                Unit = request.Unit ?? string.Empty,
                Pids = request.Pids?.ToArray(),
                PidHigh = request.PidHigh,
                PidLow = request.PidLow
            };
        }

        private static byte[] BuildHeader(uint requestId)
        {
            return
            [
                (byte)((requestId >> 24) & 0xFF),
                (byte)((requestId >> 16) & 0xFF),
                (byte)((requestId >> 8) & 0xFF),
                (byte)(requestId & 0xFF)
            ];
        }
    }
}
