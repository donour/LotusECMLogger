using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>
/// Line graph: a curve (one series), or a map drawn as one line per row of its Y axis.
/// Plots against real axis values; text (static) axes are evenly spaced.
/// </summary>
internal sealed class LinesView(GraphData data, float scale, Func<int, int, bool> isSelected)
{
    private readonly List<(PointF P, int I, int J)> _points = [];

    /// <summary>The live operating point, in this view's axis positions.</summary>
    public GraphCursor? Cursor { get; set; }
    private RectangleF _plot;

    private float S(float v) => v * scale;

    public void Draw(IPlotRenderer r, RectangleF bounds, PlotHit? hover)
    {
        _points.Clear();
        var (vmin, vmax) = data.ValueRange;
        var yTicks = AxisTicks.Compute(vmin, vmax);
        double xmin = data.X.Positions.Min(), xmax = data.X.Positions.Max();
        // Text axes are labelled per entry; numeric axes get rounded ticks.
        AxisTicks.Ticks? xTicks = data.X.Categorical ? null : AxisTicks.Compute(xmin, xmax, 7);
        if (xTicks is { } nice)
            (xmin, xmax) = (nice.Min, nice.Max);
        else if (xmin == xmax)
            (xmin, xmax) = (xmin - 0.5, xmax + 0.5);
        else
            (xmin, xmax) = (xmin - 0.3, xmax + 0.3);

        // Margins sized to the labels.
        float lineHeight = r.MeasureText("0").Height;
        float yLabelWidth = yTicks.Values.Max(v => r.MeasureText(PlotText.Tick(v, yTicks.Step)).Width);
        var rowAxis = data.Y;
        float legendWidth = rowAxis != null ? LegendWidth(r, rowAxis) : 0;
        float left = bounds.Left + S(6) + lineHeight + S(4) + yLabelWidth + S(6);
        float right = bounds.Right - S(12) - legendWidth;
        float top = bounds.Top + S(10);
        float bottom = bounds.Bottom - S(6) - lineHeight - S(4) - lineHeight - S(6);
        if (right - left < S(40) || bottom - top < S(30))
            return;
        _plot = RectangleF.FromLTRB(left, top, right, bottom);

        float X(double v) => (float)(left + (v - xmin) / (xmax - xmin) * (right - left));
        float Y(double v) => (float)(bottom - (v - yTicks.Min) / (yTicks.Max - yTicks.Min) * (bottom - top));

        // Grid lines and tick labels.
        foreach (var v in yTicks.Values)
        {
            float y = Y(v);
            r.Line(new PointF(left, y), new PointF(right, y), PlotStyle.GridLine, 1);
            r.Text(PlotText.Tick(v, yTicks.Step), new PointF(left - S(6), y), PlotStyle.Label, TextAnchor.MiddleRight);
        }
        if (xTicks is { } xt)
        {
            foreach (var v in xt.Values)
            {
                float x = X(v);
                r.Line(new PointF(x, top), new PointF(x, bottom), PlotStyle.GridLine, 1);
                r.Text(PlotText.Tick(v, xt.Step), new PointF(x, bottom + S(4)), PlotStyle.Label, TextAnchor.TopCenter);
            }
        }
        else
        {
            float widest = data.X.Labels.Max(l => r.MeasureText(l).Width) + S(8);
            int every = Math.Max(1, (int)Math.Ceiling(widest / ((right - left) / Math.Max(1, data.X.Count))));
            for (int i = 0; i < data.X.Count; i += every)
            {
                float x = X(data.X.Positions[i]);
                r.Line(new PointF(x, top), new PointF(x, bottom), PlotStyle.GridLine, 1);
                r.Text(data.X.Labels[i], new PointF(x, bottom + S(4)), PlotStyle.Label, TextAnchor.TopCenter);
            }
        }
        r.DrawRect(_plot, PlotStyle.Frame, 1);

        // Axis titles.
        r.Text(data.X.Title, new PointF((left + right) / 2, bounds.Bottom - S(6)), PlotStyle.Title, TextAnchor.BottomCenter);
        r.VerticalText(data.ValueTitle, new PointF(bounds.Left + S(6) + lineHeight / 2, (top + bottom) / 2), PlotStyle.Title);

        r.PushClip(RectangleF.Inflate(_plot, S(6), S(6)));
        int rows = data.SizeY;
        var line = new PointF[data.SizeX];
        for (int j = 0; j < rows; j++)
        {
            double t = rows == 1 ? 0 : (double)j / (rows - 1);
            var color = data.IsMap ? PlotStyle.HeatLine(t) : PlotStyle.Curve;

            if (!data.IsMap)
            {
                // Behind the curve: the file's values, where they differ.
                if (data.Originals != null)
                    DrawSeries(r, data.Originals, j, i => X(data.X.Positions[i]), Y, PlotStyle.Original, S(1.5f), dashed: true);
            }

            int n = 0;
            for (int i = 0; i < data.SizeX; i++)
            {
                double v = data.Values[i, j];
                if (!double.IsFinite(v))
                    continue;
                line[n++] = new PointF(X(data.X.Positions[i]), Y(v));
                _points.Add((line[n - 1], i, j));
            }
            r.Polyline(line.AsSpan(0, n), color, S(data.IsMap ? 1.6f : 2f));
            float radius = S(data.IsMap ? 2.2f : 3f);
            for (int k = 0; k < n; k++)
                r.FillCircle(line[k], radius, color);
        }

        // Selected cells.
        foreach (var (p, i, j) in _points)
        {
            if (isSelected(i, j))
                r.DrawCircle(p, S(5), PlotStyle.Selection, S(2));
        }
        if (Cursor is { } cursor && cursor.X is { } cx)
        {
            r.Line(new PointF(X(cx), top), new PointF(X(cx), bottom), PlotStyle.Cursor, S(1.5f), dashed: true);
            if (cursor.Value is { } v)
            {
                var at = new PointF(X(cx), Y(v));
                r.FillCircle(at, S(5), PlotStyle.Cursor);
                r.DrawCircle(at, S(5), Color.White, S(1.5f));
            }
        }
        r.PopClip();

        if (rowAxis != null)
            DrawLegend(r, rowAxis, new RectangleF(right + S(14), top, legendWidth - S(4), bottom - top));

        if (hover != null)
            DrawHover(r, hover);
    }

