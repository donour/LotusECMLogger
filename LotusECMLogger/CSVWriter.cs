using System.Diagnostics;

/// <summary>
/// A specialized CSV writer for automotive diagnostic data logging.
/// This class manages CSV file output with automatic header detection and data formatting
/// specifically designed for LiveDataReading collections from ECM logging operations.
/// </summary>
/// <remarks>
/// The CSVWriter implements a two-phase writing strategy:
/// 1. Header Detection Phase: Scans the first 20 data collections to determine all available data fields
/// 2. Data Writing Phase: Writes headers once, then outputs data rows with consistent column ordering
/// 
/// This approach ensures that all possible data fields are captured in the CSV header,
/// even if they don't appear in the first few data samples.
/// </remarks>
namespace LotusECMLogger
{
    internal class CSVWriter : IDisposable
    {
        private StreamWriter writer;
        private int linesRx = 0;
        private Dictionary<string, double> recentValues = new();
        private readonly int data_sample_lines = 100;

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

        /// <summary>Writes one line and flushes when either flush bound is reached.</summary>
        private void WriteRow(string line)
        {
            writer.WriteLine(line);

            if (++rowsSinceFlush < FlushEveryRows && sinceFlush.Elapsed < FlushInterval)
                return;

            writer.Flush();
            rowsSinceFlush = 0;
            sinceFlush.Restart();
        }

        public List<String> getSortedHeaders()
        {
            return recentValues.Keys.OrderBy(k => k).ToList();
        }

        public void WriteLine(List<LiveDataReading> readings)
        {

            if (linesRx > data_sample_lines)
            {
                var keys = getSortedHeaders();
                foreach (var r in readings)
                {
                    if (keys.Contains(r.name))
                    {
                        recentValues[r.name] = r.value_f;
                    }
                }
                WriteRow(string.Join(",", keys.Select(k => recentValues.ContainsKey(k) ? recentValues[k].ToString("F2") : "N/A")));
            }
            else
            {
                // scan N lines for headers before writing.
                foreach (var r in readings)
                {
                    recentValues[r.name] = r.value_f;
                }
                if (linesRx == data_sample_lines)
                {
                    WriteRow(string.Join(",", getSortedHeaders()));
                }

            }
            linesRx++;

        }
        public void Dispose()
        {
            this.writer.Dispose();
        }
    }

}
