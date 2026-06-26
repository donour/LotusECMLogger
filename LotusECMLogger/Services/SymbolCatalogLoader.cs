using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Discovers and loads per-ECU symbol catalogs from Ghidra CSV exports under
    /// <c>config\highSpeedLogger\symbols\*.csv</c>. Keeps only loggable RAM "Data Label" rows
    /// (0x40000000–0x4000FFFF), parses each row's Data Type via <see cref="ChannelTypeParser"/>,
    /// and infers the size of unknown-typed entries from the gap to the next symbol address.
    /// Parsed catalogs are cached per ECU. Follows the dual-path lookup of
    /// <see cref="LotusECMLogger.HighSpeedLogPresetLoader"/>.
    /// </summary>
    public static class SymbolCatalogLoader
    {
        private const string SymbolsSubDir = "config\\highSpeedLogger\\symbols";
        private const uint RamStart = 0x4000_0000;
        private const uint RamEnd = 0x4000_FFFF;

        private static readonly Dictionary<string, SymbolCatalog> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>ECU/catalog names available (file stems with any trailing "_symbols" removed).</summary>
        public static List<string> GetAvailableCatalogs()
        {
            var dir = ResolveSymbolsDir();
            if (dir == null)
                return [];

            return Directory.GetFiles(dir, "*.csv")
                .Select(CatalogNameFromPath)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>Loads (and caches) the catalog for an ECU name, or null if no matching CSV exists.</summary>
        public static SymbolCatalog? LoadByName(string ecuName)
        {
            if (string.IsNullOrWhiteSpace(ecuName))
                return null;

            lock (Cache)
            {
                if (Cache.TryGetValue(ecuName, out var cached))
                    return cached;
            }

            var dir = ResolveSymbolsDir();
            if (dir == null)
                return null;

            var path = Directory.GetFiles(dir, "*.csv")
                .FirstOrDefault(f => string.Equals(CatalogNameFromPath(f), ecuName, StringComparison.OrdinalIgnoreCase));
            if (path == null)
                return null;

            var catalog = LoadFromFile(path);
            lock (Cache)
            {
                Cache[ecuName] = catalog;
            }
            return catalog;
        }

        /// <summary>Parses a symbol catalog from an explicit CSV path.</summary>
        public static SymbolCatalog LoadFromFile(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);

            string? header = reader.ReadLine();
            if (header == null)
                return new SymbolCatalog(CatalogNameFromPath(filePath), []);

            var cols = ParseCsvLine(header);
            int nameIdx = IndexOf(cols, "Name");
            int locIdx = IndexOf(cols, "Location");
            int typeIdx = IndexOf(cols, "Type");
            int dataTypeIdx = IndexOf(cols, "Data Type");
            int nsIdx = IndexOf(cols, "Namespace");

            if (nameIdx < 0 || locIdx < 0 || typeIdx < 0)
                throw new InvalidDataException($"Symbol CSV '{filePath}' is missing required Name/Location/Type columns.");

            var entries = new List<SymbolEntry>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                    continue;

                var f = ParseCsvLine(line);
                if (f.Count <= typeIdx || !string.Equals(f[typeIdx], "Data Label", StringComparison.Ordinal))
                    continue;
                if (f.Count <= locIdx ||
                    !uint.TryParse(f[locIdx], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint addr))
                    continue;
                if (addr < RamStart || addr > RamEnd)
                    continue;

                string rawType = dataTypeIdx >= 0 && f.Count > dataTypeIdx ? f[dataTypeIdx] : string.Empty;
                entries.Add(new SymbolEntry
                {
                    Name = nameIdx < f.Count ? f[nameIdx] : string.Empty,
                    Address = addr,
                    RawType = rawType,
                    Type = ChannelTypeParser.Parse(rawType),
                    Namespace = nsIdx >= 0 && f.Count > nsIdx ? f[nsIdx] : string.Empty,
                });
            }

            InferUnknownSizes(entries);
            return new SymbolCatalog(CatalogNameFromPath(filePath), entries);
        }

        /// <summary>Fills in <c>Size == 0</c> entries from the gap to the next symbol address (clamped to 1/2/4).</summary>
        private static void InferUnknownSizes(List<SymbolEntry> entries)
        {
            var ordered = entries.OrderBy(e => e.Address).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Type.Size != 0)
                    continue;

                long gap = i + 1 < ordered.Count ? ordered[i + 1].Address - ordered[i].Address : 4;
                int size = gap >= 4 ? 4 : gap >= 2 ? 2 : 1;
                // SymbolEntry.Type is init-only; replace the entry's parsed type with the inferred size.
                int idx = entries.IndexOf(ordered[i]);
                entries[idx] = new SymbolEntry
                {
                    Name = ordered[i].Name,
                    Address = ordered[i].Address,
                    RawType = ordered[i].RawType,
                    Namespace = ordered[i].Namespace,
                    Type = ordered[i].Type with { Size = size, Confidence = TypeConfidence.Inferred },
                };
            }
        }

        private static string? ResolveSymbolsDir()
        {
            if (Directory.Exists(SymbolsSubDir))
                return SymbolsSubDir;
            var exeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SymbolsSubDir);
            return Directory.Exists(exeDir) ? exeDir : null;
        }

        private static string CatalogNameFromPath(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith("_symbols", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^"_symbols".Length];
            return stem;
        }

        private static int IndexOf(List<string> cols, string name)
        {
            for (int i = 0; i < cols.Count; i++)
                if (string.Equals(cols[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>Minimal RFC-4180-style CSV line parser (handles quoted fields and "" escapes).</summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }

            fields.Add(sb.ToString());
            return fields;
        }
    }
}
