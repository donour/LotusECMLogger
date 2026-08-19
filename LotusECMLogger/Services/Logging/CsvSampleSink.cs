using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services.Logging
{
    /// <summary>
    /// Writes samples to a CSV file in the logger's canonical layout:
    /// <code>
    /// # &lt;title&gt;
    /// # Started: 2026-08-18T14:30:22.123456-05:00
    /// # &lt;note&gt;…
    /// Timestamp,RelativeTime_ms,&lt;column&gt;,&lt;column&gt;,…
    /// </code>
    /// Timestamps, relative times and values are always formatted with
    /// <see cref="CultureInfo.InvariantCulture"/>, so a comma-decimal locale can never corrupt the
    /// field separator or shift the time separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column set is fixed at construction, so the write path is deliberately allocation-light:
    /// a name-to-column map replaces any searching, values live in flat arrays indexed by column,
    /// and each row is formatted into a reused buffer. The high-speed logger drives this at several
    /// hundred rows per second, where per-row allocation or scanning shows up directly as pressure
    /// on the CAN drain thread behind it.
    /// </para>
    /// <para>
    /// A numeric cell with no value is held as <see cref="double.NaN"/> rather than a nullable,
    /// which keeps the row array half the size and lets an unavailable sensor reading (written as an
    /// empty cell) fall out of the same check that rejects a non-finite computed value.
    /// </para>
    /// <para>
    /// How a cell is written is a property of its <see cref="SampleColumn"/>, not of the value it
    /// holds, so raw ECU memory keeps its hexadecimal form (<c>0x1F</c>) instead of being flattened
    /// into a decimal that has to be converted back before it means anything.
    /// </para>
    /// <para>
    /// Rows sit in the <see cref="StreamWriter"/> buffer until a flush bound is reached, so an
    /// abrupt end to a session — a crash, a closed lid, a pulled USB cable — loses whatever is still
    /// buffered. Flushing on whichever of <see cref="FlushEveryRows"/> or <see cref="FlushInterval"/>
    /// comes first keeps a fast logger from buffering much while also bounding how far behind real
    /// time the file can fall when the ECU is answering slowly.
    /// </para>
    /// <para>Not thread-safe; see <see cref="ISampleSink"/>.</para>
    /// </remarks>
    public sealed class CsvSampleSink : ISampleSink
    {
        /// <summary>Rows buffered before a flush is forced.</summary>
        public const int FlushEveryRows = 100;

        /// <summary>Longest a written row may sit unflushed.</summary>
        public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

        public const string TimestampColumn = "Timestamp";
        public const string RelativeTimeColumn = "RelativeTime_ms";

        /// <summary>
        /// ISO 8601, to microseconds, with the UTC offset. The offset costs six characters and buys
        /// timestamps that stay unambiguous across a DST transition — a log started before the
        /// autumn change and finished after it would otherwise repeat an hour with no way to tell
        /// the halves apart.
        /// </summary>
        internal const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffK";

        /// <summary>Wide enough for a full timestamp (32 chars) and any "G"-formatted double (24).</summary>
        private const int CellBufferChars = 48;

        private readonly StreamWriter _writer;

        /// <summary>Column name to its index. Replaces searching a list.</summary>
        private readonly Dictionary<string, int> _columnIndex;

        /// <summary>The declared columns, in output order; index-aligned with the value stores.</summary>
        private readonly SampleColumn[] _columns;

        /// <summary>Latest numeric value per column, carried forward between rows. NaN renders as empty.</summary>
        private readonly double[] _numbers;

        /// <summary>
        /// Latest text value per column, for <see cref="SampleFormat.Text"/> columns. Empty renders
        /// as an empty cell, which is the same thing a text column has no value to say.
        /// </summary>
        private readonly string[] _text;

        /// <summary>Origin for a derived <see cref="RelativeTimeColumn"/>; the instant the preamble reports.</summary>
        private readonly DateTime _sessionStart;

        private readonly StringBuilder _rowBuilder = new(1024);
        private readonly Stopwatch _sinceFlush = Stopwatch.StartNew();
        private int _rowsSinceFlush;
        private bool _disposed;

        /// <summary>
        /// Opens <paramref name="filePath"/> (creating its directory), writes the preamble and the
        /// header row, and times the session from now.
        /// </summary>
        public CsvSampleSink(string filePath, SampleLogHeader header, IEnumerable<SampleColumn> columns)
            : this(filePath, header, columns, DateTime.Now)
        {
        }

        /// <param name="sessionStart">
        /// Reported as <c># Started:</c> and used as the origin for derived relative times. Passed
        /// explicitly by <see cref="DiscoveringSampleSink"/>, whose session begins before it knows
        /// its columns and so before this sink can exist.
        /// </param>
        public CsvSampleSink(string filePath, SampleLogHeader header, IEnumerable<SampleColumn> columns,
            DateTime sessionStart)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(columns);

            _sessionStart = sessionStart;

            _columns = columns.DistinctBy(column => column.Name, StringComparer.Ordinal).ToArray();
            _columnIndex = new Dictionary<string, int>(_columns.Length, StringComparer.Ordinal);
            _numbers = new double[_columns.Length];
            _text = new string[_columns.Length];
            Array.Fill(_numbers, double.NaN);
            Array.Fill(_text, string.Empty);

            LoggerPaths.EnsureParentDirectory(filePath);
            _writer = new StreamWriter(filePath, false, Encoding.UTF8) { AutoFlush = false };

            _writer.WriteLine($"# {header.Title}");
            _writer.WriteLine($"# Started: {sessionStart.ToString(TimestampFormat, CultureInfo.InvariantCulture)}");
            foreach (string note in header.Notes)
                _writer.WriteLine($"# {note}");

            _rowBuilder.Append(TimestampColumn).Append(',').Append(RelativeTimeColumn);
            for (int i = 0; i < _columns.Length; i++)
            {
                _columnIndex[_columns[i].Name] = i;
                _rowBuilder.Append(',').Append(EscapeCsv(_columns[i].Name));
            }
            _writer.WriteLine(_rowBuilder);

            // Flushed so the file exists with a readable header the moment logging starts, rather
            // than staying empty until the first hundred rows arrive.
            _writer.Flush();
        }

        public void Set(string column, double value)
        {
            if (_columnIndex.TryGetValue(column, out int index))
                _numbers[index] = value;
        }

        public void SetText(string column, string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_columnIndex.TryGetValue(column, out int index))
                _text[index] = value;
        }

        public void Clear(string column)
        {
            if (_columnIndex.TryGetValue(column, out int index))
            {
                _numbers[index] = double.NaN;
                _text[index] = string.Empty;
            }
        }

        public void WriteRow(DateTime timestamp) =>
            WriteRow(timestamp, (timestamp - _sessionStart).TotalMilliseconds);

        public void WriteRow(DateTime timestamp, double relativeTimeMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            BuildRow(timestamp, relativeTimeMs);

            // The StringBuilder overload copies straight into the writer's buffer, so the row never
            // materialises as a separate string.
            _writer.WriteLine(_rowBuilder);

            if (++_rowsSinceFlush < FlushEveryRows && _sinceFlush.Elapsed < FlushInterval)
                return;

            Flush();
        }

        public void Flush()
        {
            if (_disposed)
                return;

            _writer.Flush();
            _rowsSinceFlush = 0;
            _sinceFlush.Restart();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                _writer.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CsvSampleSink: error flushing log on close: {ex.Message}");
            }
            finally
            {
                _writer.Dispose();
            }
        }

        /// <summary>Renders one row into <see cref="_rowBuilder"/> without allocating per cell.</summary>
        private void BuildRow(DateTime timestamp, double relativeTimeMs)
        {
            _rowBuilder.Clear();
            Span<char> cell = stackalloc char[CellBufferChars];

            // "K" renders nothing at all for an unspecified-kind DateTime, which would quietly drop
            // the offset and leave the column ambiguous. Every producer stamps rows with
            // DateTime.Now, so reading an unspecified one as local is what it already meant.
            if (timestamp.Kind == DateTimeKind.Unspecified)
                timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Local);

            if (timestamp.TryFormat(cell, out int written, TimestampFormat, CultureInfo.InvariantCulture))
                _rowBuilder.Append(cell[..written]);
            else
                _rowBuilder.Append(timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture));

            _rowBuilder.Append(',');
            AppendNumber(cell, relativeTimeMs, "F3");

            for (int i = 0; i < _columns.Length; i++)
            {
                _rowBuilder.Append(',');
                AppendCell(cell, i);
            }
        }

        /// <summary>Renders one cell according to its column's format. An absent value writes nothing.</summary>
        private void AppendCell(Span<char> cell, int index)
        {
            SampleColumn column = _columns[index];

            if (column.Format == SampleFormat.Text)
            {
                string text = _text[index];
                if (text.Length > 0)
                    _rowBuilder.Append(EscapeCsv(text));
                return;
            }

            double value = _numbers[index];
            if (!double.IsFinite(value))
                return;

            if (column.Format == SampleFormat.Number)
            {
                AppendNumber(cell, value, "G");
                return;
            }

            // Hex: the stored value is a whole number (a byte, word or dword read from the ECU), and
            // every such value is exact in a double well beyond the 64 bits this can render.
            long raw = (long)Math.Round(value);
            _rowBuilder.Append("0x");
            if (raw.TryFormat(cell, out int written, column.HexFormat, CultureInfo.InvariantCulture))
                _rowBuilder.Append(cell[..written]);
            else
                _rowBuilder.Append(raw.ToString(column.HexFormat, CultureInfo.InvariantCulture));
        }

        private void AppendNumber(Span<char> cell, double value, ReadOnlySpan<char> format)
        {
            if (value.TryFormat(cell, out int written, format, CultureInfo.InvariantCulture))
                _rowBuilder.Append(cell[..written]);
            else
                _rowBuilder.Append(value.ToString(format.ToString(), CultureInfo.InvariantCulture));
        }

        private static string EscapeCsv(string name) =>
            name.Contains(',') || name.Contains('"') ? $"\"{name.Replace("\"", "\"\"")}\"" : name;
    }
}
