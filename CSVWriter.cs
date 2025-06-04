
namespace LotusECMLogger
{
    internal class CSVWriter : IDisposable
    {
        private StreamWriter writer;
        private int linesRx = 0;
        private Dictionary<string, double> recentValues = new();

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

            if (linesRx > 20)
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
                // scan 20 lines for headers before writing.
                foreach (var r in readings)
                {
                    recentValues[r.name] = r.value_f;
                }
                if (linesRx == 20)
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
