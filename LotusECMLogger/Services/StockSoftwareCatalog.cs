using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Known factory (or CARB Executive Order) software: the CVNs each calibration ID ships with,
    /// read once from <c>config\stock_software.json</c> — a flat object mapping a calibration ID to
    /// an array of CVNs as hex strings, e.g. <c>{ "C132E0278": ["0x0000ABCD"] }</c>.
    /// <para>
    /// This stands in for the database California's inspection system checks Cal ID / CVN pairs
    /// against, which BAR does not publish. A pair is only judged when its calibration ID is listed
    /// here; record a car's values while it is known to be running stock software.
    /// </para>
    /// </summary>
    public static class StockSoftwareCatalog
    {
        private const string CatalogFile = "config\\stock_software.json";

        private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlySet<uint>>> catalog = new(Load);

        /// <summary>Calibration ID → stock CVNs; empty when the file is missing or unreadable.</summary>
        public static IReadOnlyDictionary<string, IReadOnlySet<uint>> Known => catalog.Value;

        /// <summary>
        /// Parses the catalog JSON. Entries whose CVNs do not parse as hex are skipped rather than
        /// failing the whole file.
        /// </summary>
        internal static IReadOnlyDictionary<string, IReadOnlySet<uint>> Parse(string json)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json) ?? [];
            var result = new Dictionary<string, IReadOnlySet<uint>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (calId, cvnTexts) in parsed)
            {
                var cvns = new HashSet<uint>();
                foreach (string text in cvnTexts)
                {
                    string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
                    if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint cvn))
                        cvns.Add(cvn);
                    else
                        Debug.WriteLine($"Stock software catalog: ignoring CVN '{text}' for '{calId}'.");
                }
                if (cvns.Count > 0)
                    result[calId.Trim()] = cvns;
            }

            return result;
        }

        private static IReadOnlyDictionary<string, IReadOnlySet<uint>> Load()
        {
            string? path = ResolveCatalogPath();
            if (path == null)
            {
                Debug.WriteLine($"Stock software catalog '{CatalogFile}' not found; software cannot be verified.");
                return new Dictionary<string, IReadOnlySet<uint>>();
            }

            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                // The software check degrades to "unknown"; it must not stop the emissions check.
                Debug.WriteLine($"Failed to read stock software catalog '{path}': {ex.Message}");
                return new Dictionary<string, IReadOnlySet<uint>>();
            }
        }

        /// <summary>Prefers a catalog under the working directory, then the one deployed with the exe.</summary>
        private static string? ResolveCatalogPath()
        {
            if (File.Exists(CatalogFile))
                return CatalogFile;
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CatalogFile);
            return File.Exists(exePath) ? exePath : null;
        }
    }
}
