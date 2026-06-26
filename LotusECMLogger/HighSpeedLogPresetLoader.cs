using System.Globalization;
using System.Text.Json;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger
{
    /// <summary>JSON shape of a high-speed-logger preset file.</summary>
    public sealed class HighSpeedLogPresetJson
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>ECU/firmware label; also names the symbol catalog used to resolve <c>symbol</c> refs.</summary>
        public string EcuVersion { get; set; } = string.Empty;

        public List<HighSpeedChannelJson> Channels { get; set; } = [];
    }

    /// <summary>
    /// JSON shape of a channel entry. A channel is either a <see cref="Symbol"/> reference (address/size/
    /// scale/unit resolved from the catalog, any field below overriding the derived value) or an explicit
    /// <see cref="Address"/> channel. <see cref="Address"/> accepts "0x…" hex or decimal.
    /// </summary>
    public sealed class HighSpeedChannelJson
    {
        public string? Symbol { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public byte? Size { get; set; }
        public bool? Signed { get; set; }
        public double? Scale { get; set; }
        public double? Offset { get; set; }
        public string? Unit { get; set; }

        /// <summary>Shorthand: sets the channel's rate (Hz) and marks it selected.</summary>
        public int? Rate { get; set; }
        public int? DefaultRate { get; set; }
        public bool? DefaultSelected { get; set; }
    }

    /// <summary>A loaded, resolved preset of channels for the high-speed logger.</summary>
    public sealed class HighSpeedLogPreset
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string EcuVersion { get; init; } = string.Empty;
        public List<HighSpeedChannel> Channels { get; init; } = [];

        /// <summary>Non-fatal issues encountered while resolving the preset (e.g. unknown symbols).</summary>
        public List<string> Warnings { get; init; } = [];
    }

    /// <summary>
    /// Discovers and loads high-speed-logger presets from <c>config\highSpeedLogger\*.json</c>.
    /// Symbol-referenced channels are resolved against the ECU's <see cref="SymbolCatalog"/>.
    /// </summary>
    public static class HighSpeedLogPresetLoader
    {
        private const string ConfigSubDir = "config\\highSpeedLogger";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        /// <summary>Names (file stems) of available presets, or an empty list if none are found.</summary>
        public static List<string> GetAvailablePresets()
        {
            var dir = ResolveConfigDir();
            if (dir == null)
                return [];

            return Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>Loads a preset by file stem. Throws <see cref="FileNotFoundException"/> if missing.</summary>
        public static HighSpeedLogPreset LoadByName(string presetName)
        {
            var dir = ResolveConfigDir();
            if (dir != null)
            {
                var path = Path.Combine(dir, $"{presetName}.json");
                if (File.Exists(path))
                    return LoadFromFile(path);
            }

            throw new FileNotFoundException($"High-speed log preset '{presetName}' not found in {ConfigSubDir}");
        }

        /// <summary>Loads and resolves a preset from an explicit file path.</summary>
        public static HighSpeedLogPreset LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Preset file not found: {filePath}");

            HighSpeedLogPresetJson json;
            try
            {
                json = JsonSerializer.Deserialize<HighSpeedLogPresetJson>(File.ReadAllText(filePath), JsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize preset file");
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Invalid JSON in preset file '{filePath}': {ex.Message}", ex);
            }

            string ecuVersion = json.EcuVersion;
            SymbolCatalog? catalog = string.IsNullOrWhiteSpace(ecuVersion) ? null : SymbolCatalogLoader.LoadByName(ecuVersion);
            var warnings = new List<string>();

            if (catalog == null && json.Channels.Any(c => !string.IsNullOrWhiteSpace(c.Symbol)))
                warnings.Add($"No symbol catalog for ECU '{ecuVersion}'; symbol-referenced channels were skipped.");

            var channels = json.Channels
                .Select(c => ConvertChannel(c, catalog, ecuVersion, warnings))
                .OfType<HighSpeedChannel>()
                .ToList();

            return new HighSpeedLogPreset
            {
                Name = string.IsNullOrWhiteSpace(json.Name) ? Path.GetFileNameWithoutExtension(filePath) : json.Name,
                Description = json.Description,
                EcuVersion = ecuVersion,
                Channels = channels,
                Warnings = warnings,
            };
        }

        private static HighSpeedChannel? ConvertChannel(HighSpeedChannelJson c, SymbolCatalog? catalog, string ecuVersion, List<string> warnings)
        {
            int rate = c.Rate ?? c.DefaultRate ?? 10;

            if (!string.IsNullOrWhiteSpace(c.Symbol))
            {
                if (catalog == null)
                    return null; // covered by the single catalog-missing warning

                var entry = catalog.Resolve(c.Symbol!);
                if (entry == null)
                {
                    warnings.Add($"Symbol '{c.Symbol}' not found in catalog '{ecuVersion}'.");
                    return null;
                }

                var t = entry.Type;
                return new HighSpeedChannel
                {
                    Name = c.Name ?? entry.Name,
                    Address = entry.Address,
                    Size = c.Size ?? (byte)(t.Size == 0 ? 1 : t.Size),
                    Signed = c.Signed ?? t.Signed,
                    Scale = c.Scale ?? t.Scale,
                    Offset = c.Offset ?? t.Offset,
                    Unit = c.Unit ?? t.Unit,
                    DefaultRate = rate,
                    DefaultSelected = c.DefaultSelected ?? true,
                    SourceSymbol = entry.Name,
                    Category = t.Category,
                };
            }

            if (string.IsNullOrWhiteSpace(c.Address))
            {
                warnings.Add($"Channel '{c.Name ?? "(unnamed)"}' has neither a symbol nor an address; skipped.");
                return null;
            }

            return new HighSpeedChannel
            {
                Name = c.Name ?? "Channel",
                Address = ParseAddress(c.Address!),
                Size = c.Size ?? 1,
                Signed = c.Signed ?? false,
                Scale = c.Scale ?? 1.0,
                Offset = c.Offset ?? 0.0,
                Unit = c.Unit ?? string.Empty,
                DefaultRate = rate,
                DefaultSelected = c.DefaultSelected ?? false,
            };
        }

        private static uint ParseAddress(string address)
        {
            var s = address.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static string? ResolveConfigDir()
        {
            if (Directory.Exists(ConfigSubDir))
                return ConfigSubDir;

            var exeConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigSubDir);
            return Directory.Exists(exeConfigDir) ? exeConfigDir : null;
        }
    }
}
