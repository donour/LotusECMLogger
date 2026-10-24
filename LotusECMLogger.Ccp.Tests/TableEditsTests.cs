using System.Globalization;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

public sealed class TableEditsTests
{
    private static double[,] Grid(int width, int height, Func<int, int, double> value)
    {
        var grid = new double[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[y, x] = value(x, y);
        return grid;
    }

    private static double At(IReadOnlyList<CellValue> cells, int x, int y) => cells.Single(c => c.X == x && c.Y == y).Value;

    [Theory]
    [InlineData(TableOperation.Set, 5, 5)]
    [InlineData(TableOperation.Add, -2, 8)]
    [InlineData(TableOperation.Multiply, 1.5, 15)]
    public void ArithmeticAppliesToTheSelectedCellsOnly(TableOperation operation, double operand, double expected)
    {
        var values = Grid(3, 3, (_, _) => 10);
        var result = TableEdits.Arithmetic(values, [(1, 1), (2, 0)], operation, operand);
        Assert.Equal(2, result.Count);
        Assert.Equal(expected, At(result, 1, 1), 9);
    }

    [Fact]
    public void InterpolatesAlongARowByAxisPosition()
    {
        var values = Grid(4, 1, (x, _) => x == 0 ? 0 : x == 3 ? 100 : 7);
        var byIndex = TableEdits.Interpolate(values, new CellRange(0, 0, 3, 0));
        Assert.Equal([0, 100 / 3.0, 200 / 3.0, 100], byIndex.Select(c => c.Value), new ToleranceComparer());
        // Axis points 0, 10, 20, 100: the middle cells sit a tenth and a fifth of the way along.
        var byAxis = TableEdits.Interpolate(values, new CellRange(0, 0, 3, 0), xAxis: [0, 10, 20, 100]);
        Assert.Equal([0, 10, 20, 100], byAxis.Select(c => c.Value), new ToleranceComparer());
        // A repeated axis point falls back to even spacing.
        Assert.Equal(byIndex.Select(c => c.Value), TableEdits.Interpolate(values, new CellRange(0, 0, 3, 0), xAxis: [0, 10, 10, 100])
            .Select(c => c.Value), new ToleranceComparer());
    }

    [Fact]
    public void InterpolatesABlockBilinearlyFromItsCorners()
    {
        var values = Grid(3, 3, (x, y) => (x, y) switch { (0, 0) => 0, (2, 0) => 20, (0, 2) => 40, (2, 2) => 60, _ => -1 });
        var result = TableEdits.Interpolate(values, new CellRange(0, 0, 2, 2));
        Assert.Equal(30, At(result, 1, 1), 9);
        Assert.Equal(10, At(result, 1, 0), 9);
        Assert.Equal(50, At(result, 1, 2), 9);
        Assert.Equal(60, At(result, 2, 2), 9);
    }

    [Fact]
    public void SmoothsWithTheNeighboursAroundEachCell()
    {
        var values = Grid(3, 3, (x, y) => x == 1 && y == 1 ? 90 : 0);
        Assert.Equal(10, At(TableEdits.Smooth(values, [(1, 1)]), 1, 1), 9);
        Assert.Equal(22.5, At(TableEdits.Smooth(values, [(0, 0)]), 0, 0), 9); // corner: 4 cells
        var curve = Grid(3, 1, (x, _) => x * 3);
        Assert.Equal(3, At(TableEdits.Smooth(curve, [(1, 0)]), 1, 0), 9);
    }

    [Fact]
    public void ParsesSpreadsheetBlocks()
    {
        var block = TableEdits.ParseBlock("1\t2.5\t3\r\n4\t5\t-6\r\n", CultureInfo.InvariantCulture);
        Assert.Equal((2, 3), (block.GetLength(0), block.GetLength(1)));
        Assert.Equal(-6, block[1, 2]);
        var german = TableEdits.ParseBlock("1,5\t2\n", CultureInfo.GetCultureInfo("de-DE"));
        Assert.Equal(1.5, german[0, 0]);
        Assert.Equal(2, TableEdits.ParseBlock("1,2,3", CultureInfo.InvariantCulture)[0, 1]);
        Assert.Contains("row 1, column 2", Assert.Throws<FormatException>(() => TableEdits.ParseBlock("1\tx")).Message);
        Assert.Throws<FormatException>(() => TableEdits.ParseBlock("1\t2\n3"));
    }

    [Fact]
    public void FormatsBlocksAndReadsA2lFormats()
    {
        var values = Grid(2, 2, (x, y) => x + y * 10 + 0.26);
        Assert.Equal("10.3\t11.3\r\n", TableEdits.FormatBlock(values, new CellRange(0, 1, 1, 1), 1)
            .Replace(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, "."));
        Assert.Equal(3, TableEdits.Decimals("%11.3"));
        Assert.Equal(0, TableEdits.Decimals("%8.0"));
        Assert.Null(TableEdits.Decimals(""));
    }

    private sealed class ToleranceComparer : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-9;
        public int GetHashCode(double value) => 0;
    }
}
