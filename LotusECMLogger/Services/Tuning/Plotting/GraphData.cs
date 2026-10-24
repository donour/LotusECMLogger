using System.Globalization;
using LotusECMLogger.Services.A2l;

namespace LotusECMLogger.Services.Tuning.Plotting;

/// <summary>The live operating point on a graph, in axis positions, with the table's value there.</summary>
public sealed record GraphCursor(double? X, double? Y, double? Value);

/// <summary>One axis of a graph: where each column or row sits, and how to label it.</summary>
public sealed class GraphAxis
{
    public required string Title { get; init; }

    /// <summary>Position of each entry: the physical axis value, or the index when the axis can't be used.</summary>
    public required double[] Positions { get; init; }

    /// <summary>Display text of each entry.</summary>
    public required string[] Labels { get; init; }

    /// <summary>True when positions are evenly spaced indices rather than axis values.</summary>
    public bool Categorical { get; init; }

    public int Count => Positions.Length;
}

/// <summary>
/// A curve (one axis) or map (two axes) ready to plot, in physical units. Values are indexed [x, y]; a curve has a
/// single row (y = 0). Adapted from RaiderLite's GraphData, reading an A2L item from a calibration image.
/// </summary>
public sealed class GraphData
{
    public required string Title { get; init; }

    public required GraphAxis X { get; init; }

    /// <summary>The second axis of a map; null for a curve.</summary>
    public GraphAxis? Y { get; init; }

    /// <summary>Title of the value (vertical) axis, e.g. "ms".</summary>
    public required string ValueTitle { get; init; }

    public required int ValueDecimals { get; init; }

    public required double[,] Values { get; init; }

    public required double[,] Raw { get; init; }

    /// <summary>Values in the file as opened where any cell has changed; otherwise null.</summary>
    public double[,]? Originals { get; init; }

    public bool IsMap => Y != null;

    public int SizeX => Values.GetLength(0);

    public int SizeY => Values.GetLength(1);

    /// <summary>Smallest and largest finite value across values and originals.</summary>
    public (double Min, double Max) ValueRange
    {
        get
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (var grid in new[] { Values, Originals })
            {
                if (grid == null)
                    continue;
                foreach (var v in grid)
                {
                    if (!double.IsFinite(v))
                        continue;
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }
            return min <= max ? (min, max) : (0, 1);
        }
    }

    /// <summary>Whether an item can be graphed: maps, curves, axes and value blocks; not single values or text.</summary>
    public static bool CanGraph(A2lCalibrationItem item) =>
        item.Kind is A2lItemKind.Map or A2lItemKind.Curve or A2lItemKind.AxisPts or A2lItemKind.ValBlk && item.ElementCount > 1;

