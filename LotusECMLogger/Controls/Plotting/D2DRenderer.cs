using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>
/// Shared Direct2D and DirectWrite factories. Created once per process and used only on the
/// UI thread; they live as long as the program, so they are never disposed.
/// </summary>
internal static class Direct2D
{
    private static ID2D1Factory? _factory;
    private static IDWriteFactory? _dwrite;
    private static bool _failed;

    /// <summary>The factories, or null if Direct2D isn't available on this system.</summary>
    public static (ID2D1Factory Factory, IDWriteFactory Write)? Factories
    {
        get
        {
            if (_failed)
                return null;
            try
            {
                _factory ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
                _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
                return (_factory, _dwrite);
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                _failed = true;
                return null;
            }
        }
    }
}

/// <summary>
/// Direct2D renderer over any render target (a window, or a GDI device context when
/// rendering to a bitmap). Brushes and text formats are cached per target.
/// </summary>
public sealed class D2DRenderer : IPlotRenderer, IDisposable
{
    private readonly ID2D1Factory _factory;
    private readonly IDWriteFactory _dwrite;
    private readonly Dictionary<int, ID2D1SolidColorBrush> _brushes = [];
    private readonly Dictionary<(string, bool), SizeF> _textSizes = [];
    private readonly IDWriteTextFormat _font;
    private readonly IDWriteTextFormat _bold;
    private readonly ID2D1StrokeStyle _dash;
    private readonly ID2D1StrokeStyle _round;
    private ID2D1RenderTarget? _target;

