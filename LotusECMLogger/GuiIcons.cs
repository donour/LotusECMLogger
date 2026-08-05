using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LotusECMLogger;

/// <summary>
/// Renders Segoe MDL2 Assets glyphs into bitmaps for use as button and tab icons.
/// Segoe MDL2 Assets is built into Windows 10/11 — no external files required.
/// Glyph codes: https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font
/// </summary>
internal static class GuiIcons
{
    private const string FontName = "Segoe MDL2 Assets";

    // Tab glyphs
    public const string VehicleInfo  = ""; // Info circle
    public const string LiveData     = ""; // RenderBarChart
    public const string EcuCoding    = ""; // Settings gear
    public const string Dtc          = ""; // AlertSolid
    public const string RmaLogging   = ""; // Download (read from ECU)
    public const string LiveTuning   = ""; // Edit / pencil
    public const string Snapshots    = ""; // Camera (point-in-time memory snapshot)

    public const string HighSpeedLog = ""; // Stopwatch (high-speed channel logging)

    // Sub-tab glyphs (inside Live Data)
    public const string LoggerTab    = ""; // Play
    public const string ConfigTab    = ""; // Setting2 / sliders

    // Button glyphs
    public const string Play    = ""; // Play
    public const string Stop    = ""; // Stop
    public const string Read    = ""; // Download (read from ECU)
    public const string Write   = ""; // Upload (write to ECU)
    public const string Save    = ""; // Save
    public const string Refresh = ""; // Refresh
    public const string UpdateRestore = Refresh; // UpdateRestore (E777)
    public const string Add      = ""; // Add
    public const string Delete   = ""; // Delete (trash)
    public const string Clear    = ""; // Clear
    public const string OpenFile = ""; // OpenFile (browse)
    public const string Connect  = ""; // Ethernet (test connection)
    public const string DynoMode = ""; // SpeedHigh (dyno mode)

    /// <summary>
    /// Renders a single MDL2 glyph into a square bitmap with a transparent background.
    /// </summary>
    public static Bitmap Render(string glyph, int size, Color color)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(FontName, size * 0.72f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), fmt);
        return bmp;
    }

    /// <summary>
    /// Draws a cross-drilled brake rotor with a caliper clamped over its edge, for the ABS/ESP tab.
    /// </summary>
    /// <remarks>
    /// This one is drawn rather than rendered from a glyph because Segoe MDL2 Assets has no brake
    /// symbol — the nearest circular glyphs read as a gear or a wheel. Proportions are tuned to sit
    /// at the same visual weight as the MDL2 tab glyphs beside it: thin outlines for the disc and
    /// hub, small filled dots for the drilling, and a solid caliper (which physically sits in front
    /// of the disc). The disc is offset left by half the caliper's overhang so the whole composition
    /// stays optically centred in the icon box.
    /// </remarks>
    public static Bitmap RenderBrakeRotor(int size, Color color)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Stroke is floored at 1px so the outlines survive at small icon sizes.
        float stroke = MathF.Max(1f, size * 0.062f);
        float rOut = size * 0.36f - stroke / 2f;
        float caliperRadial = size * 0.19f;
        float caliperTangential = size * 0.30f;

        float cx = size / 2f - caliperRadial / 4f;
        float cy = size / 2f;

        using var pen = new Pen(color, stroke);
        using var brush = new SolidBrush(color);

        // Disc edge and hub.
        g.DrawEllipse(pen, cx - rOut, cy - rOut, rOut * 2, rOut * 2);
        float rHub = size * 0.12f;
        g.DrawEllipse(pen, cx - rHub, cy - rHub, rHub * 2, rHub * 2);

        // Cross-drilled holes, evenly spaced midway between hub and rim.
        float rHoles = (rOut + rHub) / 2f;
        float rHole = size * 0.04f;
        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3.0 + Math.PI / 6.0;
            float hx = cx + (float)(Math.Cos(angle) * rHoles);
            float hy = cy + (float)(Math.Sin(angle) * rHoles);
            g.FillEllipse(brush, hx - rHole, hy - rHole, rHole * 2, rHole * 2);
        }

        // Caliper at 3 o'clock — axis-aligned so it lands cleanly on the pixel grid.
        var body = new RectangleF(
            cx + rOut - caliperRadial / 2f, cy - caliperTangential / 2f,
            caliperRadial, caliperTangential);
        using var caliper = RoundedRectangle(body, size * 0.07f);
        g.FillPath(brush, caliper);

        return bmp;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Builds an ImageList from a sequence of glyphs, all rendered at the same size and color.
    /// </summary>
    public static ImageList BuildImageList(int size, Color color, params string[] glyphs)
    {
        var list = new ImageList
        {
            ImageSize = new Size(size, size),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        foreach (var glyph in glyphs)
            list.Images.Add(Render(glyph, size, color));
        return list;
    }

    /// <summary>
    /// Applies a glyph icon to a button, positioned to the left of the text.
    /// </summary>
    public static void ApplyToButton(Button button, string glyph, int size = 14)
    {
        button.Image = Render(glyph, size, SystemColors.ControlText);
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.ImageAlign = ContentAlignment.MiddleLeft;
    }
}
