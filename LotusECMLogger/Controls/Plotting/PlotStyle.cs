using System.Globalization;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>Colours and sizes shared by the graph views and the calibration grid (RaiderLite's default preset).</summary>
internal static class PlotStyle
{
    public static readonly Color Background = Color.White;
    public static readonly Color Frame = Color.FromArgb(190, 194, 200);
    public static readonly Color GridLine = Color.FromArgb(234, 236, 239);
    public static readonly Color Label = Color.FromArgb(90, 94, 102);
    public static readonly Color Title = Color.FromArgb(50, 54, 62);
    public static readonly Color Curve = Color.FromArgb(0, 103, 192);
    public static readonly Color Original = Color.FromArgb(150, 150, 150);
    public static readonly Color Crosshair = Color.FromArgb(150, 150, 160);
    public static readonly Color ReadoutBack = Color.FromArgb(245, 255, 255, 255);

    /// <summary>Low and high ends of the heat map, the selection, and the borders of raised and lowered cells.</summary>
    public static readonly Color MinColor = Color.FromArgb(153, 153, 255);
    public static readonly Color MaxColor = Color.FromArgb(255, 102, 102);
    public static readonly Color Selection = Color.FromArgb(27, 161, 226);
    public static readonly Color Increased = Color.FromArgb(255, 0, 0);
    public static readonly Color Decreased = Color.FromArgb(0, 0, 255);
    public static readonly Color Axis = Color.FromArgb(242, 242, 242);

    /// <summary>The grid's min→max colour at <paramref name="t"/> (0..1).</summary>
    public static Color Heat(double t) => ColorScaler.GetScaledColor(t, MinColor, MaxColor);

    /// <summary>A darker heat colour that reads well as a line on white.</summary>
    public static Color HeatLine(double t)
    {
        var c = Heat(t);
        return Color.FromArgb((int)(c.R * 0.72), (int)(c.G * 0.72), (int)(c.B * 0.72));
    }

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}

/// <summary>A point under the mouse: the cell (in the plotted data's x, y) and the text describing it.</summary>
internal sealed record PlotHit(int I, int J, PointF Location, string Readout);

/// <summary>Formats values the way the grid does.</summary>
internal static class PlotText
{
    public static string Value(GraphData data, double v) => v.ToString("F" + data.ValueDecimals, CultureInfo.CurrentCulture);

    public static string Tick(double v, double step) => v.ToString(AxisTicks.FormatFor(step), CultureInfo.CurrentCulture);

    /// <summary>The hover readout, e.g. "rpm: 4000 / ms: 50.0 (raw 10)".</summary>
    public static string Readout(GraphData data, int i, int j)
    {
        var parts = new List<string> { $"{data.X.Title}: {data.X.Labels[i]}" };
        if (data.Y != null)
            parts.Add($"{data.Y.Title}: {data.Y.Labels[j]}");
        parts.Add($"{Value(data, data.Values[i, j])} {data.ValueTitle}   (raw {data.Raw[i, j].ToString(CultureInfo.CurrentCulture)})");
        return string.Join("\n", parts);
    }
}
