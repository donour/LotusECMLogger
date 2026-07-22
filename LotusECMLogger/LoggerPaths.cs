namespace LotusECMLogger
{
    /// <summary>
    /// Central location for logger output files. All loggers (live data, T6 RMA, high-speed)
    /// default to <c>Documents\LotusECMLogger</c>.
    /// </summary>
    internal static class LoggerPaths
    {
        /// <summary>Root output directory: <c>Documents\LotusECMLogger</c>. Not guaranteed to exist.</summary>
        public static string OutputDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LotusECMLogger");

        /// <summary>Builds a default output path like <c>Documents\LotusECMLogger\{prefix}_{timestamp}.csv</c>.</summary>
        public static string TimestampedCsvPath(string prefix) => TimestampedPath(prefix, "csv");

        /// <summary>Builds a default output path like <c>Documents\LotusECMLogger\{prefix}_{timestamp}.{extension}</c>.</summary>
        public static string TimestampedPath(string prefix, string extension) =>
            Path.Combine(OutputDirectory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");

        /// <summary>
        /// Returns <paramref name="filePath"/> if nothing exists there; otherwise appends _1, _2, …
        /// before the extension until the name is free, so an existing log is never overwritten.
        /// </summary>
        public static string UniquePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string dir = Path.GetDirectoryName(filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            for (int i = 1; ; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        /// <summary>Creates the parent directory of <paramref name="filePath"/> if it does not exist.</summary>
        public static void EnsureParentDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
