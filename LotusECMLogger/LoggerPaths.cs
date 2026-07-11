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
        public static string TimestampedCsvPath(string prefix) =>
            Path.Combine(OutputDirectory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        /// <summary>Creates the parent directory of <paramref name="filePath"/> if it does not exist.</summary>
        public static void EnsureParentDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