    public D2DRenderer(ID2D1Factory factory, IDWriteFactory dwrite, float dpiScale)
    {
        _factory = factory;
        _dwrite = dwrite;
        // The render target runs at 96 DPI so one unit is one pixel; scale the font instead.
        float size = 8.5f * dpiScale * 96f / 72f;
        _font = dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size, "");
        _bold = dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Bold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size, "");
        var round = new StrokeStyleProperties { LineJoin = LineJoin.Round, StartCap = CapStyle.Round, EndCap = CapStyle.Round };
        _round = factory.CreateStrokeStyle(round);
        var dash = new StrokeStyleProperties { LineJoin = LineJoin.Round, DashStyle = DashStyle.Custom };
        _dash = factory.CreateStrokeStyle(dash, [4f, 3f]);
    }

    /// <summary>Points the renderer at a (new) target; brushes belong to a target, so they are recreated.</summary>
    public void Bind(ID2D1RenderTarget target)
    {
        if (ReferenceEquals(target, _target))
            return;
        ReleaseBrushes();
        _target = target;
    }

    /// <summary>
    /// Call before each frame's BeginDraw. Surface colours are continuous, so the brush cache
    /// grows with every edit; it is trimmed here, never mid-frame where queued drawing may use a brush.
    /// </summary>
    public void BeginFrame()
    {
        if (_brushes.Count > 4096)
            ReleaseBrushes();
    }

    private ID2D1RenderTarget T => _target ?? throw new InvalidOperationException("No render target bound.");

    private ID2D1SolidColorBrush Brush(Color color)
    {
        int key = color.ToArgb();
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = T.CreateSolidColorBrush(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            _brushes[key] = brush;
        }
        return brush;
    }

    private static Vector2 V(PointF p) => new(p.X, p.Y);

    private ID2D1PathGeometry Path(ReadOnlySpan<PointF> points, bool closed)
    {
        var geometry = _factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(V(points[0]), closed ? FigureBegin.Filled : FigureBegin.Hollow);
        var rest = new Vector2[points.Length - 1];
        for (int i = 1; i < points.Length; i++)
            rest[i - 1] = V(points[i]);
        sink.AddLines(rest);
        sink.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open);
        sink.Close();
        return geometry;
    }

    public void Clear(Color color) => T.Clear(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, 1f));

    public void Line(PointF a, PointF b, Color color, float width, bool dashed = false) =>
        T.DrawLine(V(a), V(b), Brush(color), width, dashed ? _dash : _round);

    public void Polyline(ReadOnlySpan<PointF> points, Color color, float width, bool dashed = false)
    {
        if (points.Length < 2)
            return;
        using var path = Path(points, closed: false);
        T.DrawGeometry(path, Brush(color), width, dashed ? _dash : _round);
    }

    public void FillPolygon(ReadOnlySpan<PointF> points, Color color)
    {
        if (points.Length < 3)
            return;
        using var path = Path(points, closed: true);
        T.FillGeometry(path, Brush(color), null);
    }

    public void DrawPolygon(ReadOnlySpan<PointF> points, Color color, float width)
    {
        if (points.Length < 2)
            return;
        using var path = Path(points, closed: true);
        T.DrawGeometry(path, Brush(color), width, _round);
    }

    public void FillAndDrawPolygon(ReadOnlySpan<PointF> points, Color fill, Color stroke, float width)
    {
        if (points.Length < 3)
            return;
        using var path = Path(points, closed: true);
        T.FillGeometry(path, Brush(fill), null);
        // Plain lines for the outline: stroking the geometry makes Direct2D tessellate it on the
        // CPU, which costs several times more than the fill for a surface of ~1000 quads.
        var brush = Brush(stroke);
        for (int i = 0; i < points.Length; i++)
            T.DrawLine(V(points[i]), V(points[(i + 1) % points.Length]), brush, width);
    }

    public void FillCircle(PointF center, float radius, Color color) =>
        T.FillEllipse(new Ellipse(V(center), radius, radius), Brush(color));

    public void DrawCircle(PointF center, float radius, Color color, float width) =>
        T.DrawEllipse(new Ellipse(V(center), radius, radius), Brush(color), width);

    public void FillRect(RectangleF rect, Color color) =>
        T.FillRectangle(new Rect(rect.X, rect.Y, rect.Width, rect.Height), Brush(color));

    public void DrawRect(RectangleF rect, Color color, float width) =>
        T.DrawRectangle(new Rect(rect.X, rect.Y, rect.Width, rect.Height), Brush(color), width);

    public SizeF MeasureText(string text, bool bold = false)
    {
        // Labels repeat every frame; measuring creates a DirectWrite layout, so cache the sizes.
        if (_textSizes.TryGetValue((text, bold), out var cached))
            return cached;
        using var layout = _dwrite.CreateTextLayout(text, bold ? _bold : _font, 4096, 4096);
        var m = layout.Metrics;
        var size = new SizeF(m.WidthIncludingTrailingWhitespace, m.Height);
        if (_textSizes.Count > 2000)
            _textSizes.Clear();
        _textSizes[(text, bold)] = size;
        return size;
    }

    public void Text(string text, PointF at, Color color, TextAnchor anchor, bool bold = false)
    {
        var size = MeasureText(text, bold);
        var origin = TextAnchors.Origin(at, size, anchor);
        T.DrawText(text, bold ? _bold : _font, new Rect(origin.X, origin.Y, size.Width + 2, size.Height + 2), Brush(color));
    }

    public void VerticalText(string text, PointF center, Color color)
    {
        var size = MeasureText(text);
        var saved = T.Transform;
        T.Transform = Matrix3x2.CreateRotation(-MathF.PI / 2, V(center)) * saved;
        T.DrawText(text, _font, new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width + 2, size.Height + 2), Brush(color));
        T.Transform = saved;
    }

    public void PushClip(RectangleF rect) =>
        T.PushAxisAlignedClip(new Vortice.RawRectF(rect.Left, rect.Top, rect.Right, rect.Bottom), AntialiasMode.Aliased);

    public void PopClip() => T.PopAxisAlignedClip();

    private void ReleaseBrushes()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        _brushes.Clear();
    }

    public void Dispose()
    {
        ReleaseBrushes();
        _font.Dispose();
        _bold.Dispose();
        _dash.Dispose();
        _round.Dispose();
        _target = null;
    }
}
