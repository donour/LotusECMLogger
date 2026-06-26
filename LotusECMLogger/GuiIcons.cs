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
