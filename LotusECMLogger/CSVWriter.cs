using System.Diagnostics;
using System.Globalization;
using System.Text;

/// <summary>
/// A specialized CSV writer for automotive diagnostic data logging.
/// This class manages CSV file output with automatic header detection and data formatting
/// specifically designed for LiveDataReading collections from ECM logging operations.
/// </summary>
/// <remarks>
/// The CSVWriter implements a two-phase writing strategy:
/// 1. Header Detection Phase: Scans the first N data collections to determine all available data fields
/// 2. Data Writing Phase: Writes headers once, then outputs data rows with consistent column ordering
///
/// This approach ensures that all possible data fields are captured in the CSV header,
/// even if they don't appear in the first few data samples.
///
/// Once the header is written the column set is frozen, so the steady-state path is deliberately
/// allocation-light: a name-to-column map replaces any searching, values live in a flat array
/// indexed by column, and each row is formatted into a reused buffer. This runs at the ECU's full
/// answer rate on the CSV writer thread, so per-row sorting or scanning shows up directly as
/// pressure on the logging loop behind it.
/// </remarks>
namespace LotusECMLogger
{
    internal class CSVWriter : IDisposable
    {
        private StreamWriter writer;
        private int linesRx = 0;
        private readonly int data_sample_lines = 100;

        /// <summary>Field names seen during discovery, with the latest value for each.</summary>
        private readonly Dictionary<string, double> recentValues = new();

        // ── Frozen at the moment the header row is written; all null during discovery ──

        /// <summary>Column name to its index in <see cref="rowValues"/>. Replaces searching a list.</summary>
        private Dictionary<string, int>? columnIndex;

        /// <summary>
        /// Latest value per column, carried forward between rows exactly as the discovery-phase
        /// dictionary did — a field the ECU did not answer for this batch keeps its previous value.
        /// </summary>
        private double[]? rowValues;

        /// <summary>Reused across rows so steady-state logging does not allocate a buffer per line.</summary>
        private readonly StringBuilder rowBuilder = new(1024);

        // Rows written through StreamWriter sit in its buffer until it fills or the file is closed,
        // so an abrupt end to a session — a crash, a closed lid, a pulled USB cable — loses whatever
        // is still buffered. Flush on whichever bound is reached first: the row count keeps a fast
        // ECU from buffering much, and the interval bounds how far behind real time the file can be
        // when the ECU is answering slowly. Matches HighSpeedLogService's CsvFlushEveryRows.
        private const int FlushEveryRows = 100;
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

        private int rowsSinceFlush = 0;
        private readonly Stopwatch sinceFlush = Stopwatch.StartNew();

        public CSVWriter(string filename)
        {
            LoggerPaths.EnsureParentDirectory(filename);
            this.writer = new StreamWriter(filename);
        }

        public void WriteLine(List<LiveDataReading> readings)
        {
            if (columnIndex is null)
            {
                // Discovery: collect every field name that shows up in the first N batches.
                foreach (var r in readings)
                    recentValues[r.name] = r.value_f;

                if (linesRx == data_sample_lines)
                    FreezeHeader();
            }
            else
            {
                // Steady state: one hash lookup per reading. Fields first seen after the header was
                // written are dropped, since the column set is fixed once the header row is on disk.
                foreach (var r in readings)
                {
                    if (columnIndex.TryGetValue(r.name, out int column))
                        rowValues![column] = r.value_f;
                }

                BuildRow();
                WriteRow(rowBuilder);
            }

            linesRx++;
        }

        /// <summary>
        /// Fixes the column set and writes the header row. The ordering comparer is deliberately
        /// left as the default (culture-sensitive) one so column layout stays byte-identical to
        /// previously recorded logs — this sort runs once per file, so it costs nothing to keep.
        /// The lookup dictionary is ordinal, matching the equality the old code got from
        /// List.Contains and Dictionary's default comparer.
        /// </summary>
        private void FreezeHeader()
        {
            string[] headerKeys = recentValues.Keys.OrderBy(k => k).ToArray();

            columnIndex = new Dictionary<string, int>(headerKeys.Length, StringComparer.Ordinal);
            rowValues = new double[headerKeys.Length];

            rowBuilder.Clear();
            for (int i = 0; i < headerKeys.Length; i++)
            {
                columnIndex[headerKeys[i]] = i;

                // Seed from discovery so the first data row carries real values, and so no column
                // can ever be missing a value — every header name came out of this same dictionary.
                rowValues[i] = recentValues[headerKeys[i]];

                if (i > 0)
                    rowBuilder.Append(',');
                rowBuilder.Append(headerKeys[i]);
            }

            WriteRow(rowBuilder);
        }

        /// <summary>Renders the current row into <see cref="rowBuilder"/> without allocating per cell.</summary>
        private void BuildRow()
        {
            rowBuilder.Clear();

            // Invariant culture so a comma-decimal locale cannot corrupt the field separator.
            Span<char> cell = stackalloc char[32];
            double[] values = rowValues!;

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    rowBuilder.Append(',');

                if (values[i].TryFormat(cell, out int written, "F2", CultureInfo.InvariantCulture))
                    rowBuilder.Append(cell[..written]);
                else
                    rowBuilder.Append(values[i].ToString("F2", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Writes one line and flushes when either flush bound is reached.</summary>
        private void WriteRow(StringBuilder line)
        {
            // The StringBuilder overload copies straight into the writer's buffer, so the row
            // never materialises as a separate string.
            writer.WriteLine(line);

            if (++rowsSinceFlush < FlushEveryRows && sinceFlush.Elapsed < FlushInterval)
                return;

            writer.Flush();
            rowsSinceFlush = 0;
            sinceFlush.Restart();
        }

        public void Dispose()
        {
            this.writer.Dispose();
        }
    }

}