    /// <summary>A secondary series (originals or compared values); <paramref name="xAt"/> maps a column index to x.</summary>
    private static void DrawSeries(IPlotRenderer r, double[,] values, int j, Func<int, float> xAt, Func<double, float> y,
        Color color, float width, bool dashed)
    {
        var pts = new List<PointF>();
        for (int i = 0; i < values.GetLength(0); i++)
        {
            if (double.IsFinite(values[i, j]))
                pts.Add(new PointF(xAt(i), y(values[i, j])));
        }
        r.Polyline(pts.ToArray(), color, width, dashed);
    }

    private float LegendWidth(IPlotRenderer r, GraphAxis y)
    {
        float labels = Math.Max(r.MeasureText(y.Labels[0]).Width, r.MeasureText(y.Labels[^1]).Width);
        // Bar, end labels, then the title running vertically.
        return S(12) + S(5) + labels + S(6) + r.MeasureText(y.Title).Height + S(4);
    }

    /// <summary>Colour bar for the rows of a map: first row at the bottom, last at the top.</summary>
    private void DrawLegend(IPlotRenderer r, GraphAxis y, RectangleF area)
    {
        float barWidth = S(12);
        var bar = new RectangleF(area.Left, area.Top, barWidth, Math.Max(S(20), area.Height));
        int steps = Math.Max(2, data.SizeY);
        for (int k = 0; k < steps; k++)
        {
            float y0 = bar.Bottom - (k + 1) * bar.Height / steps;
            r.FillRect(new RectangleF(bar.Left, y0, bar.Width, bar.Height / steps + 0.5f), PlotStyle.HeatLine((double)k / (steps - 1)));
        }
        r.DrawRect(bar, PlotStyle.Frame, 1);
        r.Text(y.Labels[^1], new PointF(bar.Right + S(5), bar.Top), PlotStyle.Label, TextAnchor.TopLeft);
        r.Text(y.Labels[0], new PointF(bar.Right + S(5), bar.Bottom), PlotStyle.Label, TextAnchor.BottomLeft);
        float lineHeight = r.MeasureText(y.Title).Height;
        r.VerticalText(y.Title, new PointF(area.Right - lineHeight / 2, bar.Top + bar.Height / 2), PlotStyle.Title);
    }

    private void DrawHover(IPlotRenderer r, PlotHit hover)
    {
        var p = hover.Location;
        r.Line(new PointF(_plot.Left, p.Y), new PointF(_plot.Right, p.Y), PlotStyle.Crosshair, 1, dashed: true);
        r.Line(new PointF(p.X, _plot.Top), new PointF(p.X, _plot.Bottom), PlotStyle.Crosshair, 1, dashed: true);
        r.DrawCircle(p, S(6), PlotStyle.Title, S(1.5f));
        Readout.Draw(r, hover.Readout, p, _plot, scale);
    }

    public PlotHit? HitTest(PointF location)
    {
        float best = (10 * scale) * (10 * scale);
        (PointF P, int I, int J)? hit = null;
        foreach (var pt in _points)
        {
            float dx = pt.P.X - location.X, dy = pt.P.Y - location.Y;
            float d = dx * dx + dy * dy;
            if (d < best)
            {
                best = d;
                hit = pt;
            }
        }
        return hit is { } h ? new PlotHit(h.I, h.J, h.P, PlotText.Readout(data, h.I, h.J)) : null;
    }
}

/// <summary>The small box that shows the values under the mouse.</summary>
internal static class Readout
{
    public static void Draw(IPlotRenderer r, string text, PointF near, RectangleF area, float scale)
    {
        var lines = text.Split('\n');
        float w = lines.Max(l => r.MeasureText(l).Width) + 12 * scale;
        float lh = r.MeasureText("0").Height;
        float h = lines.Length * lh + 8 * scale;
        float x = near.X + 12 * scale;
        float y = near.Y - h - 8 * scale;
        if (x + w > area.Right)
            x = near.X - w - 12 * scale;
        if (y < area.Top)
            y = near.Y + 12 * scale;
        var box = new RectangleF(x, y, w, h);
        r.FillRect(box, PlotStyle.ReadoutBack);
        r.DrawRect(box, PlotStyle.Frame, 1);
        for (int k = 0; k < lines.Length; k++)
            r.Text(lines[k], new PointF(x + 6 * scale, y + 4 * scale + k * lh), PlotStyle.Title, TextAnchor.TopLeft, bold: k == lines.Length - 1);
    }
}
