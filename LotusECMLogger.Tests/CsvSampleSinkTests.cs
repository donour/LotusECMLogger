using System.Globalization;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the sink every logger now writes through. Golden-string assertions use UTC timestamps
    /// so the offset in the canonical format is deterministic on any machine; <see
    /// cref="LocalTimestamp_RoundTripsToTheSameInstant"/> covers the local-time case the loggers
    /// actually produce.
    /// </summary>
    public sealed class CsvSampleSinkTests : IDisposable
    {
        private static readonly DateTime Start = new(2026, 8, 18, 14, 30, 0, DateTimeKind.Utc);
        private const string StartedLine = "# Started: 2026-08-18T14:30:00.000000Z";

        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "LotusECMLoggerTests", Guid.NewGuid().ToString("N"));

        public CsvSampleSinkTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); }
            catch (IOException) { /* a lingering handle must not fail the test run */ }
        }

        private string LogPath([System.Runtime.CompilerServices.CallerMemberName] string name = "log") =>
            Path.Combine(_dir, $"{name}.csv");

        /// <summary>Reads a log that may still be open, so flush behaviour can be observed mid-session.</summary>
        private static string[] Lines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        }

        // ── Canonical layout ──────────────────────────────────────────────────────────────────

        [Fact]
        public void WritesPreambleHeaderAndRows()
        {
            string path = LogPath();
            var header = new SampleLogHeader("Test Log", ["note one", "note two"]);

            using (var sink = new CsvSampleSink(path, header, ["rpm", "coolant"], Start))
            {
                sink.Set("rpm", 1450.5);
                sink.Set("coolant", 84);
                sink.WriteRow(Start.AddMilliseconds(250));
            }

            Assert.Equal(
            [
                "# Test Log",
                StartedLine,
                "# note one",
                "# note two",
                "Timestamp,RelativeTime_ms,rpm,coolant",
                "2026-08-18T14:30:00.250000Z,250.000,1450.5,84",
            ], Lines(path));
        }

        [Fact]
        public void HeaderWithoutNotes_HasTitleAndStartOnly()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Bare"), ["x"], Start))
                sink.WriteRow(Start);

            Assert.Equal(["# Bare", StartedLine, "Timestamp,RelativeTime_ms,x", "2026-08-18T14:30:00.000000Z,0.000,"],
                Lines(path));
        }

        // ── Value semantics ───────────────────────────────────────────────────────────────────

        [Fact]
        public void ValuesCarryForwardUntilChangedOrCleared()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Hold"), ["a", "b"], Start))
            {
                sink.Set("a", 1);
                sink.Set("b", 2);
                sink.WriteRow(Start, 0);

                sink.Set("a", 3);          // b is not restated
                sink.WriteRow(Start, 1);

                sink.Clear("b");
                sink.WriteRow(Start, 2);
            }

            Assert.Equal(["1,2", "3,2", "3,"], DataRows(path));
        }

        [Fact]
        public void UnsetColumn_IsEmptyNotZero()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Empty"), ["seen", "unseen"], Start))
            {
                sink.Set("seen", 0);
                sink.WriteRow(Start, 0);
            }

            // A genuine zero and "the module never reported this" must not look alike.
            Assert.Equal(["0,"], DataRows(path));
        }

        [Fact]
        public void UndeclaredColumn_IsIgnored()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Drift"), ["a"], Start))
            {
                sink.Set("a", 1);
                sink.Set("appeared-mid-session", 99);
                sink.WriteRow(Start, 0);
            }

            Assert.Equal("Timestamp,RelativeTime_ms,a", Lines(path)[2]);
            Assert.Equal(["1"], DataRows(path));
        }

        [Theory]
        [InlineData(double.NaN, "nan")]
        [InlineData(double.PositiveInfinity, "posinf")]
        [InlineData(double.NegativeInfinity, "neginf")]
        public void NonFiniteValue_WritesAnEmptyCell(double value, string name)
        {
            string path = LogPath($"nonfinite_{name}");
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("NonFinite"), ["a"], Start))
            {
                sink.Set("a", value);
                sink.WriteRow(Start, 0);
            }

            Assert.Equal([""], DataRows(path));
        }

        [Fact]
        public void DuplicateColumns_AreCollapsed()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Dupes"), ["a", "a", "b"], Start))
            {
                sink.Set("a", 1);
                sink.Set("b", 2);
                sink.WriteRow(Start, 0);
            }

            Assert.Equal("Timestamp,RelativeTime_ms,a,b", Lines(path)[2]);
            Assert.Equal(["1,2"], DataRows(path));
        }

        // ── Column formats ────────────────────────────────────────────────────────────────────

        [Fact]
        public void HexColumn_WritesRawHexNotDecimal()
        {
            string path = LogPath();
            SampleColumn[] columns = [SampleColumn.Hex("Byte0"), SampleColumn.Hex("Byte1")];

            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Hex"), columns, Start))
            {
                sink.Set("Byte0", 0x1F);
                sink.Set("Byte1", 0x00);
                sink.WriteRow(Start, 0);

                sink.Set("Byte0", 0xFF);
                sink.WriteRow(Start, 1);
            }

            Assert.Equal(["0x1F,0x00", "0xFF,0x00"], DataRows(path));
        }

        [Theory]
        [InlineData(2, 0x1F, "0x1F")]
        [InlineData(4, 0x1F, "0x001F")]
        [InlineData(8, 0x40000000, "0x40000000")]
        public void HexColumn_PadsToItsDeclaredWidth(int digits, long value, string expected)
        {
            string path = LogPath($"hexwidth_{digits}");
            using (var sink = new CsvSampleSink(
                path, new SampleLogHeader("Width"), [SampleColumn.Hex("raw", digits)], Start))
            {
                sink.Set("raw", value);
                sink.WriteRow(Start, 0);
            }

            Assert.Equal([expected], DataRows(path));
        }

        [Fact]
        public void HexColumn_WithNoValue_IsEmptyNotZero()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(
                path, new SampleLogHeader("HexEmpty"), [SampleColumn.Hex("a"), SampleColumn.Hex("b")], Start))
            {
                sink.Set("a", 0);
                sink.WriteRow(Start, 0);   // "b" never set

                sink.Clear("a");
                sink.WriteRow(Start, 1);
            }

            // 0x00 is a byte the ECU actually reported; an empty cell is one it did not.
            Assert.Equal(["0x00,", ","], DataRows(path));
        }

        [Fact]
        public void MixedFormats_RenderIndependently()
        {
            string path = LogPath();
            SampleColumn[] columns =
                [SampleColumn.Number("rpm"), SampleColumn.Hex("status", 4), SampleColumn.Text("gear")];

            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Mixed"), columns, Start))
            {
                sink.Set("rpm", 1450.5);
                sink.Set("status", 0xBEEF);
                sink.SetText("gear", "N");
                sink.WriteRow(Start, 0);
            }

            Assert.Equal("Timestamp,RelativeTime_ms,rpm,status,gear", Lines(path)[2]);
            Assert.Equal(["1450.5,0xBEEF,N"], DataRows(path));
        }

        [Fact]
        public void TextColumn_CarriesForwardAndEscapes()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(
                path, new SampleLogHeader("Text"), [SampleColumn.Text("state")], Start))
            {
                sink.SetText("state", "warming up, closed loop");
                sink.WriteRow(Start, 0);
                sink.WriteRow(Start, 1);          // not restated
                sink.Clear("state");
                sink.WriteRow(Start, 2);
            }

            Assert.Equal(
                ["\"warming up, closed loop\"", "\"warming up, closed loop\"", ""],
                DataRows(path));
        }

        // ── Formatting ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void HeaderNames_WithCommaOrQuote_AreEscaped()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(
                path, new SampleLogHeader("Escapes"), ["boost, psi", "he said \"hi\"", "plain"], Start))
            {
                sink.WriteRow(Start, 0);
            }

            Assert.Equal("Timestamp,RelativeTime_ms,\"boost, psi\",\"he said \"\"hi\"\"\",plain", Lines(path)[2]);
        }

        [Fact]
        public void NumbersAndTimestamps_StayInvariantUnderCommaDecimalCulture()
        {
            string path = LogPath();
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                // A comma-decimal locale would otherwise emit "1450,5" and corrupt the separator.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                using var sink = new CsvSampleSink(path, new SampleLogHeader("Culture"), ["rpm"], Start);
                sink.Set("rpm", 1450.5);
                sink.WriteRow(Start.AddMilliseconds(1.5));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }

            Assert.Equal("2026-08-18T14:30:00.001500Z,1.500,1450.5", Lines(path)[3]);
        }

        [Fact]
        public void LocalTimestamp_RoundTripsToTheSameInstant()
        {
            string path = LogPath();
            DateTime local = DateTime.Now;

            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Local"), ["a"], local))
                sink.WriteRow(local);

            string written = Lines(path)[^1].Split(',')[0];
            var parsed = DateTimeOffset.Parse(written, CultureInfo.InvariantCulture);

            // The format stores microseconds; DateTime ticks are ten times finer, so compare at the
            // resolution the log actually keeps. The offset must survive, or a log spanning a DST
            // change becomes ambiguous.
            var expected = new DateTimeOffset(local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMicrosecond)));
            Assert.Equal(expected, parsed);
        }

        [Fact]
        public void UnspecifiedKindTimestamp_IsWrittenWithTheLocalOffset()
        {
            string path = LogPath();
            var unspecified = new DateTime(2026, 8, 18, 14, 30, 0, DateTimeKind.Unspecified);

            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Unspecified"), ["a"], unspecified))
                sink.WriteRow(unspecified);

            string written = Lines(path)[^1].Split(',')[0];
            Assert.StartsWith("2026-08-18T14:30:00.000000", written);
            Assert.True(written.Length > "2026-08-18T14:30:00.000000".Length,
                $"expected an offset suffix, got '{written}'");
        }

        // ── Relative time ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void RelativeTime_IsDerivedFromSessionStart()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Derived"), ["a"], Start))
            {
                sink.WriteRow(Start.AddSeconds(1.5));
                sink.WriteRow(Start.AddSeconds(3));
            }

            Assert.Equal(["1500.000", "3000.000"], Lines(path).Skip(3).Select(l => l.Split(',')[1]));
        }

        [Fact]
        public void ExplicitRelativeTime_IsUsedVerbatim()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Explicit"), ["a"], Start))
            {
                // The high-speed logger supplies the adapter's hardware timing, which need not agree
                // with the wall-clock gap.
                sink.WriteRow(Start.AddSeconds(9), 12.5);
            }

            Assert.Equal("12.500", Lines(path)[3].Split(',')[1]);
        }

        // ── Flushing ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RowsReachDiskOnceTheRowBoundIsMet()
        {
            string path = LogPath();
            using var sink = new CsvSampleSink(path, new SampleLogHeader("Flush"), ["a"], Start);

            for (int i = 0; i < CsvSampleSink.FlushEveryRows; i++)
            {
                sink.Set("a", i);
                sink.WriteRow(Start, i);
            }

            // 3 preamble/header lines + every row, without the sink having been closed.
            Assert.Equal(3 + CsvSampleSink.FlushEveryRows, Lines(path).Length);
        }

        [Fact]
        public void DisposeFlushesRowsStillBuffered()
        {
            string path = LogPath();
            using (var sink = new CsvSampleSink(path, new SampleLogHeader("Close"), ["a"], Start))
            {
                sink.Set("a", 7);
                sink.WriteRow(Start, 0);
                Assert.Equal(3, Lines(path).Length); // still buffered: well under the flush bound
            }

            Assert.Equal(["7"], DataRows(path));
        }

        [Fact]
        public void WriteAfterDispose_Throws()
        {
            string path = LogPath();
            var sink = new CsvSampleSink(path, new SampleLogHeader("Closed"), ["a"], Start);
            sink.Dispose();

            Assert.Throws<ObjectDisposedException>(() => sink.WriteRow(Start, 0));
        }

        // ── Column discovery ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Discovery_FreezesSortedColumnsAndReplaysHeldRows()
        {
            string path = LogPath();
            using (var sink = new DiscoveringSampleSink(path, new SampleLogHeader("Discovered"), 3, Start))
            {
                sink.Set("rpm", 1);
                sink.WriteRow(Start, 0);          // "coolant" not seen yet

                sink.Set("coolant", 2);
                sink.WriteRow(Start, 1);

                sink.Set("rpm", 3);
                sink.WriteRow(Start, 2);          // window closes here; held rows are replayed

                sink.Set("coolant", 4);
                sink.WriteRow(Start, 3);
            }

            Assert.Equal("Timestamp,RelativeTime_ms,coolant,rpm", Lines(path)[2]);
            Assert.Equal([",1", "2,1", "2,3", "4,3"], DataRows(path));
        }

        [Fact]
        public void Discovery_SessionShorterThanTheWindow_StillWritesItsRows()
        {
            string path = LogPath();
            using (var sink = new DiscoveringSampleSink(path, new SampleLogHeader("Short"), 100, Start))
            {
                sink.Set("rpm", 900);
                sink.WriteRow(Start, 0);
            }

            // The previous implementation discarded everything before the window closed, so a short
            // session produced a file with no rows at all.
            Assert.Equal("Timestamp,RelativeTime_ms,rpm", Lines(path)[2]);
            Assert.Equal(["900"], DataRows(path));
        }

        [Fact]
        public void Discovery_ClearedValueStaysEmptyThroughTheReplay()
        {
            string path = LogPath();
            using (var sink = new DiscoveringSampleSink(path, new SampleLogHeader("Cleared"), 2, Start))
            {
                sink.Set("a", 1);
                sink.Clear("a");
                sink.WriteRow(Start, 0);

                sink.Set("a", 2);
                sink.WriteRow(Start, 1);
            }

            Assert.Equal(["", "2"], DataRows(path));
        }

        [Fact]
        public void Discovery_TimesRowsFromTheSessionStartNotTheFreeze()
        {
            string path = LogPath();
            using (var sink = new DiscoveringSampleSink(path, new SampleLogHeader("Timing"), 2, Start))
            {
                sink.Set("a", 1);
                sink.WriteRow(Start.AddSeconds(1));
                sink.WriteRow(Start.AddSeconds(2));
            }

            Assert.Equal(StartedLine, Lines(path)[1]);
            Assert.Equal(["1000.000", "2000.000"], Lines(path).Skip(3).Select(l => l.Split(',')[1]));
        }

        [Fact]
        public void Discovery_KeepsTextColumnsAsText()
        {
            string path = LogPath();
            using (var sink = new DiscoveringSampleSink(path, new SampleLogHeader("Kinds"), 2, Start))
            {
                sink.Set("rpm", 900);
                sink.SetText("state", "idle");
                sink.WriteRow(Start, 0);

                sink.SetText("state", "cranking");
                sink.WriteRow(Start, 1);
            }

            // A column only ever written as text must not be frozen as a numeric one.
            Assert.Equal("Timestamp,RelativeTime_ms,rpm,state", Lines(path)[2]);
            Assert.Equal(["900,idle", "900,cranking"], DataRows(path));
        }

        /// <summary>The value cells of each row, with the timestamp and relative-time columns dropped.</summary>
        private static string[] DataRows(string path) =>
            Lines(path).Skip(3).Select(line => string.Join(',', line.Split(',').Skip(2))).ToArray();
    }
}
