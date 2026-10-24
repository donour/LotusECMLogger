using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LotusECMLogger.Services.Tuning.Plotting;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using Size = System.Drawing.Size;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>
/// Read-only graph of a curve or map. Draws with Direct2D on the GPU (Direct2D falls back to
/// software on its own), or with GDI+ if Direct2D can't be used at all. Paints only when
/// something changes; there is no render loop.
/// </summary>
public sealed class PlotControl : Control
{
    private GraphData? _data;
    private GraphData? _shown;
    private LinesView? _lines;
    private SurfaceView? _surface;
    private PlotHit? _hover;
    private Point? _dragFrom;
    private bool _dragged;
    private bool _failed;
    private bool _surfaceMode;
    private bool _transposed;
    private Func<int, int, bool> _selected = (_, _) => false;
    private GraphCursor? _cursor;

    // Direct2D state; null when using GDI+. RAIDERLITE_GDI_GRAPHS=1 forces GDI+ (e.g. for a misbehaving driver).
    private ID2D1HwndRenderTarget? _target;
    private D2DRenderer? _d2d;
    private bool _useGdi = Environment.GetEnvironmentVariable("LOTUS_GDI_GRAPHS") == "1";

    public PlotControl()
    {
        // Direct2D presents straight to the window: no WinForms double buffer or background erase.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = PlotStyle.Background;
    }

    /// <summary>Raised when a point is clicked, with its cell (x, y) in the data as given, to select it in the grid.</summary>
    public event EventHandler<(int X, int Y)>? CellClicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SurfaceCamera Camera { get; } = new();

    /// <summary>Show a map as a 3D surface instead of lines.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SurfaceMode
    {
        get => _surfaceMode;
        set
        {
            _surfaceMode = value;
            Rebuild();
        }
    }

    /// <summary>Draw a map's lines per column instead of per row.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Transposed
    {
        get => _transposed;
        set
        {
            _transposed = value;
            Rebuild();
        }
    }

    /// <summary>Whether the data can be shown as a surface (a map with at least 2 × 2 values).</summary>
    public bool SurfaceAvailable => _data != null && SurfaceView.CanShow(_data);

    /// <summary>Whether a surface is being drawn (surface mode and the data allows it).</summary>
    public bool ShowingSurface => _surface != null;

    /// <summary>Time spent issuing the last on-screen frame's drawing (excludes waiting for the display).</summary>
    public double LastDrawMilliseconds { get; private set; }

    /// <summary>Whether drawing is on the GPU path (Direct2D) rather than GDI+.</summary>
    public bool UsingDirect2D => !_useGdi;

    public void SetData(GraphData? data)
    {
        _data = data;
        Rebuild();
    }

    private void Rebuild()
    {
        _hover = null;
        _failed = false;
        _shown = _data != null && _transposed && _data.IsMap ? _data.Transposed() : _data;
        float scale = DeviceDpi / 96f;
        _lines = _shown != null ? new LinesView(_shown, scale, LinesTransposed ? (i, j) => _selected(j, i) : (i, j) => _selected(i, j)) : null;
        // The surface keeps the table's orientation; transposing only affects the lines.
        _surface = _data != null && _surfaceMode ? SurfaceView.TryCreate(_data, Camera, scale, (i, j) => _selected(i, j)) : null;
        ApplyCursor();
        Invalidate();
    }

    private bool LinesTransposed => _transposed && _data is { IsMap: true };

    /// <summary>Which cells (x, y) are selected in the grid; their points are ringed.</summary>
    /// <summary>The live operating point, in the data's axis positions (as given, not transposed); null hides it.</summary>
    public void SetCursor(GraphCursor? cursor)
    {
        _cursor = cursor;
        ApplyCursor();
        Invalidate();
    }

    private void ApplyCursor()
    {
        if (_lines != null) _lines.Cursor = _cursor is { } c && LinesTransposed ? c with { X = c.Y, Y = c.X } : _cursor;
        if (_surface != null) _surface.Cursor = _cursor;
    }

    public void SetSelection(Func<int, int, bool> isSelected)
    {
        _selected = isSelected;
        Invalidate();
    }

    private void Render(IPlotRenderer r, Rectangle bounds)
    {
        r.Clear(PlotStyle.Background);
        if (_failed)
            r.Text("The graph could not be drawn (details in the error log).", new PointF(bounds.Width / 2f, bounds.Height / 2f), PlotStyle.Label, TextAnchor.Center);
        else if (_surface != null)
            _surface.Draw(r, bounds, _hover);
        else if (_lines != null)
            _lines.Draw(r, bounds, _hover);
        else
            r.Text("Nothing to graph", new PointF(bounds.Width / 2f, bounds.Height / 2f), PlotStyle.Label, TextAnchor.Center);
    }

