using System.Globalization;
using System.Text;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// One change to the ECU's calibration RAM during a recording: the bytes before and after, when, whether this
    /// session wrote it or a refresh found it, and the items it touched (for people reading the file).
    /// </summary>
    public sealed record RecordedEdit(DateTime At, uint Address, byte[] Before, byte[] After, bool Found, string Items)
    {
        /// <summary>Seconds into the recording; set when the recording is read.</summary>
        public double Time { get; init; }
    }

    /// <summary>
    /// The recording's <c>X.edits.csv</c>: a line per change to the ECU's calibration RAM, timestamped on the log's clock
    /// (<see cref="HighSpeedLogService.LogTime"/>) so it lines up with the log's Timestamp column (local time with its offset). Appends may come from any thread; each line is
    /// flushed at once, since writes are rare and losing one would make the replay show the wrong calibration.
    /// </summary>
    public sealed class CalibrationEditLog : IDisposable
    {
        public const string Header = "Timestamp,Address,Before,After,Source,Items";
        private const string WriteSource = "write", FoundSource = "found";

        private readonly object _lock = new();
        private readonly StreamWriter _writer;
        private bool _disposed;

        private CalibrationEditLog(StreamWriter writer) => _writer = writer;

        public int Count { get; private set; }

        public static CalibrationEditLog Create(string path, string logName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            LoggerPaths.EnsureParentDirectory(path);
            var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine($"# Changes to the ECU's calibration RAM while {logName} was recorded: verified writes, and runs a refresh found changed.");
            writer.WriteLine("# Before and After are the bytes from Address on, in hex.");
            writer.WriteLine(Header);
            writer.Flush();
            return new CalibrationEditLog(writer);
        }

        /// <param name="at">When, on the log's clock; the change's own (wall-clock) time when null.</param>
        public void Append(CalibrationWrittenEventArgs change, string items, DateTime? at = null)
        {
            ArgumentNullException.ThrowIfNull(change);
            var when = at ?? change.At;
            var stamp = when.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(when, DateTimeKind.Local) : when;
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{stamp.ToString(CsvSampleSink.TimestampFormat, CultureInfo.InvariantCulture)},0x{change.Address:X8},{Convert.ToHexString(change.Before)}," +
                $"{Convert.ToHexString(change.After)},{(change.Found ? FoundSource : WriteSource)},{Csv.Escape(items)}");
            lock (_lock)
            {
                if (_disposed) return;
                _writer.WriteLine(line);
                _writer.Flush();
                Count++;
            }
        }

        /// <summary>The changes in the file, in the order written.</summary>
        public static List<RecordedEdit> Read(string path)
        {
            var edits = new List<RecordedEdit>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("Timestamp,", StringComparison.Ordinal)) continue;
                var fields = Csv.Split(line);
                if (fields.Count < 5)
                    throw new InvalidDataException($"{Path.GetFileName(path)}: '{line}' has {fields.Count} fields; expected {Header}.");
                try
                {
                    edits.Add(new RecordedEdit(
                        DateTime.Parse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        uint.Parse(fields[1].Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        Convert.FromHexString(fields[2]), Convert.FromHexString(fields[3]), fields[4] == FoundSource,
                        fields.Count > 5 ? fields[5] : ""));
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)}: '{line}' cannot be read: {ex.Message}", ex);
                }
            }
            return edits;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _writer.Dispose();
            }
        }
    }

    /// <summary>The CSV quoting the logs use: a field with a comma or quote is quoted, quotes doubled.</summary>
    internal static class Csv
    {
        public static string Escape(string field) =>
            field.Contains(',') || field.Contains('"') ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

        public static List<string> Split(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
