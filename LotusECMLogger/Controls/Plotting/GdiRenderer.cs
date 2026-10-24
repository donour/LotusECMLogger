using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>GDI+ renderer: used when Direct2D isn't available, and for rendering to bitmaps without it.</summary>
public sealed class GdiRenderer(Graphics g, float dpiScale) : IPlotRenderer, IDisposable
{
    // Pixel sizes, as in D2DRenderer: a point size would be scaled again by the Graphics' own DPI
    // (twice-scaled text on screen at 125%+, and a different size again in 96 DPI bitmaps).
    private readonly Font _font = new("Segoe UI", 8.5f * dpiScale * 96f / 72f, GraphicsUnit.Pixel);
    private readonly Font _bold = new("Segoe UI", 8.5f * dpiScale * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Stack<GraphicsState> _clips = new();
    private bool _prepared;

    private Graphics G
    {
        get
        {
            if (!_prepared)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.PageUnit = GraphicsUnit.Pixel;
                _prepared = true;
            }
            return g;
        }
    }

    private static Pen MakePen(Color color, float width, bool dashed)
    {
        var pen = new Pen(color, width) { LineJoin = LineJoin.Round };
        if (dashed)
            pen.DashPattern = [4f, 3f];
        return pen;
    }

    public void Clear(Color color) => G.Clear(color);

    public void Line(PointF a, PointF b, Color color, float width, bool dashed = false)
    {
        using var pen = MakePen(color, width, dashed);
        G.DrawLine(pen, a, b);
    }

    public void Polyline(ReadOnlySpan<PointF> points, Color color, float width, bool dashed = false)
    {
        if (points.Length < 2)
            return;
        using var pen = MakePen(color, width, dashed);
        G.DrawLines(pen, points.ToArray());
    }

    public void FillPolygon(ReadOnlySpan<PointF> points, Color color)
    {
        if (points.Length < 3)
            return;
        using var brush = new SolidBrush(color);
        G.FillPolygon(brush, points.ToArray());
    }

    public void DrawPolygon(ReadOnlySpan<PointF> points, Color color, float width)
    {
        if (points.Length < 2)
            return;
        using var pen = MakePen(color, width, false);
        G.DrawPolygon(pen, points.ToArray());
    }

    public void FillAndDrawPolygon(ReadOnlySpan<PointF> points, Color fill, Color stroke, float width)
    {
        FillPolygon(points, fill);
        DrawPolygon(points, stroke, width);
    }

    public void FillCircle(PointF center, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        G.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    public void DrawCircle(PointF center, float radius, Color color, float width)
    {
        using var pen = MakePen(color, width, false);
        G.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    public void FillRect(RectangleF rect, Color color)
    {
        using var brush = new SolidBrush(color);
        G.FillRectangle(brush, rect);
    }

    public void DrawRect(RectangleF rect, Color color, float width)
    {
        using var pen = MakePen(color, width, false);
        G.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public SizeF MeasureText(string text, bool bold = false) =>
        G.MeasureString(text, bold ? _bold : _font, PointF.Empty, StringFormat.GenericTypographic);

    public void Text(string text, PointF at, Color color, TextAnchor anchor, bool bold = false)
    {
        var size = MeasureText(text, bold);
        using var brush = new SolidBrush(color);
        G.DrawString(text, bold ? _bold : _font, brush, TextAnchors.Origin(at, size, anchor), StringFormat.GenericTypographic);
    }

    public void VerticalText(string text, PointF center, Color color)
    {
        var size = MeasureText(text);
        var state = G.Save();
        G.TranslateTransform(center.X, center.Y);
        G.RotateTransform(-90);
        using var brush = new SolidBrush(color);
        G.DrawString(text, _font, brush, -size.Width / 2, -size.Height / 2, StringFormat.GenericTypographic);
        G.Restore(state);
    }

    public void PushClip(RectangleF rect)
    {
        _clips.Push(G.Save());
        G.SetClip(rect, CombineMode.Intersect);
    }

    public void PopClip()
    {
        if (_clips.Count > 0)
            G.Restore(_clips.Pop());
    }

    public void Dispose()
    {
        _font.Dispose();
        _bold.Dispose();
    }
}