    /// <summary>The graph of an item as the image holds it now, or null if it can't be graphed.</summary>
    public static GraphData? From(CalibrationImage image, A2lCalibrationItem item)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(item);
        if (!CanGraph(item))
            return null;
        int sx = item.CountX, sy = item.CountY;
        var values = new double[sx, sy];
        var raw = new double[sx, sy];
        var originals = new double[sx, sy];
        bool changed = false;
        for (int i = 0; i < sx; i++)
        {
            for (int j = 0; j < sy; j++)
            {
                raw[i, j] = image.ReadRaw(item, i, j);
                values[i, j] = item.Conversion.ToPhysical(raw[i, j]);
                double original = image.ReadOriginalRaw(item, i, j);
                originals[i, j] = item.Conversion.ToPhysical(original);
                changed |= original != raw[i, j];
            }
        }
        var x = item.Kind is A2lItemKind.Map or A2lItemKind.Curve ? AxisFrom(image, item, 0) : IndexAxis(sx);
        var y = item.Kind == A2lItemKind.Map ? AxisFrom(image, item, 1) : null;
        return new GraphData
        {
            Title = item.Name,
            X = x,
            Y = y,
            ValueTitle = item.Conversion.Unit.Length > 0 ? item.Conversion.Unit : "value",
            ValueDecimals = TableEdits.Decimals(item.Conversion.Format) ?? 3,
            Values = values,
            Raw = raw,
            Originals = changed ? originals : null,
        };
    }

    /// <summary>An axis's short name, e.g. "coolant_tmp" for CAL_x_X_coolant_tmp, with its unit.</summary>
    public static string AxisTitle(A2lAxisLayout axis)
    {
        string name = axis.AxisPtsName ?? axis.InputQuantity ?? "";
        foreach (var marker in new[] { "_X_", "_Y_" })
        {
            int at = name.LastIndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                name = name[(at + marker.Length)..];
                break;
            }
        }
        string unit = axis.Conversion.Unit;
        return name.Length == 0 ? unit : unit.Length > 0 ? $"{name} ({unit})" : name;
    }

    private static GraphAxis AxisFrom(CalibrationImage image, A2lCalibrationItem item, int index)
    {
        var axis = item.Axes[index];
        int count = index == 0 ? item.CountX : item.CountY;
        var positions = image.ReadAxisPhysical(axis, item.ByteOrder);
        int decimals = TableEdits.Decimals(axis.Conversion.Format) ?? 2;
        var labels = positions.Select(p => p.ToString("F" + decimals, CultureInfo.CurrentCulture)).ToArray();
        bool categorical = positions.Length != count || positions.Any(p => !double.IsFinite(p)) || positions.Distinct().Count() < Math.Min(2, count);
        if (categorical)
            positions = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        return new GraphAxis { Title = AxisTitle(axis), Positions = positions, Labels = labels.Length == count ? labels : IndexLabels(count), Categorical = categorical };
    }

    private static string[] IndexLabels(int count) =>
        Enumerable.Range(0, count).Select(i => i.ToString(CultureInfo.CurrentCulture)).ToArray();

    private static GraphAxis IndexAxis(int count) => new()
    {
        Title = "index",
        Positions = Enumerable.Range(0, count).Select(i => (double)i).ToArray(),
        Labels = IndexLabels(count),
        Categorical = true,
    };

    /// <summary>
    /// Where an operating point sits on this graph: each bound axis's position, interpolated between its points, and
    /// the table's value there once every axis of the table is bound.
    /// </summary>
    public GraphCursor? CursorAt(OperatingPointSample sample)
    {
        static double Position(GraphAxis axis, AxisPosition p)
        {
            int i = Math.Clamp(p.Index, 0, axis.Count - 1), j = Math.Min(i + 1, axis.Count - 1);
            return axis.Positions[i] + (axis.Positions[j] - axis.Positions[i]) * p.Fraction;
        }
        double? x = sample.X is { } px ? Position(X, px) : null;
        double? y = Y is not null && sample.Y is { } py ? Position(Y, py) : null;
        if (x is null && y is null) return null;
        double? value = sample.X is { } vx && (Y is null || sample.Y is not null)
            ? OperatingPoint.Interpolate(Values, vx, Y is null ? null : sample.Y) : null;
        return new GraphCursor(x, y, value);
    }

    /// <summary>The same map with X and Y swapped (one line per column instead of per row).</summary>
    public GraphData Transposed()
    {
        if (Y == null)
            return this;
        static double[,] Swap(double[,] a)
        {
            var r = new double[a.GetLength(1), a.GetLength(0)];
            for (int i = 0; i < a.GetLength(0); i++)
                for (int j = 0; j < a.GetLength(1); j++)
                    r[j, i] = a[i, j];
            return r;
        }
        return new GraphData
        {
            Title = Title,
            X = Y,
            Y = X,
            ValueTitle = ValueTitle,
            ValueDecimals = ValueDecimals,
            Values = Swap(Values),
            Raw = Swap(Raw),
            Originals = Originals != null ? Swap(Originals) : null,
        };
    }
}
