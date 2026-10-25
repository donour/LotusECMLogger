using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LotusECMLogger.Services.Tuning
{
    public enum TableOperation { Set, Add, Multiply }

    /// <summary>A rectangle of cells, inclusive: X is the column (first axis), Y the row.</summary>
    public readonly record struct CellRange(int X0, int Y0, int X1, int Y1)
    {
        public int Width => X1 - X0 + 1;
        public int Height => Y1 - Y0 + 1;

        public IEnumerable<(int X, int Y)> Cells()
        {
            for (int y = Y0; y <= Y1; y++)
                for (int x = X0; x <= X1; x++)
                    yield return (x, y);
        }

        public static CellRange Bounding(IEnumerable<(int X, int Y)> cells)
        {
            var list = cells.ToList();
            if (list.Count == 0) throw new ArgumentException("No cells selected.", nameof(cells));
            return new(list.Min(c => c.X), list.Min(c => c.Y), list.Max(c => c.X), list.Max(c => c.Y));
        }
    }

    /// <summary>
    /// The editor's selection operations, on physical values held as <c>values[y, x]</c>. Each returns the new values
    /// for <see cref="CalibrationImage.SetPhysical"/>, which rounds them to raw and applies them as one undo step.
    /// </summary>
    public static class TableEdits
    {
        public static IReadOnlyList<CellValue> Arithmetic(double[,] values, IEnumerable<(int X, int Y)> cells,
            TableOperation operation, double operand) =>
            cells.Select(c => new CellValue(c.X, c.Y, operation switch
            {
                TableOperation.Set => operand,
                TableOperation.Add => values[c.Y, c.X] + operand,
                _ => values[c.Y, c.X] * operand,
            })).ToList();

        /// <summary>
        /// Fills the range from its edges: linearly along a single row or column, bilinearly from the four corners
        /// otherwise. Positions come from the axis values when they increase across the range, else from cell indices.
        /// </summary>
        public static IReadOnlyList<CellValue> Interpolate(double[,] values, CellRange range,
            IReadOnlyList<double>? xAxis = null, IReadOnlyList<double>? yAxis = null)
        {
            if (range.Width * range.Height < 2) throw new ArgumentException("Select a row, a column or a block to interpolate across.");
            var px = Positions(xAxis, range.X0, range.X1);
            var py = Positions(yAxis, range.Y0, range.Y1);
            double Fx(int x) => range.Width == 1 ? 0 : (px[x - range.X0] - px[0]) / (px[^1] - px[0]);
            double Fy(int y) => range.Height == 1 ? 0 : (py[y - range.Y0] - py[0]) / (py[^1] - py[0]);
            double a = values[range.Y0, range.X0], b = values[range.Y0, range.X1];
            double c = values[range.Y1, range.X0], d = values[range.Y1, range.X1];
            return range.Cells().Select(cell =>
            {
                double fx = Fx(cell.X), fy = Fy(cell.Y);
                double top = a + (b - a) * fx, bottom = c + (d - c) * fx;
                return new CellValue(cell.X, cell.Y, top + (bottom - top) * fy);
            }).ToList();
        }

        /// <summary>Fills each row of the range linearly between its first and last cell (Horizontal interpolate).</summary>
        public static IReadOnlyList<CellValue> InterpolateRows(double[,] values, CellRange range, IReadOnlyList<double>? xAxis = null)
        {
            if (range.Width < 2) throw new ArgumentException("Select at least two cells across a row.");
            var cells = new List<CellValue>();
            for (int y = range.Y0; y <= range.Y1; y++)
                cells.AddRange(Interpolate(values, range with { Y0 = y, Y1 = y }, xAxis, null));
            return cells;
        }

        /// <summary>Fills each column of the range linearly between its first and last cell (Vertical interpolate).</summary>
        public static IReadOnlyList<CellValue> InterpolateColumns(double[,] values, CellRange range, IReadOnlyList<double>? yAxis = null)
        {
            if (range.Height < 2) throw new ArgumentException("Select at least two cells down a column.");
            var cells = new List<CellValue>();
            for (int x = range.X0; x <= range.X1; x++)
                cells.AddRange(Interpolate(values, range with { X0 = x, X1 = x }, null, yAxis));
            return cells;
        }

        /// <summary>
        /// The physical size of one raw count, the natural fine step: 5 for a 5 ms-per-count table. Verbal tables and
        /// conversions that are not linear step by 1.
        /// </summary>
        public static double RawStep(A2l.A2lCompuMethod conversion)
        {
            if (conversion.Problem is not null || conversion.Kind == A2l.A2lConversionKind.VerbalTable) return 1;
            double step = Math.Abs(conversion.ToPhysical(1) - conversion.ToPhysical(0));
            return double.IsFinite(step) && step > 0 ? step : 1;
        }

        private static double[] Positions(IReadOnlyList<double>? axis, int from, int to)
        {
            var indices = Enumerable.Range(from, to - from + 1).Select(i => (double)i).ToArray();
            if (axis is null || to >= axis.Count) return indices;
            var points = Enumerable.Range(from, to - from + 1).Select(i => axis[i]).ToArray();
            for (int i = 1; i < points.Length; i++)
                if (!(points[i] > points[i - 1])) return indices;
            return points;
        }

        /// <summary>Replaces each selected cell with the mean of itself and its neighbours (3 × 3, or 3 along a curve).</summary>
        public static IReadOnlyList<CellValue> Smooth(double[,] values, IEnumerable<(int X, int Y)> cells)
        {
            int height = values.GetLength(0), width = values.GetLength(1);
            return cells.Select(c =>
            {
                double sum = 0;
                int count = 0;
                for (int y = Math.Max(0, c.Y - 1); y <= Math.Min(height - 1, c.Y + 1); y++)
                    for (int x = Math.Max(0, c.X - 1); x <= Math.Min(width - 1, c.X + 1); x++)
                    {
                        sum += values[y, x];
                        count++;
                    }
                return new CellValue(c.X, c.Y, sum / count);
            }).ToList();
        }

        /// <summary>
        /// Reads a block copied from a spreadsheet: rows on lines, cells separated by tabs (or commas when there are no
        /// tabs). Numbers in the invariant culture or the current one. Returns <c>[row, column]</c>.
        /// </summary>
        public static double[,] ParseBlock(string text, CultureInfo? culture = null)
        {
            var numbers = culture ?? CultureInfo.CurrentCulture;
            var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) throw new FormatException("The clipboard holds no numbers.");
            bool tabs = lines.Any(l => l.Contains('\t'));
            // A comma is a separator only when the culture's decimal mark is not a comma.
            char separator = tabs ? '\t' : numbers.NumberFormat.NumberDecimalSeparator == "," ? ';' : ',';
            var rows = lines.Select(l => l.Split(separator).Select(s => s.Trim()).ToArray()).ToList();
            int width = rows.Max(r => r.Length);
            var block = new double[rows.Count, width];
            for (int y = 0; y < rows.Count; y++)
            {
                if (rows[y].Length != width) throw new FormatException($"Row {y + 1} has {rows[y].Length} cells; expected {width}.");
                for (int x = 0; x < width; x++)
                    block[y, x] = ParseNumber(rows[y][x]) ?? throw new FormatException($"'{rows[y][x]}' (row {y + 1}, column {x + 1}) is not a number.");
            }
            return block;

            double? ParseNumber(string s) =>
                double.TryParse(s, NumberStyles.Float, numbers, out double v) ? v
                : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : null;
        }

        /// <summary>The range as tab-separated rows, for pasting into a spreadsheet.</summary>
        public static string FormatBlock(double[,] values, CellRange range, int decimals)
        {
            var text = new StringBuilder();
            for (int y = range.Y0; y <= range.Y1; y++)
            {
                for (int x = range.X0; x <= range.X1; x++)
                {
                    if (x > range.X0) text.Append('\t');
                    text.Append(values[y, x].ToString("F" + decimals, CultureInfo.CurrentCulture));
                }
                text.Append("\r\n");
            }
            return text.ToString();
        }

        private static readonly Regex FormatRe = new(@"%\s*\d*\.(\d+)", RegexOptions.Compiled);

        /// <summary>Decimal places from an A2L FORMAT such as "%8.3", or null when it gives none.</summary>
        public static int? Decimals(string a2lFormat) =>
            FormatRe.Match(a2lFormat) is { Success: true } m ? Math.Min(6, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : null;
    }
}
