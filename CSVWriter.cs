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

        public CSVWriter(string filename)
        {
            this.writer = new StreamWriter(filename);
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
                writer.WriteLine(string.Join(",", keys.Select(k => recentValues.ContainsKey(k) ? recentValues[k].ToString("F2") : "N/A")));
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
                    writer.WriteLine(string.Join(",", getSortedHeaders()));
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
