using System.ComponentModel;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// A recording's timeline for replay: the playhead, a tick at each calibration write, and an optional range that the
    /// cell activity covers. Click or drag to move the playhead; Shift+drag selects a range; double-click clears it.
    /// </summary>
    internal sealed class ReplayTimeline : Control
    {
        private static readonly Color Track = Color.FromArgb(225, 228, 232);
        private static readonly Color Selected = Color.FromArgb(110, 70, 130, 220);
        private static readonly Color WriteMark = Color.DarkOrange;

        private double duration = 1, position;
        private (double From, double To)? range;
        private IReadOnlyList<double> marks = [];
        private bool dragging, selecting;
        private double selectFrom;
        private readonly ToolTip tip = new();

        public ReplayTimeline()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
            Cursor = Cursors.Hand;
            tip.SetToolTip(this, "Click or drag to move through the recording. Shift+drag selects the time the cell activity covers; " +
                "double-click clears it. Orange ticks are calibration writes.");
        }

        /// <summary>The user moved the playhead.</summary>
        public event EventHandler? PositionChanged;

        /// <summary>The user selected or cleared a range.</summary>
        public event EventHandler? RangeChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Duration
        {
            get => duration;
            set { duration = Math.Max(1e-3, value); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Position
        {
            get => position;
            set { position = Math.Clamp(value, 0, duration); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public (double From, double To)? Range
        {
            get => range;
            set { range = value; Invalidate(); }
        }

        /// <summary>Times of the calibration writes, in seconds.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<double> Marks
        {
            get => marks;
            set { marks = value ?? []; Invalidate(); }
        }

        private Rectangle TrackBounds => new(6, 9, Math.Max(1, Width - 12), Math.Max(1, Height - 18));

        private double TimeAt(int x)
        {
            var track = TrackBounds;
            return Math.Clamp((x - track.Left) / (double)track.Width, 0, 1) * duration;
        }

        private float XAt(double time)
        {
            var track = TrackBounds;
            return track.Left + (float)(Math.Clamp(time / duration, 0, 1) * track.Width);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var track = TrackBounds;
            using (var fill = new SolidBrush(Track)) g.FillRectangle(fill, track);
            if (range is { } r)
            {
                float a = XAt(Math.Min(r.From, r.To)), b = XAt(Math.Max(r.From, r.To));
                using var fill = new SolidBrush(Selected);
                g.FillRectangle(fill, a, track.Top - 3, Math.Max(1, b - a), track.Height + 6);
            }
            using (var mark = new Pen(WriteMark, 2))
                foreach (double t in marks)
                {
                    float x = XAt(t);
                    g.DrawLine(mark, x, track.Top - 4, x, track.Bottom + 4);
                }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float head = XAt(position);
            using (var pen = new Pen(PlotStyle.Cursor, 2)) g.DrawLine(pen, head, 1, head, Height - 2);
            using (var brush = new SolidBrush(PlotStyle.Cursor))
                g.FillPolygon(brush, new[] { new PointF(head - 5, 0), new PointF(head + 5, 0), new PointF(head, 6) });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Capture = true;
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                selecting = true;
                selectFrom = TimeAt(e.X);
                Range = (selectFrom, selectFrom);
                return;
            }
            dragging = true;
            Seek(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Seek(e.X);
            else if (selecting) Range = (selectFrom, TimeAt(e.X));
            else if (marks.Count > 0 && marks.FirstOrDefault(t => Math.Abs(XAt(t) - e.X) <= 3, -1) is var near and >= 0)
                tip.SetToolTip(this, string.Create(CultureInfo.CurrentCulture, $"Calibration write at {Format(near)}"));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            dragging = false;
            if (!selecting) return;
            selecting = false;
            if (range is { } r)
            {
                var (from, to) = (Math.Min(r.From, r.To), Math.Max(r.From, r.To));
                // A click with Shift (no drag) is not a range.
                Range = XAt(to) - XAt(from) < 3 ? null : (from, to);
            }
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (range is null) return;
            Range = null;
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Seek(int x)
        {
            Position = TimeAt(x);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A duration in seconds, or milliseconds below one: "12.3 s", "250 ms".</summary>
        public static string Seconds(double seconds) => seconds >= 1 || seconds == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{seconds:0.0} s")
            : string.Create(CultureInfo.CurrentCulture, $"{seconds * 1000:0.##} ms");

        /// <summary>m:ss.f, or h:mm:ss.f past an hour.</summary>
        public static string Format(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss\.f", CultureInfo.CurrentCulture)
                : span.ToString(@"m\:ss\.f", CultureInfo.CurrentCulture);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
