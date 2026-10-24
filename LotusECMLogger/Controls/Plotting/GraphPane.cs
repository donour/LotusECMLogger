using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls.Plotting;

/// <summary>
/// The graph beside the calibration grid: a <see cref="PlotControl"/>, with a slim toolbar for maps (Lines /
/// Surface, Transpose, Reset View). Right-click copies the graph as an image. Ported from RaiderLite.
/// </summary>
public sealed class GraphPane : Panel
{
    // Lines or surface: the last choice is used for maps opened later in this run.
    private static bool rememberSurface = true;

    private readonly ToolStrip _bar;
    private readonly ToolStripButton _linesButton;
    private readonly ToolStripButton _surfaceButton;
    private readonly ToolStripButton _transposeButton;
    private readonly ToolStripButton _resetButton;
    private readonly ContextMenuStrip _menu = new();

    public GraphPane()
    {
        BackColor = PlotStyle.Background;
        Plot.Dock = DockStyle.Fill;
        Controls.Add(Plot);

        _bar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = PlotStyle.Background,
            Padding = new Padding(4, 0, 4, 0),
            CanOverflow = false,
            Visible = false,
        };
        ToolStripButton Button(string text, string glyph, string tip, Action click)
        {
            var b = new ToolStripButton(text)
            {
                Image = GuiIcons.Render(glyph, 16, Color.FromArgb(50, 54, 62)),
                ImageScaling = ToolStripItemImageScaling.None,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = tip,
                AutoToolTip = false,
            };
            b.Click += (_, _) => click();
            _bar.Items.Add(b);
            return b;
        }
        _linesButton = Button("Lines", "\uE9D2", "One line per row of the map", () => SetSurface(false));
        _surfaceButton = Button("Surface", "\uF158", "3D surface (drag to rotate)", () => SetSurface(true));
        _bar.Items.Add(new ToolStripSeparator());
        _transposeButton = Button("Transpose", "\uE8AB", "Lines per column instead of per row", () =>
        {
            Plot.Transposed = !Plot.Transposed;
            UpdateButtons();
        });
        _resetButton = Button("Reset View", "\uE777", "Return the surface to its default angle", () =>
        {
            Plot.Camera.Reset();
            Plot.Invalidate();
        });
        Controls.Add(_bar);
        Plot.SurfaceMode = rememberSurface;

        _menu.Items.Add("&Copy Image", null, (_, _) => CopyImage());
        Plot.ContextMenuStrip = _menu;
    }

    public PlotControl Plot { get; } = new();

    /// <summary>Shows new data (after the item or its values changed). Only maps have view choices.</summary>
    public void SetData(GraphData? data)
    {
        _bar.Visible = data is { IsMap: true };
        Plot.SetData(data);
        UpdateButtons();
    }

    private void SetSurface(bool surface)
    {
        Plot.SurfaceMode = surface;
        rememberSurface = surface;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        // A map with a single row or column can't be a surface: it stays as lines.
        bool surface = Plot.ShowingSurface;
        _surfaceButton.Enabled = Plot.SurfaceAvailable;
        _linesButton.Checked = !surface;
        _surfaceButton.Checked = surface;
        _transposeButton.Checked = Plot.Transposed;
        _transposeButton.Enabled = !surface;
        _resetButton.Enabled = surface;
    }

    public void CopyImage()
    {
        using var bmp = Plot.RenderToBitmap(Plot.ClientSize);
        try
        {
            Clipboard.SetImage(bmp);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is busy; nothing else to do.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _menu.Dispose();
        base.Dispose(disposing);
    }
}
