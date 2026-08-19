namespace LotusECMLogger.Services.Logging
{
    /// <summary>
    /// A sink for producers whose column set is not known until the ECU starts answering. Rows are
    /// held while the column names are collected, then a <see cref="CsvSampleSink"/> is created with
    /// the discovered columns and the held rows are replayed into it. Everything after that is
    /// delegated straight through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the OBD Live Data logger needs this: which readings a response yields depends on the
    /// PIDs the ECU actually answers, and one PID can produce several readings. Every other logger
    /// knows its columns up front and constructs a <see cref="CsvSampleSink"/> directly.
    /// </para>
    /// <para>
    /// The held rows are written once the column set is known, including when a session ends before
    /// the discovery window closes. The previous implementation discarded them, so every Live Data
    /// log silently lost its opening samples and a session shorter than the window produced a file
    /// with no rows at all.
    /// </para>
    /// <para>Not thread-safe; see <see cref="ISampleSink"/>.</para>
    /// </remarks>
    public sealed class DiscoveringSampleSink : ISampleSink
    {
        /// <summary>Rows sampled for column names before the header is frozen.</summary>
        public const int DefaultDiscoveryRows = 100;

        private readonly string _filePath;
        private readonly SampleLogHeader _header;
        private readonly int _discoveryRows;

        /// <summary>Timed from here, not from the freeze, so the log reports when logging began.</summary>
        private readonly DateTime _sessionStart;

        /// <summary>Every column name seen so far, with its latest value.</summary>
        private readonly Dictionary<string, PendingCell> _pending = new(StringComparer.Ordinal);

        /// <summary>Rows held back until the column set is known, then replayed.</summary>
        private readonly List<BufferedRow> _buffered;

        private CsvSampleSink? _frozen;
        private bool _disposed;

        /// <summary>
        /// A value seen during discovery. Which setter was used decides the column's format once the
        /// header is frozen, so a channel only ever written as text does not become a numeric column.
        /// </summary>
        private readonly record struct PendingCell(double Number, string Text, bool IsText)
        {
            public static PendingCell FromNumber(double value) => new(value, string.Empty, false);
            public static PendingCell FromText(string value) => new(double.NaN, value, true);

            /// <summary>Cleared, keeping the kind so the frozen column keeps its format.</summary>
            public PendingCell Cleared() => IsText ? FromText(string.Empty) : FromNumber(double.NaN);
        }

        /// <summary>
        /// One held row, with a snapshot of the values known at the time. A relative time of NaN
        /// means the row did not carry an explicit one and should be timed from the session start.
        /// </summary>
        private readonly record struct BufferedRow(
            DateTime Timestamp,
            double RelativeTimeMs,
            KeyValuePair<string, PendingCell>[] Values);

        public DiscoveringSampleSink(string filePath, SampleLogHeader header,
            int discoveryRows = DefaultDiscoveryRows)
            : this(filePath, header, discoveryRows, DateTime.Now)
        {
        }

        /// <param name="sessionStart">
        /// Reported as <c># Started:</c> and used as the origin for derived relative times. It is
        /// taken when logging begins, not when the columns are frozen, so the log reports the start
        /// of the session rather than the end of the discovery window.
        /// </param>
        public DiscoveringSampleSink(string filePath, SampleLogHeader header, int discoveryRows,
            DateTime sessionStart)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(header);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discoveryRows);

            _filePath = filePath;
            _header = header;
            _discoveryRows = discoveryRows;
            _sessionStart = sessionStart;
            _buffered = new List<BufferedRow>(discoveryRows);

            // The file itself is not created until the columns are known, but failing to create its
            // directory is worth reporting now rather than a hundred rows from now.
            LoggerPaths.EnsureParentDirectory(filePath);
        }

        public void Set(string column, double value)
        {
            if (_frozen is CsvSampleSink sink)
                sink.Set(column, value);
            else
                _pending[column] = PendingCell.FromNumber(value);
        }

        public void SetText(string column, string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_frozen is CsvSampleSink sink)
                sink.SetText(column, value);
            else
                _pending[column] = PendingCell.FromText(value);
        }

        public void Clear(string column)
        {
            if (_frozen is CsvSampleSink sink)
                sink.Clear(column);
            else if (_pending.TryGetValue(column, out PendingCell existing))
                _pending[column] = existing.Cleared();
            else
                _pending[column] = PendingCell.FromNumber(double.NaN);
        }

        public void WriteRow(DateTime timestamp) => Add(timestamp, double.NaN);

        public void WriteRow(DateTime timestamp, double relativeTimeMs) => Add(timestamp, relativeTimeMs);

        private void Add(DateTime timestamp, double relativeTimeMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_frozen is CsvSampleSink sink)
            {
                Emit(sink, timestamp, relativeTimeMs);
                return;
            }

            _buffered.Add(new BufferedRow(timestamp, relativeTimeMs, _pending.ToArray()));
            if (_buffered.Count >= _discoveryRows)
                Freeze();
        }

        /// <summary>
        /// Deliberately does not end discovery: freezing early would cut short the column set and
        /// drop every channel first seen after the flush. Nothing is on disk to flush until then.
        /// </summary>
        public void Flush() => _frozen?.Flush();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // A session shorter than the discovery window still gets its rows written.
            if (_frozen is null)
                Freeze();

            _frozen?.Dispose();
        }

        private void Freeze()
        {
            IEnumerable<SampleColumn> columns = _pending
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Value.IsText
                    ? SampleColumn.Text(entry.Key)
                    : SampleColumn.Number(entry.Key));

            var sink = new CsvSampleSink(_filePath, _header, columns, _sessionStart);
            _frozen = sink;

            // Each snapshot is cumulative, so replaying them reproduces the carry-forward behaviour
            // exactly, and a column not yet seen stays empty in the rows before its first reading.
            foreach (BufferedRow row in _buffered)
            {
                foreach ((string name, PendingCell value) in row.Values)
                {
                    if (value.IsText)
                        sink.SetText(name, value.Text);
                    else if (double.IsNaN(value.Number))
                        sink.Clear(name);
                    else
                        sink.Set(name, value.Number);
                }
                Emit(sink, row.Timestamp, row.RelativeTimeMs);
            }

            _buffered.Clear();
            _pending.Clear();
        }

        private static void Emit(CsvSampleSink sink, DateTime timestamp, double relativeTimeMs)
        {
            if (double.IsNaN(relativeTimeMs))
                sink.WriteRow(timestamp);
            else
                sink.WriteRow(timestamp, relativeTimeMs);
        }
    }
}
