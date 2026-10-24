using System.Numerics;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>
/// 3D surface of a map: heat-coloured quads painted back to front (no depth buffer needed for a
/// height field), on a floor grid with a value axis. Rotated with the mouse via <see cref="SurfaceCamera"/>.
/// </summary>
internal sealed class SurfaceView
{
    private readonly GraphData data;
    private readonly GraphAxis yAxis;
    private readonly SurfaceCamera camera;
    private readonly float scale;
    private readonly Func<int, int, bool> isSelected;
    private readonly List<(PointF P, int I, int J)> _points = [];
    private RectangleF _area;

    private float S(float v) => v * scale;

    private SurfaceView(GraphData data, GraphAxis yAxis, SurfaceCamera camera, float scale, Func<int, int, bool> isSelected)
    {
        this.isSelected = isSelected;
        this.data = data;
        this.yAxis = yAxis;
        this.camera = camera;
        this.scale = scale;
    }

    /// <summary>Whether a surface can be drawn: a map with at least two entries on each axis.</summary>
    public static bool CanShow(GraphData data) => data.Y != null && data.SizeX >= 2 && data.SizeY >= 2;

    public static SurfaceView? TryCreate(GraphData data, SurfaceCamera camera, float scale, Func<int, int, bool> isSelected) =>
        CanShow(data) && data.Y is { } yAxis ? new SurfaceView(data, yAxis, camera, scale, isSelected) : null;

    private static double[] Fractions(GraphAxis axis)
    {
        double min = axis.Positions.Min(), max = axis.Positions.Max();
        return axis.Positions.Select(p => max == min ? 0.5 : (p - min) / (max - min)).ToArray();
    }

    public void Draw(IPlotRenderer r, RectangleF bounds, PlotHit? hover)
    {
        _points.Clear();
        _area = bounds;
        var fx = Fractions(data.X);
        var fy = Fractions(yAxis);
        var (vmin, vmax) = data.ValueRange;
        var ticks = AxisTicks.Compute(vmin, vmax, 5);
        double Fz(double v) => double.IsFinite(v) ? (v - ticks.Min) / (ticks.Max - ticks.Min) : 0;

        // Fit the projected unit cube into the area, leaving room for labels.
        float lineHeight = r.MeasureText("0").Height;
        var inner = RectangleF.FromLTRB(bounds.Left + S(50), bounds.Top + S(14) + lineHeight, bounds.Right - S(50), bounds.Bottom - S(14) - lineHeight * 2);
        if (inner.Width < S(60) || inner.Height < S(60))
            return;
        var corners = new List<Vector3>();
        foreach (var x in new[] { 0.0, 1.0 })
            foreach (var y in new[] { 0.0, 1.0 })
                foreach (var z in new[] { 0.0, 1.0 })
                    corners.Add(camera.Project(x, y, z));
        float minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        float minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        float k = Math.Min(inner.Width / (maxX - minX), inner.Height / (maxY - minY));
        float ox = inner.Left + (inner.Width - (maxX - minX) * k) / 2 - minX * k;
        float oy = inner.Top + (inner.Height - (maxY - minY) * k) / 2 - minY * k;
        PointF P(double x, double y, double z)
        {
            var v = camera.Project(x, y, z);
            return new PointF(ox + v.X * k, oy + v.Y * k);
        }

        // Floor and its grid lines.
        var floor = new[] { P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0) };
        r.FillPolygon(floor, Color.FromArgb(247, 248, 250));
        for (int i = 0; i < data.SizeX; i++)
            r.Line(P(fx[i], 0, 0), P(fx[i], 1, 0), PlotStyle.GridLine, 1);
        for (int j = 0; j < data.SizeY; j++)
            r.Line(P(0, fy[j], 0), P(1, fy[j], 0), PlotStyle.GridLine, 1);
        r.DrawPolygon(floor, PlotStyle.Frame, 1);


        // Surface, back to front.
        var fz = new double[data.SizeX, data.SizeY];
        for (int i = 0; i < data.SizeX; i++)
            for (int j = 0; j < data.SizeY; j++)
                fz[i, j] = Math.Clamp(Fz(data.Values[i, j]), 0, 1);
        var quad = new PointF[4];
        var outline = PlotStyle.WithAlpha(Color.FromArgb(60, 64, 72), 110);
        foreach (var (i, j) in camera.PaintOrder(data.SizeX, data.SizeY, (a, b) => fz[a, b]))
        {
            quad[0] = P(fx[i], fy[j], fz[i, j]);
            quad[1] = P(fx[i + 1], fy[j], fz[i + 1, j]);
            quad[2] = P(fx[i + 1], fy[j + 1], fz[i + 1, j + 1]);
            quad[3] = P(fx[i], fy[j + 1], fz[i, j + 1]);
            double t = (fz[i, j] + fz[i + 1, j] + fz[i, j + 1] + fz[i + 1, j + 1]) / 4;
            r.FillAndDrawPolygon(quad, PlotStyle.Heat(t), outline, 1);
        }

        // Points for hit testing, and the selected cells on top.
        for (int i = 0; i < data.SizeX; i++)
        {
            for (int j = 0; j < data.SizeY; j++)
            {
                var p = P(fx[i], fy[j], fz[i, j]);
                _points.Add((p, i, j));
                if (isSelected(i, j))
                {
                    r.FillCircle(p, S(3.5f), PlotStyle.Selection);
                    r.DrawCircle(p, S(3.5f), Color.White, S(1));
                }
            }
        }

