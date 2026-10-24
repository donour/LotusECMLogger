namespace LotusECMLogger.Controls.Plotting;

public enum TextAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>
/// The drawing operations graphs need. Implemented on Direct2D (GPU) and GDI+ (fallback),
/// so the views don't depend on either. Coordinates are device pixels.
/// </summary>
public interface IPlotRenderer
{
    void Clear(Color color);

    void Line(PointF a, PointF b, Color color, float width, bool dashed = false);

    void Polyline(ReadOnlySpan<PointF> points, Color color, float width, bool dashed = false);

    void FillPolygon(ReadOnlySpan<PointF> points, Color color);

    void DrawPolygon(ReadOnlySpan<PointF> points, Color color, float width);

    /// <summary>Fills and outlines a polygon in one go (one geometry instead of two).</summary>
    void FillAndDrawPolygon(ReadOnlySpan<PointF> points, Color fill, Color stroke, float width);

    void FillCircle(PointF center, float radius, Color color);

    void DrawCircle(PointF center, float radius, Color color, float width);

    void FillRect(RectangleF rect, Color color);

    void DrawRect(RectangleF rect, Color color, float width);

    SizeF MeasureText(string text, bool bold = false);

    void Text(string text, PointF at, Color color, TextAnchor anchor, bool bold = false);

    /// <summary>Text rotated 90° counter-clockwise, centred on <paramref name="center"/>.</summary>
    void VerticalText(string text, PointF center, Color color);

    void PushClip(RectangleF rect);

    void PopClip();
}

internal static class TextAnchors
{
    /// <summary>Top-left corner for text of <paramref name="size"/> placed at <paramref name="at"/> by <paramref name="anchor"/>.</summary>
    public static PointF Origin(PointF at, SizeF size, TextAnchor anchor)
    {
        float x = anchor switch
        {
            TextAnchor.TopCenter or TextAnchor.Center or TextAnchor.BottomCenter => at.X - size.Width / 2,
            TextAnchor.TopRight or TextAnchor.MiddleRight or TextAnchor.BottomRight => at.X - size.Width,
            _ => at.X,
        };
        float y = anchor switch
        {
            TextAnchor.MiddleLeft or TextAnchor.Center or TextAnchor.MiddleRight => at.Y - size.Height / 2,
            TextAnchor.BottomLeft or TextAnchor.BottomCenter or TextAnchor.BottomRight => at.Y - size.Height,
            _ => at.Y,
        };
        return new PointF(x, y);
    }
}