    // ----- painting -----

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            if (!_useGdi && TryPaintDirect2D())
                return;
            PaintGdi(e.Graphics);
        }
        catch (Exception ex) when (!_failed)
        {
            // A drawing bug must not take the window down or, by reporting from inside WM_PAINT,
            // repeat on every repaint: log it once and show a message in place of the graph.
            Debug.WriteLine($"PlotControl: drawing failed: {ex}");
            _failed = true;
            _hover = null;
            Invalidate();
        }
    }

    private bool TryPaintDirect2D()
    {
        if (Width <= 0 || Height <= 0)
            return true;
        try
        {
            if ((_target == null || _d2d == null) && !CreateTarget())
                return false;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _d2d.BeginFrame();
            _target.BeginDraw();
            SharpGen.Runtime.Result result;
            try
            {
                Render(_d2d, ClientRectangle);
            }
            finally
            {
                // Always close the frame, or the target stays mid-draw and every later frame fails.
                LastDrawMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                result = _target.EndDraw(out _, out _);
            }
            if (result == Vortice.Direct2D1.ResultCode.RecreateTarget)
            {
                // The GPU device was lost (driver reset, sleep, remote session): start over.
                ReleaseTarget();
                Invalidate();
            }
            return true;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            ReleaseTarget();
            _useGdi = true;
            return false;
        }
    }

    [MemberNotNullWhen(true, nameof(_target), nameof(_d2d))]
    private bool CreateTarget()
    {
        if (Direct2D.Factories is not { } f)
        {
            _useGdi = true;
            return false;
        }
        // RenderTargetType.Default: hardware when available, otherwise Direct2D's software rasterizer.
        var props = new RenderTargetProperties(RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96,
            RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);
        var hwnd = new HwndRenderTargetProperties
        {
            Hwnd = Handle,
            PixelSize = new SizeI(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)),
            PresentOptions = PresentOptions.None,
        };
        _target = f.Factory.CreateHwndRenderTarget(props, hwnd);
        _target.AntialiasMode = AntialiasMode.PerPrimitive;
        _d2d ??= new D2DRenderer(f.Factory, f.Write, DeviceDpi / 96f);
        _d2d.Bind(_target);
        return true;
    }

    private void ReleaseTarget()
    {
        _target?.Dispose();
        _target = null;
    }

    private void PaintGdi(Graphics screen)
    {
        // Double-buffer by hand: the control is created without WinForms double buffering for Direct2D.
        using var buffer = BufferedGraphicsManager.Current.Allocate(screen, ClientRectangle);
        using (var renderer = new GdiRenderer(buffer.Graphics, DeviceDpi / 96f))
            Render(renderer, ClientRectangle);
        buffer.Render(screen);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_target != null && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            try
            {
                _target.Resize(new SizeI(ClientSize.Width, ClientSize.Height));
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                ReleaseTarget();
            }
        }
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _d2d?.Dispose();
        _d2d = null;
        ReleaseTarget();
        Rebuild();
    }

    /// <summary>Renders the graph into a bitmap (for copying), with Direct2D if possible.</summary>
    public Bitmap RenderToBitmap(Size size)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var bounds = new Rectangle(Point.Empty, bmp.Size);
        var hover = _hover;
        _hover = null;
        try
        {
            using var g = Graphics.FromImage(bmp);
            if (Direct2D.Factories is { } f)
            {
                try
                {
                    var props = new RenderTargetProperties(RenderTargetType.Default,
                        new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                        RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);
                    using var dc = f.Factory.CreateDCRenderTarget(props);
                    using var renderer = new D2DRenderer(f.Factory, f.Write, DeviceDpi / 96f);
                    var hdc = g.GetHdc();
                    try
                    {
                        dc.BindDC(hdc, new Vortice.RawRect(0, 0, bmp.Width, bmp.Height));
                        dc.AntialiasMode = AntialiasMode.PerPrimitive;
                        renderer.Bind(dc);
                        dc.BeginDraw();
                        Render(renderer, bounds);
                        dc.EndDraw(out _, out _);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                    return bmp;
                }
                catch (SharpGen.Runtime.SharpGenException)
                {
                    // Fall through to GDI+.
                }
            }
            using (var gdi = new GdiRenderer(g, DeviceDpi / 96f))
                Render(gdi, bounds);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
        finally
        {
            _hover = hover;
        }
    }

    // ----- mouse -----

    private PlotHit? HitTest(Point p) => _surface?.HitTest(p) ?? _lines?.HitTest(p);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _surface != null)
        {
            _dragFrom = e.Location;
            _dragged = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is { } from && e.Button == MouseButtons.Left && _surface != null)
        {
            // Small jitters while pressing still count as a click: rotate only once the mouse really moves.
            _dragged |= Math.Abs(e.X - from.X) > 2 || Math.Abs(e.Y - from.Y) > 2;
            if (!_dragged)
                return;
            // Orbit: horizontal drag turns, vertical drag tilts.
            Camera.Yaw += (e.X - from.X) * 0.5;
            Camera.Pitch += (e.Y - from.Y) * 0.4;
            _dragFrom = e.Location;
            _hover = null;
            Invalidate();
            return;
        }
        var hit = HitTest(e.Location);
        if ((hit?.I, hit?.J) != (_hover?.I, _hover?.J))
        {
            _hover = hit;
            Cursor = hit != null ? Cursors.Hand : _surface != null ? Cursors.SizeAll : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        // A press that turned into a rotation is not a click, even if it ends on a point.
        bool dragged = _dragged;
        _dragFrom = null;
        _dragged = false;
        if (e.Button == MouseButtons.Left && !dragged && HitTest(e.Location) is { } hit)
            CellClicked?.Invoke(this, _surface == null && LinesTransposed ? (hit.J, hit.I) : (hit.I, hit.J));
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_surface != null)
        {
            Camera.Reset();
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            _hover = null;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseTarget();
            _d2d?.Dispose();
            _d2d = null;
        }
        base.Dispose(disposing);
    }
}