        // Value axis on the leftmost floor corner, drawn after the surface so it stays readable.
        // The value axis goes on the leftmost floor corner. The X and Y titles (with their ranges)
        // go on the two front edges, which meet at the corner nearest the viewer, so they are never
        // covered by the surface.
        var floorCorners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
        var corner = floorCorners.OrderBy(c => P(c.Item1, c.Item2, 0).X).First();
        var labelled = floorCorners.OrderByDescending(c => camera.Project(c.Item1, c.Item2, 0).Z).First();
        var axisBottom = P(corner.Item1, corner.Item2, 0);
        var axisTop = P(corner.Item1, corner.Item2, 1);
        r.Line(axisBottom, axisTop, PlotStyle.Label, 1);
        foreach (var v in ticks.Values)
        {
            var p = P(corner.Item1, corner.Item2, Fz(v));
            r.Line(p, new PointF(p.X - S(4), p.Y), PlotStyle.Label, 1);
            r.Text(PlotText.Tick(v, ticks.Step), new PointF(p.X - S(7), p.Y), PlotStyle.Label, TextAnchor.MiddleRight);
        }
        r.Text(data.ValueTitle, new PointF(axisTop.X, axisTop.Y - S(6)), PlotStyle.Title, TextAnchor.BottomCenter, bold: true);

        // Axis titles and end labels along the front floor edges.
        DrawEdgeLabels(r, data.X, P(0, labelled.Item2, 0), P(1, labelled.Item2, 0), floor, bounds);
        DrawEdgeLabels(r, yAxis, P(labelled.Item1, 0, 0), P(labelled.Item1, 1, 0), floor, bounds);

        r.Text("drag to rotate, double-click to reset", new PointF(bounds.Right - S(6), bounds.Bottom - S(4)),
            PlotStyle.WithAlpha(PlotStyle.Label, 150), TextAnchor.BottomRight);

        if (hover != null)
        {
            r.DrawCircle(hover.Location, S(6), PlotStyle.Title, S(1.5f));
            Readout.Draw(r, hover.Readout, hover.Location, bounds, scale);
        }
    }

    /// <summary>Writes an axis title and its range beside a floor edge, pushed away from the floor.</summary>
    private void DrawEdgeLabels(IPlotRenderer r, GraphAxis axis, PointF start, PointF end, PointF[] floor, RectangleF bounds)
    {
        var center = new PointF(floor.Average(p => p.X), floor.Average(p => p.Y));
        var mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        float dx = mid.X - center.X, dy = mid.Y - center.Y;
        float len = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        dx /= len;
        dy /= len;
        var anchor = dx < -0.3f ? TextAnchor.MiddleRight : dx > 0.3f ? TextAnchor.MiddleLeft : dy > 0 ? TextAnchor.TopCenter : TextAnchor.BottomCenter;
        var at = new PointF(mid.X + dx * S(14), mid.Y + dy * S(14));
        string range = $"{axis.Labels[0]} – {axis.Labels[^1]}";
        var titleSize = r.MeasureText(axis.Title, true);
        // Title above, range below, as one block.
        var block = new SizeF(Math.Max(titleSize.Width, r.MeasureText(range).Width), titleSize.Height * 2);
        var origin = TextAnchors.Origin(at, block, anchor);
        Clamped(r, axis.Title, new PointF(origin.X + block.Width / 2, origin.Y), PlotStyle.Title, TextAnchor.TopCenter, bounds, true);
        Clamped(r, range, new PointF(origin.X + block.Width / 2, origin.Y + titleSize.Height), PlotStyle.Label, TextAnchor.TopCenter, bounds, false);
    }

    /// <summary>Draws text anchored at a point, moved as needed to stay inside <paramref name="bounds"/>.</summary>
    private void Clamped(IPlotRenderer r, string text, PointF at, Color color, TextAnchor anchor, RectangleF bounds, bool bold)
    {
        var size = r.MeasureText(text, bold);
        var origin = TextAnchors.Origin(at, size, anchor);
        float x = Math.Clamp(origin.X, bounds.Left + S(2), Math.Max(bounds.Left + S(2), bounds.Right - size.Width - S(2)));
        float y = Math.Clamp(origin.Y, bounds.Top + S(2), Math.Max(bounds.Top + S(2), bounds.Bottom - size.Height - S(2)));
        r.Text(text, new PointF(x, y), color, TextAnchor.TopLeft, bold);
    }

    public PlotHit? HitTest(PointF location)
    {
        float best = (10 * scale) * (10 * scale);
        (PointF P, int I, int J)? hit = null;
        // Later points are nearer the viewer in most views; prefer them on ties.
        foreach (var pt in _points)
        {
            float dx = pt.P.X - location.X, dy = pt.P.Y - location.Y;
            float d = dx * dx + dy * dy;
            if (d <= best)
            {
                best = d;
                hit = pt;
            }
        }
        return hit is { } h ? new PlotHit(h.I, h.J, h.P, PlotText.Readout(data, h.I, h.J)) : null;
    }
}
