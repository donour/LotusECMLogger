using System.Globalization;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// One logged channel's raw values over time, kept only where they change: the log carries every value forward
    /// row to row, so a value holds until the next entry. Times are seconds into the recording.
    /// </summary>
    public sealed class ChannelSeries
    {
        private readonly List<double> _times = [];
        private readonly List<ulong> _bits = [];

        public ChannelSeries(LoggedChannel channel) => Channel = channel;

        public LoggedChannel Channel { get; }
        public IReadOnlyList<double> Times => _times;
        public int Count => _times.Count;

        internal void Add(double time, ulong bits)
        {
            if (_bits.Count > 0 && _bits[^1] == bits) return;
            _times.Add(time);
            _bits.Add(bits);
        }

        /// <summary>The value holding at <paramref name="time"/>, or null before the first.</summary>
        public ulong? At(double time)
        {
            int i = IndexAt(time);
            return i < 0 ? null : _bits[i];
        }

        /// <summary>The index of the last entry at or before <paramref name="time"/>, or -1.</summary>
        public int IndexAt(double time)
        {
            int lo = 0, hi = _times.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (_times[mid] <= time) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }
    }

    /// <summary>
    /// A saved recording read back for replay (steps 3 and 4 of recording and replay): its channels' raw values over
    /// time, the calibration writes, and the calibration at its start and end. Any high-speed log can be read; one
    /// without the <c>channel:</c> preamble lines or the side files simply has less to replay.
    /// </summary>
    public sealed class TuningRecording
    {
        private readonly Dictionary<(uint Address, int Size), ChannelSeries> _series;

        private TuningRecording(RecordingFiles files, string? build, DateTime origin, double duration, int rows,
            Dictionary<(uint, int), ChannelSeries> series, IReadOnlyList<LoggedChannel> unreadable, IReadOnlyList<RecordedEdit> edits,
            byte[]? calibrationStart, byte[]? calibrationEnd)
        {
            (Files, Build, Origin, Duration, Rows, _series, Unreadable, Edits, CalibrationStart, CalibrationEnd) =
                (files, build, origin, duration, rows, series, unreadable, edits, calibrationStart, calibrationEnd);
        }

        public RecordingFiles Files { get; }
        /// <summary>The ECU build the preamble names, or null for a log that does not say.</summary>
        public string? Build { get; }
        /// <summary>The wall-clock time of time 0.</summary>
        public DateTime Origin { get; }
        /// <summary>Seconds from time 0 to the last row or recorded write, whichever is later.</summary>
        public double Duration { get; }
        public int Rows { get; }
        public IEnumerable<ChannelSeries> Series() => _series.Values;
        /// <summary>Channels whose values could not be turned back into raw values (no scaling known), so they are not replayed.</summary>
        public IReadOnlyList<LoggedChannel> Unreadable { get; }
        /// <summary>The calibration writes, by time.</summary>
        public IReadOnlyList<RecordedEdit> Edits { get; }
        public byte[]? CalibrationStart { get; }
        public byte[]? CalibrationEnd { get; }

        public ChannelSeries? SeriesOf(uint address, int size) => _series.GetValueOrDefault((address, size));

        /// <summary>The recording's raw values as they stood at <paramref name="time"/>.</summary>
        public IRawValueSource At(double time) => new Moment(this, time);

        public double TimeOf(DateTime at) => (at.ToUniversalTime() - Origin.ToUniversalTime()).TotalSeconds;

        private sealed class Moment(TuningRecording recording, double time) : IRawValueSource
        {
            public ulong? Bits(uint address, int size) => recording.SeriesOf(address, size)?.At(time);
        }

        /// <summary>
        /// Reads a log and its side files. <paramref name="complete"/> may supply the scaling of a channel the preamble
        /// does not give in full (a log from before the <c>channel:</c> lines), e.g. from the A2L measurement at its address.
        /// </summary>
        public static TuningRecording Load(string logPath, Func<LoggedChannel, LoggedChannel?>? complete = null)
        {
            var files = new RecordingFiles(logPath);
            string? build = null;
            var channels = new Dictionary<string, LoggedChannel>(StringComparer.Ordinal);
            var unreadable = new List<LoggedChannel>();
            string[]? header = null;
            var columns = new List<(int Index, ChannelSeries Series)>();
            var series = new Dictionary<(uint, int), ChannelSeries>();
            DateTime? origin = null;
            double last = 0;
            int rows = 0;

            foreach (var line in File.ReadLines(files.Log))
            {
                if (header is null)
                {
                    if (line.StartsWith('#'))
                    {
                        string note = line.TrimStart('#').TrimStart();
                        if (note.StartsWith("build: ", StringComparison.Ordinal)) build = note["build: ".Length..].Trim();
                        else if (LoggedChannel.Parse(note) is { } channel) channels.TryAdd(channel.Name, channel);
                        continue;
                    }
                    if (line.Length == 0) continue;
                    header = [.. Csv.Split(line)];
                    if (header.Length < 2 || header[0] != CsvSampleSink.TimestampColumn || header[1] != CsvSampleSink.RelativeTimeColumn)
                        throw new InvalidDataException($"{Path.GetFileName(files.Log)} is not a sample log: its header is '{line}'.");
                    for (int i = 2; i < header.Length; i++)
                    {
                        if (!channels.TryGetValue(header[i], out var channel)) continue;
                        var exact = channel.IsExact ? channel : complete?.Invoke(channel);
                        if (exact is not { IsExact: true })
                        {
                            unreadable.Add(channel);
                            continue;
                        }
                        // Two columns for one variable (a user channel and an input) carry the same values: keep the first.
                        if (series.ContainsKey((exact.Address, exact.Size))) continue;
                        var s = new ChannelSeries(exact);
                        series[(exact.Address, exact.Size)] = s;
                        columns.Add((i, s));
                    }
                    continue;
                }
                if (line.Length == 0) continue;
                var fields = line.Split(',');
                if (fields.Length < 2 || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)) continue;
                double time = ms / 1000;
                if (origin is null && DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp))
                    origin = stamp.AddMilliseconds(-ms);
                foreach (var (index, s) in columns)
                {
                    if (index >= fields.Length || fields[index].Length == 0) continue;
                    if (double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                        s.Channel.BitsOf(value) is ulong bits)
                        s.Add(time, bits);
                }
                last = Math.Max(last, time);
                rows++;
            }
            if (header is null) throw new InvalidDataException($"{Path.GetFileName(files.Log)} has no column header.");

            var start = origin ?? File.GetCreationTime(files.Log);
            var edits = File.Exists(files.Edits)
                ? CalibrationEditLog.Read(files.Edits).Select(e => e with { Time = (e.At.ToUniversalTime() - start.ToUniversalTime()).TotalSeconds })
                    .OrderBy(e => e.Time).ToList()
                : [];
            // The recording lasts until its last row or its last write, whichever is later.
            double duration = Math.Max(last, edits.Count > 0 ? edits[^1].Time : 0);
            return new TuningRecording(files, build, start, duration, rows, series, unreadable, edits,
                File.Exists(files.CalibrationStart) ? File.ReadAllBytes(files.CalibrationStart) : null,
                File.Exists(files.CalibrationEnd) ? File.ReadAllBytes(files.CalibrationEnd) : null);
        }
    }

    /// <summary>
    /// A calibration image kept at what the ECU held at a moment of a recording: the start calibration with the recorded
    /// writes up to that moment applied (undone again when seeking back). The image is for viewing; it never syncs.
    /// </summary>
    public sealed class ReplayCalibration
    {
        private readonly IReadOnlyList<RecordedEdit> _edits;

        /// <param name="image">The calibration at the recording's start; it is moved by <see cref="SeekTo"/>.</param>
        public ReplayCalibration(CalibrationImage image, IReadOnlyList<RecordedEdit> edits)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(edits);
            (Image, _edits) = (image, edits);
        }

        public CalibrationImage Image { get; }
        /// <summary>How many of the writes are applied: those at or before the last seek.</summary>
        public int Applied { get; private set; }

        public void SeekTo(double time)
        {
            while (Applied < _edits.Count && _edits[Applied].Time <= time)
            {
                Apply(_edits[Applied], after: true);
                Applied++;
            }
            while (Applied > 0 && _edits[Applied - 1].Time > time)
            {
                Applied--;
                Apply(_edits[Applied], after: false);
            }
        }

        private void Apply(RecordedEdit edit, bool after)
        {
            var bytes = after ? edit.After : edit.Before;
            if (Image.Contains(edit.Address, bytes.Length)) Image.ApplyRecorded(edit.Address, bytes);
        }
    }
}
