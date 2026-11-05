using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// How long a recording spent reading each cell of a table (step 3 of recording and replay). Each stretch of time
    /// is shared among the cells the lookup interpolates between, by the same bilinear weights, on the axes the ECU had
    /// at that moment (the recorded writes are replayed alongside). An axis with no input bound cannot place the
    /// point along it, so its share is spread evenly along that axis.
    /// </summary>
    public sealed class CellActivity
    {
        private CellActivity(A2lCalibrationItem item, double[,] seconds, double from, double to, double covered, bool spreadX, bool spreadY)
        {
            (Item, Seconds, From, To, Covered, SpreadX, SpreadY) = (item, seconds, from, to, covered, spreadX, spreadY);
            foreach (double s in seconds) Max = Math.Max(Max, s);
        }

        public A2lCalibrationItem Item { get; }
        /// <summary>Seconds at each cell, [x, y].</summary>
        public double[,] Seconds { get; }
        public double From { get; }
        public double To { get; }
        /// <summary>Seconds in the range during which every bound input had a value.</summary>
        public double Covered { get; }
        public double Max { get; }
        /// <summary>The X axis has no input, so time is spread along it.</summary>
        public bool SpreadX { get; }
        public bool SpreadY { get; }

        public double At(int x, int y) => Seconds[x, y];

        /// <summary>A cell's share of the covered time, 0 to 1.</summary>
        public double ShareAt(int x, int y) => Covered > 0 ? Seconds[x, y] / Covered : 0;

        /// <summary>
        /// The table's activity between <paramref name="from"/> and <paramref name="to"/> seconds, or null with the
        /// reason when it has none to show (no bound input, or an input that was not recorded).
        /// </summary>
        /// <param name="calibration">The calibration at the recording's start, which the recorded writes are applied to.</param>
        public static CellActivity? Compute(TuningRecording recording, TableInputs inputs, byte[] calibration, CcpBuildProfile build,
            double from, double to, out string? problem)
        {
            ArgumentNullException.ThrowIfNull(recording);
            ArgumentNullException.ThrowIfNull(inputs);
            ArgumentNullException.ThrowIfNull(calibration);
            problem = null;
            var item = inputs.Item;
            if (!inputs.Any)
            {
                problem = inputs.Problems.Count > 0 ? inputs.Problems[0] : "no axis of this table has an input bound in the A2L";
                return null;
            }
            var series = new List<ChannelSeries>();
            foreach (var source in inputs.Sources)
            {
                if (recording.SeriesOf(source.Address!.Value, source.DataType.Size()) is not { } s)
                {
                    problem = $"{source.Name} was not recorded";
                    return null;
                }
                series.Add(s);
            }

            double start = Math.Max(0, Math.Min(from, to)), end = Math.Min(recording.Duration, Math.Max(from, to));
            var seconds = new double[item.CountX, item.CountY];
            double covered = 0;
            if (end > start)
            {
                // The moments anything that matters changes: an input, or the calibration (an axis may have moved).
                var times = new List<double> { start };
                foreach (var s in series)
                    for (int i = Math.Max(0, s.IndexAt(start) + 1); i < s.Count && s.Times[i] < end; i++) times.Add(s.Times[i]);
                times.AddRange(recording.Edits.Select(e => e.Time).Where(t => t > start && t < end));
                times.Sort();
                times.Add(end);

                var replay = new ReplayCalibration(CalibrationImage.FromBytes(calibration, build), recording.Edits);
                double[]? xAxis = null, yAxis = null;
                int axesAt = -1;
                for (int i = 0; i + 1 < times.Count; i++)
                {
                    double dt = times[i + 1] - times[i];
                    if (dt <= 0) continue;
                    replay.SeekTo(times[i]);
                    if (replay.Applied != axesAt)
                    {
                        xAxis = inputs.X is null ? null : replay.Image.ReadAxisRaw(item.Axes[0], item.ByteOrder);
                        yAxis = inputs.Y is null ? null : replay.Image.ReadAxisRaw(item.Axes[1], item.ByteOrder);
                        axesAt = replay.Applied;
                    }
                    if (inputs.Sample(recording.At(times[i]), xAxis, yAxis) is not { } point) continue;
                    covered += dt;
                    Spread(seconds, point, dt);
                }
            }
            return new CellActivity(item, seconds, start, end, covered, inputs.X is null, item.Kind == A2lItemKind.Map && inputs.Y is null);
        }

        private static void Spread(double[,] seconds, OperatingPointSample point, double dt)
        {
            int countX = seconds.GetLength(0), countY = seconds.GetLength(1);
            foreach (var (x, wx) in Weights(point.X, countX))
                foreach (var (y, wy) in Weights(point.Y, countY))
                    seconds[x, y] += dt * wx * wy;
        }

        /// <summary>The cells along one axis and their weights: the two the lookup interpolates between, or all evenly when unbound.</summary>
        private static IEnumerable<(int Index, double Weight)> Weights(AxisPosition? position, int count)
        {
            if (position is not { } p)
            {
                for (int i = 0; i < count; i++) yield return (i, 1.0 / count);
                yield break;
            }
            int at = Math.Clamp(p.Index, 0, count - 1);
            if (p.Fraction <= 0 || at + 1 >= count)
            {
                yield return (at, 1);
                yield break;
            }
            yield return (at, 1 - p.Fraction);
            yield return (at + 1, p.Fraction);
        }
    }
}
