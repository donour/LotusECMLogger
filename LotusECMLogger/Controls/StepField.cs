using System.ComponentModel;

namespace LotusECMLogger.Controls;

/// <summary>
/// A compact segmented input: a borderless text box with small buttons attached on either
/// side, all inside one border, so the whole thing reads as a single control
/// (e.g. <c>[ − | 1 | + ]</c> or <c>[ 12.5 | ✓ | × ]</c>). Ported from RaiderLite.
/// </summary>
public sealed class StepField : Control
{
    private static readonly Color BorderColor = Color.FromArgb(138, 143, 152);
    private static readonly Color FocusColor = Color.FromArgb(0, 103, 192);
    private static readonly Color DividerColor = Color.FromArgb(218, 220, 224);
    private static readonly Color DisabledBack = Color.FromArgb(243, 243, 243);

    private readonly List<FieldButton> _left = [];
    private readonly List<FieldButton> _right = [];
    private readonly int _boxWidth;
    private readonly int _buttonWidth;
    private readonly ToolTip _tips = new();
    private Color _fieldColor = Color.White;

    public StepField(int boxWidth, int height, int buttonWidth)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        _boxWidth = boxWidth;
        _buttonWidth = buttonWidth;
        Height = height;
        BackColor = Color.White;
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        // The wheel steps fields with buttons on both sides ([ - | n | + ]); on a value field
        // ([ value | set | x ]) it must not press "set".
        Box.MouseWheel += (_, e) =>
        {
            if (_left.Count > 0 && _right.Count > 0)
                (e.Delta > 0 ? _right[0] : _left[0]).PerformClick();
        };
        Controls.Add(Box);
        UpdateWidth();
    }

    /// <summary>The text box inside the field.</summary>
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        TextAlign = HorizontalAlignment.Right,
        BackColor = Color.White,
    };

    /// <summary>Background of the field (e.g. tinted to show a changed value).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FieldColor
    {
        get => _fieldColor;
        set
        {
            _fieldColor = value;
            Box.BackColor = Enabled ? value : DisabledBack;
            Invalidate(true);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ToolTipText
    {
        set => _tips.SetToolTip(Box, value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tips.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Adds a button before (<paramref name="left"/>) or after the text box.</summary>
    public void AddButton(bool left, Image image, string tip, Action click)
    {
        var button = new FieldButton(image);
        button.Click += (_, _) => click();
        _tips.SetToolTip(button, tip);
        (left ? _left : _right).Add(button);
        Controls.Add(button);
        UpdateWidth();
    }

    private void UpdateWidth()
    {
        int buttons = (_left.Count + _right.Count) * (_buttonWidth + 1);
        Width = 2 + buttons + _boxWidth + 8;
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int h = Height;
        int x = 1;
        foreach (var b in _left)
        {
            b.SetBounds(x, 1, _buttonWidth, h - 2);
            x += _buttonWidth + 1;
        }
        int boxHeight = Box.PreferredHeight;
        Box.SetBounds(x + 4, Math.Max(1, (h - boxHeight) / 2), _boxWidth, boxHeight);
        x += _boxWidth + 8;
        foreach (var b in _right)
        {
            x += 1;
            b.SetBounds(x, 1, _buttonWidth, h - 2);
            x += _buttonWidth;
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Box.BackColor = Enabled ? _fieldColor : DisabledBack;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var back = new SolidBrush(Enabled ? _fieldColor : DisabledBack))
            g.FillRectangle(back, ClientRectangle);

        // Dividers between the text box and its buttons.
        using (var divider = new Pen(DividerColor))
        {
            foreach (var b in _left)
                g.DrawLine(divider, b.Right, 3, b.Right, Height - 4);
            foreach (var b in _right)
                g.DrawLine(divider, b.Left - 1, 3, b.Left - 1, Height - 4);
        }

        var border = !Enabled ? DividerColor : Box.Focused ? FocusColor : BorderColor;
        using var pen = new Pen(border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        if (Box.Focused && Enabled)
        {
            // A heavier bottom edge marks the field being typed in.
            g.DrawLine(pen, 1, Height - 2, Width - 2, Height - 2);
        }
    }

    /// <summary>A flat icon button inside a <see cref="StepField"/>. It never takes focus.</summary>
    private sealed class FieldButton : Control
    {
        private static readonly Color Hover = Color.FromArgb(224, 238, 250);
        private static readonly Color Pressed = Color.FromArgb(200, 225, 246);
        private readonly Image _image;
        private readonly Image _disabled;
        private bool _hover;
        private bool _pressed;

        public FieldButton(Image image)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            _image = image;
            _disabled = ToolStripRenderer.CreateDisabledImage(image);
            Cursor = Cursors.Hand;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disabled.Dispose();
            base.Dispose(disposing);
        }

        public void PerformClick()
        {
            if (Enabled)
                OnClick(EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = e.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var field = (Parent as StepField)?.FieldColor ?? Color.White;
            var back = !Enabled ? field
                : _pressed ? Pressed
                : _hover ? Hover
                : field;
            if (Parent is { Enabled: false })
                back = DisabledBack;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            var img = Enabled ? _image : _disabled;
            e.Graphics.DrawImage(img, (Width - img.Width) / 2, (Height - img.Height) / 2, img.Width, img.Height);
        }
    }
}
