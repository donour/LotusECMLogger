using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// One line of ECU load figures (<see cref="EcuLoadText"/>), each segment coloured by its level, or a grey message
    /// while there are none. Clicking it raises <see cref="Control.Click"/> (the Tuning page opens the details).
    /// </summary>
    internal sealed class EcuLoadStrip : Control
    {
        private const int Gap = 16;
        private IReadOnlyList<(string Text, EcuLoadLevel Level)> segments = [];
        private string message = "";

        public EcuLoadStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public static Color ColorOf(EcuLoadLevel level) => level switch
        {
            EcuLoadLevel.Fault => Color.Firebrick,
            EcuLoadLevel.Warning => Color.DarkOrange,
            _ => SystemColors.ControlText,
        };

        /// <summary>The segments shown, for tests.</summary>
        public IReadOnlyList<(string Text, EcuLoadLevel Level)> Segments => segments;
        /// <summary>The line as plain text: the segments, or the message.</summary>
        public string DisplayText => segments.Count > 0 ? "ECU  " + string.Join("   ", segments.Select(s => s.Text)) : message;
        public EcuLoadLevel WorstLevel => segments.Count == 0 ? EcuLoadLevel.Normal : segments.Max(s => s.Level);

        public void Show(IReadOnlyList<(string Text, EcuLoadLevel Level)> shown)
        {
            if (shown.SequenceEqual(segments)) return;
            (segments, message) = (shown, "");
            Invalidate();
        }

        public void ShowMessage(string text)
        {
            if (segments.Count == 0 && text == message) return;
            (segments, message) = ([], text);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int y = Math.Max(0, (Height - Font.Height) / 2);
            if (segments.Count == 0)
            {
                TextRenderer.DrawText(e.Graphics, message, Font, new Rectangle(4, y, Width - 8, Font.Height), SystemColors.GrayText,
                    flags | TextFormatFlags.EndEllipsis);
                return;
            }
            int x = 4;
            using var bold = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "ECU", bold, new Point(x, y), SystemColors.GrayText, flags);
            x += TextRenderer.MeasureText(e.Graphics, "ECU", bold, Size.Empty, flags).Width + Gap / 2;
            foreach (var (text, level) in segments)
            {
                if (x >= Width) break;
                var font = level == EcuLoadLevel.Normal ? Font : bold;
                TextRenderer.DrawText(e.Graphics, text, font, new Point(x, y), ColorOf(level), flags);
                x += TextRenderer.MeasureText(e.Graphics, text, font, Size.Empty, flags).Width + Gap;
            }
        }
    }
}
