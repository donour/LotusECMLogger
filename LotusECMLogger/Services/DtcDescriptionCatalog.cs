using System.Diagnostics;
using System.Text.Json;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Plain-English descriptions for SAE J2012 diagnostic trouble codes, read once at startup from
    /// <c>config\obd_ii_code_descriptions.json</c> (a flat code → text object) into a
    /// case-insensitive dictionary.
    /// <para>
    /// The shipped table is derived from <c>documentation/OBD/Lotus OBD Codes/OBD2-DTCs.csv</c> in
    /// https://github.com/donour/LotusECU-T4e. Where that source lists a code more than once —
    /// alternate readings drawn from different manufacturers' tables, plus the odd spelling
    /// variant — every distinct wording is kept, joined by " | ", because nothing in the source
    /// says which reading a given ECU intends.
    /// </para>
    /// </summary>
    public static class DtcDescriptionCatalog
    {
        private const string CatalogFile = "config\\obd_ii_code_descriptions.json";

        private static readonly Lazy<Dictionary<string, string>> catalog = new(Load);

        /// <summary>Codes available for labelling; zero when the catalog is missing or unreadable.</summary>
        public static int Count => catalog.Value.Count;

        /// <summary>
        /// Reads the catalog now rather than on first lookup, so the file I/O is paid for at
        /// startup and a missing or malformed catalog shows up in the debug log there too.
        /// </summary>
        public static void Preload() => _ = catalog.Value;

        /// <summary>The description for a code such as "P0301", or null when it is not catalogued.</summary>
        public static string? TryGetDescription(string code) =>
            catalog.Value.TryGetValue(code, out string? description) ? description : null;

        private static Dictionary<string, string> Load()
        {
            string? path = ResolveCatalogPath();
            if (path == null)
            {
                Debug.WriteLine($"DTC catalog '{CatalogFile}' not found; codes will be shown unlabelled.");
                return [];
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                // Rebuilt with an ordinal-ignore-case comparer so lookups tolerate either casing.
                return parsed == null ? [] : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Descriptions are cosmetic: an unreadable catalog must not stop the app starting,
                // nor stop it reading codes.
                Debug.WriteLine($"Failed to read DTC catalog '{path}': {ex.Message}");
                return [];
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
