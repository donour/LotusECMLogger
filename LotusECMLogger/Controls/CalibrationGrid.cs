using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    internal enum CellRole { Empty, Data, XAxis, YAxis }

    /// <summary>A grid position's meaning: a data cell (item x, y), or point <c>Index</c> of the X or Y axis.</summary>
    internal readonly record struct GridCell(CellRole Role, int X, int Y)
    {
        public int Index => Role == CellRole.YAxis ? Y : X;
    }

    /// <summary>Keyboard commands the grid forwards to the editor (RaiderLite's shortcuts).</summary>
    internal enum GridCommand
    {
        IncrementCoarse, DecrementCoarse, IncrementFine, DecrementFine, Multiply, Copy, Paste,
        Interpolate, VerticalInterpolate, HorizontalInterpolate, Undo, Redo, ToggleGraph,
    }

    /// <summary>
    /// One calibration item as a grid, after RaiderLite's TableGrid: a map has its X axis frozen across the top and
    /// its Y axis down the left, a curve its axis above the values; axes, value blocks and single values are rows of
    /// cells. Values are not edited in place: typing starts value entry in the editor's toolbar. Cells are coloured
    /// min → max, outlined red or blue where raised or lowered from the file, and marked in the corner by their ECU
    /// sync state (orange pending, blue writing, red failed).
    /// </summary>
    internal sealed class CalibrationGrid : DataGridView
    {
        public const int BlockColumns = 16;
        private static readonly Color PendingMark = Color.DarkOrange;
        private static readonly Color WritingMark = Color.RoyalBlue;
        private static readonly Color FailedMark = Color.Firebrick;

        private readonly CalibrationImage image;
        private readonly A2lCalibrationItem item;
        private readonly Func<uint, int, CalibrationSyncState?> syncState;
        private readonly Font regular;
        private readonly Font bold;
        private GridCell[,] map = new GridCell[0, 0];
        private double min, max;
        private double[]? xAxis, yAxis;
        private bool paintFailed;
        private OperatingPointSample? cursor;
        private readonly List<(double X, double Y)> trail = [];
        private const int TrailLength = 24;

        public CalibrationGrid(CalibrationImage image, A2lCalibrationItem item, Func<uint, int, CalibrationSyncState?> syncState)
        {
            (this.image, this.item, this.syncState) = (image, item, syncState);
            regular = new Font("Segoe UI", 9F);
            bold = new Font("Segoe UI", 9F, FontStyle.Bold);
            DoubleBuffered = true;
            VirtualMode = true;
            ReadOnly = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            RowHeadersVisible = false;
            ColumnHeadersVisible = false;
            SelectionMode = DataGridViewSelectionMode.CellSelect;
            MultiSelect = true;
            BorderStyle = BorderStyle.None;
            BackgroundColor = SystemColors.Window;
            CellBorderStyle = DataGridViewCellBorderStyle.None;
            ShowCellToolTips = true;
            StandardTab = true;
            EditMode = DataGridViewEditMode.EditProgrammatically;
            DefaultCellStyle.Font = regular;
            DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            BuildLayout();
        }

        public A2lCalibrationItem Item => item;

        /// <summary>Show each cell's change from the file instead of its value.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowChanges { get; set; }

        public event EventHandler<GridCommand>? Command;

        /// <summary>Raised when a value-entry key (digit, '.', ',', '-') is typed.</summary>
        public event EventHandler<char>? ValueEntryStarted;

        public GridCell GetGridCell(int row, int column) =>
            row >= 0 && column >= 0 && row < map.GetLength(0) && column < map.GetLength(1) ? map[row, column] : default;

        public GridCell CurrentGridCell => CurrentCell is null ? default : GetGridCell(CurrentCell.RowIndex, CurrentCell.ColumnIndex);

        private bool HasXAxis => item.Kind is A2lItemKind.Map or A2lItemKind.Curve;

        private void BuildLayout()
        {
            switch (item.Kind)
            {
                case A2lItemKind.Map:
                    map = new GridCell[item.CountY + 1, item.CountX + 1];
                    for (int x = 0; x < item.CountX; x++) map[0, x + 1] = new GridCell(CellRole.XAxis, x, 0);
                    for (int y = 0; y < item.CountY; y++)
                    {
                        map[y + 1, 0] = new GridCell(CellRole.YAxis, 0, y);
                        for (int x = 0; x < item.CountX; x++) map[y + 1, x + 1] = new GridCell(CellRole.Data, x, y);
                    }
                    break;
                case A2lItemKind.Curve:
                    map = new GridCell[2, item.CountX];
                    for (int x = 0; x < item.CountX; x++)
                    {
                        map[0, x] = new GridCell(CellRole.XAxis, x, 0);
                        map[1, x] = new GridCell(CellRole.Data, x, 0);
                    }
                    break;
                case A2lItemKind.Value or A2lItemKind.Ascii:
                    map = new GridCell[1, 1];
                    map[0, 0] = new GridCell(CellRole.Data, 0, 0);
                    break;
                default: // value blocks and stand-alone axes wrap at 16
                    int columns = Math.Min(BlockColumns, item.ElementCount);
                    map = new GridCell[(item.ElementCount + columns - 1) / columns, columns];
                    for (int i = 0; i < item.ElementCount; i++) map[i / columns, i % columns] = new GridCell(CellRole.Data, i, 0);
                    break;
            }
            Columns.Clear();
            for (int c = 0; c < map.GetLength(1); c++)
                Columns.Add(new DataGridViewTextBoxColumn { SortMode = DataGridViewColumnSortMode.NotSortable });
            RowCount = map.GetLength(0);
            RowTemplate.Height = 22;
            foreach (DataGridViewRow row in Rows) row.Height = 22;
            // Keep the axes in view when the grid scrolls.
            if (HasXAxis && RowCount > 1) Rows[0].Frozen = true;
            if (item.Kind == A2lItemKind.Map && Columns.Count > 1) Columns[0].Frozen = true;
            RefreshValues();
            AutoSizeCells();
        }

        /// <summary>Re-reads values and axes, recomputes the colour range and repaints. Call after values change.</summary>
        public void RefreshValues()
        {
            min = double.MaxValue;
            max = double.MinValue;
            if (item.Kind != A2lItemKind.Ascii)
            {
                for (int y = 0; y < item.CountY; y++)
                    for (int x = 0; x < item.CountX; x++)
                    {
                        double v = Physical(x, y);
                        if (!double.IsFinite(v)) continue;
                        min = Math.Min(min, v);
                        max = Math.Max(max, v);
                    }
            }
            xAxis = HasXAxis ? image.ReadAxisPhysical(item.Axes[0], item.ByteOrder) : null;
            yAxis = item.Kind == A2lItemKind.Map ? image.ReadAxisPhysical(item.Axes[1], item.ByteOrder) : null;
            Invalidate();
        }

        /// <summary>Sizes columns to fit their widest text.</summary>
        public void AutoSizeCells()
        {
            for (int c = 0; c < map.GetLength(1); c++)
            {
                int width = item.Kind == A2lItemKind.Ascii ? 320 : 46;
                for (int r = 0; r < map.GetLength(0); r++)
                {
                    var gc = map[r, c];
                    if (gc.Role == CellRole.Empty) continue;
                    var size = TextRenderer.MeasureText(GetText(gc), gc.Role == CellRole.Data ? regular : bold);
                    width = Math.Max(width, size.Width + 12);
                }
                Columns[c].Width = width;
            }
        }

        /// <summary>The size that shows every cell without scrolling.</summary>
        public Size ContentSize => new(Columns.Cast<DataGridViewColumn>().Sum(c => c.Width) + 2,
            Rows.Cast<DataGridViewRow>().Sum(r => r.Height) + 2);

        private double Physical(int x, int y) => image.ReadPhysical(item, x, y);

        private double Original(int x, int y) => item.Conversion.ToPhysical(image.ReadOriginalRaw(item, x, y));

        private A2lAxisLayout? AxisOf(GridCell gc) => gc.Role switch
        {
            CellRole.XAxis when HasXAxis => item.Axes[0],
            CellRole.YAxis when item.Kind == A2lItemKind.Map => item.Axes[1],
            _ => null,
        };

        /// <summary>The bytes behind a grid cell: a data cell, or a stored axis point; null for fixed axes and blanks.</summary>
        public (uint Address, int Length)? BytesOf(GridCell gc) => gc.Role switch
        {
            CellRole.Data => (item.CellAddress(gc.X, gc.Y), item.ElementSize),
            CellRole.XAxis or CellRole.YAxis when AxisOf(gc) is { IsStored: true } axis => (axis.PointAddress(gc.Index), axis.Type.Size()),
            _ => null,
        };

        public string GetText(GridCell gc)
        {
            switch (gc.Role)
            {
                case CellRole.Empty:
                    return "";
                case CellRole.XAxis or CellRole.YAxis:
                    var values = gc.Role == CellRole.XAxis ? xAxis : yAxis;
                    return values is null || gc.Index >= values.Length ? "" : Format(values[gc.Index], AxisOf(gc)!.Conversion);
            }
            if (item.Kind == A2lItemKind.Ascii) return image.ReadText(item);
            double value = Physical(gc.X, gc.Y);
            if (!ShowChanges) return item.Conversion.ToText(image.ReadRaw(item, gc.X, gc.Y)) ?? Format(value, item.Conversion);
            double change = value - Original(gc.X, gc.Y);
            return change == 0 ? "" : (change > 0 ? "+" : "") + Format(change, item.Conversion);
        }

        public static string Format(double value, A2lCompuMethod conversion) =>
            value.ToString("F" + (TableEdits.Decimals(conversion.Format) ?? 3), CultureInfo.CurrentCulture);

        protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
        {
            e.Value = GetText(GetGridCell(e.RowIndex, e.ColumnIndex));
            base.OnCellValueNeeded(e);
        }

        protected override void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e)
        {
            var gc = GetGridCell(e.RowIndex, e.ColumnIndex);
            if (gc.Role == CellRole.Data && item.Kind != A2lItemKind.Ascii)
            {
                double raw = image.ReadRaw(item, gc.X, gc.Y);
                e.ToolTipText = $"{Format(Physical(gc.X, gc.Y), item.Conversion)} {item.Conversion.Unit}\n" +
                    $"Raw: {raw.ToString(CultureInfo.CurrentCulture)} (0x{(long)raw & 0xFFFFFFFF:X})\n" +
                    $"Address: 0x{item.CellAddress(gc.X, gc.Y):X8}\n" +
                    $"File: {Format(Original(gc.X, gc.Y), item.Conversion)}" +
                    (syncState(item.CellAddress(gc.X, gc.Y), item.ElementSize) is { } s ? $"\nECU: {Describe(s)}" : "");
            }
            else if (AxisOf(gc) is { } axis)
            {
                e.ToolTipText = $"{GetText(gc)} {axis.Conversion.Unit}\n{axis.AxisPtsName ?? "fixed axis"}" +
                    (axis.InputQuantity is { } input ? $"\nInput: {input}" : "\nNo input bound");
            }
            base.OnCellToolTipTextNeeded(e);
        }

        public static string Describe(CalibrationSyncState state) => state switch
        {
            CalibrationSyncState.InSync => "in sync",
            CalibrationSyncState.Pending => "pending",
            CalibrationSyncState.Writing => "writing",
            _ => "write failed",
        };

        private Color BackgroundFor(GridCell gc)
        {
            if (gc.Role == CellRole.Empty) return SystemColors.Control;
            if (gc.Role != CellRole.Data || item.Kind == A2lItemKind.Ascii) return PlotStyle.Axis;
            double value = Physical(gc.X, gc.Y);
            if (ShowChanges)
            {
                double change = value - Original(gc.X, gc.Y);
                return change == 0 ? SystemColors.Window : change > 0 ? Color.FromArgb(255, 214, 214) : Color.FromArgb(214, 224, 255);
            }
            return PlotStyle.Heat(max > min ? (value - min) / (max - min) : 0.5);
        }

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            base.OnCellPainting(e);
            if (paintFailed || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null) return;
            try
            {
                PaintCell(e, e.Graphics);
            }
            catch (Exception ex)
            {
                // Reporting from inside painting would repeat on every repaint: log once and fall back to the
                // standard cell painting for the rest of this grid's life.
                Debug.WriteLine($"CalibrationGrid: painting failed: {ex}");
                paintFailed = true;
                e.Handled = false;
                Invalidate();
            }
        }

        private void PaintCell(DataGridViewCellPaintingEventArgs e, Graphics graphics)
        {
            var gc = GetGridCell(e.RowIndex, e.ColumnIndex);
            var bounds = e.CellBounds;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0 && gc.Role != CellRole.Empty;
            var back = selected ? PlotStyle.Selection : BackgroundFor(gc);
            using (var brush = new SolidBrush(back)) graphics.FillRectangle(brush, bounds);
            using (var pen = new Pen(gc.Role == CellRole.Empty ? SystemColors.Control : Color.FromArgb(90, 90, 90)))
                graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

            if (BytesOf(gc) is { } bytes)
            {
                var (address, length) = bytes;
                // Raised or lowered from the file: a red or blue border, as in RaiderLite.
                var now = image.Bytes(address, length);
                var was = image.OriginalBytes(address, length);
                if (!now.SequenceEqual(was))
                {
                    var type = gc.Role == CellRole.Data ? item.ValueType : AxisOf(gc)!.Type;
                    bool up = type.ReadRaw(now, item.ByteOrder) > type.ReadRaw(was, item.ByteOrder);
                    using var pen = new Pen(up ? PlotStyle.Increased : PlotStyle.Decreased, 2);
                    graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
                }
                if (syncState(address, length) is { } state and not CalibrationSyncState.InSync)
                {
                    var mark = state switch { CalibrationSyncState.Pending => PendingMark, CalibrationSyncState.Writing => WritingMark, _ => FailedMark };
                    int size = Math.Min(9, bounds.Height / 2);
                    using var brush = new SolidBrush(mark);
                    graphics.FillPolygon(brush, new[]
                    {
                        new Point(bounds.Right - 1 - size, bounds.Top + 1), new Point(bounds.Right - 1, bounds.Top + 1),
                        new Point(bounds.Right - 1, bounds.Top + 1 + size),
                    });
                }
            }

            if (gc.Role != CellRole.Empty)
            {
                var font = gc.Role == CellRole.Data ? regular : bold;
                var fore = selected ? Color.White : ColorScaler.ContrastText(back);
                TextRenderer.DrawText(graphics, GetText(gc), font, bounds, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (Focused && CurrentCell is { } current && current.RowIndex == e.RowIndex && current.ColumnIndex == e.ColumnIndex)
            {
                using var pen = new Pen(Color.Black) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                graphics.DrawRectangle(pen, bounds.X + 2, bounds.Y + 2, bounds.Width - 5, bounds.Height - 5);
            }
            e.Handled = true;
        }

        // ── Selection ────────────────────────────────────────────────────────────────────────────────────

        private IEnumerable<GridCell> SelectedGridCells() =>
            SelectedCells.Cast<DataGridViewCell>().Select(c => GetGridCell(c.RowIndex, c.ColumnIndex));

        /// <summary>The selected data cells (x, y).</summary>
        public List<(int X, int Y)> SelectedData() =>
            SelectedGridCells().Where(c => c.Role == CellRole.Data).Select(c => (c.X, c.Y)).Distinct().ToList();

        /// <summary>When only axis points are selected, all on one axis: that axis (0 = X, 1 = Y) and the points.</summary>
        public (int Axis, List<int> Points)? SelectedAxisPoints()
        {
            var cells = SelectedGridCells().Where(c => c.Role != CellRole.Empty).ToList();
            if (cells.Count == 0 || cells.Any(c => c.Role == CellRole.Data)) return null;
            var roles = cells.Select(c => c.Role).Distinct().ToList();
            if (roles.Count != 1) return null;
            return (roles[0] == CellRole.XAxis ? 0 : 1, cells.Select(c => c.Index).Distinct().Order().ToList());
        }

        public bool IsSelected(int x, int y) => SelectedGridCells().Any(c => c.Role == CellRole.Data && c.X == x && c.Y == y);

        /// <summary>Selects data cells, the first becoming the current cell.</summary>
        public void SelectData(IEnumerable<(int X, int Y)> cells)
        {
            ClearSelection();
            bool first = true;
            foreach (var (x, y) in cells)
            {
                if (Find(CellRole.Data, x, y) is not { } at) continue;
                if (first) CurrentCell = Rows[at.Y].Cells[at.X];
                Rows[at.Y].Cells[at.X].Selected = true;
                first = false;
            }
        }

        /// <summary>Selects every data cell (not axes).</summary>
        public void SelectAllData() => SelectData(Enumerable.Range(0, item.CountY).SelectMany(y => Enumerable.Range(0, item.CountX).Select(x => (x, y))));

        private Point? Find(CellRole role, int x, int y)
        {
            for (int r = 0; r < map.GetLength(0); r++)
                for (int c = 0; c < map.GetLength(1); c++)
                    if (map[r, c].Role == role && map[r, c].X == x && map[r, c].Y == y) return new Point(c, r);
            return null;
        }

        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            // Clicking the empty corner of a map selects all its data.
            if (e.Button == MouseButtons.Left && item.Kind == A2lItemKind.Map && GetGridCell(e.RowIndex, e.ColumnIndex).Role == CellRole.Empty)
            {
                SelectAllData();
                return;
            }
            base.OnCellMouseDown(e);
        }

        // ── Keyboard ─────────────────────────────────────────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            GridCommand? command = keyData switch
            {
                Keys.Control | Keys.Up => GridCommand.IncrementCoarse,
                Keys.Control | Keys.Down => GridCommand.DecrementCoarse,
                Keys.Control | Keys.Shift | Keys.Up => GridCommand.IncrementFine,
                Keys.Control | Keys.Shift | Keys.Down => GridCommand.DecrementFine,
                Keys.Control | Keys.Oemplus or Keys.Control | Keys.Add => GridCommand.IncrementFine,
                Keys.Control | Keys.OemMinus or Keys.Control | Keys.Subtract => GridCommand.DecrementFine,
                Keys.Control | Keys.Enter => GridCommand.Multiply,
                Keys.Control | Keys.C or Keys.Control | Keys.Insert => GridCommand.Copy,
                Keys.Control | Keys.V or Keys.Shift | Keys.Insert => GridCommand.Paste,
                Keys.Shift | Keys.I => GridCommand.Interpolate,
                Keys.Shift | Keys.V => GridCommand.VerticalInterpolate,
                Keys.Shift | Keys.H => GridCommand.HorizontalInterpolate,
                Keys.Control | Keys.Z => GridCommand.Undo,
                Keys.Control | Keys.Y or Keys.Control | Keys.Shift | Keys.Z => GridCommand.Redo,
                Keys.Control | Keys.G => GridCommand.ToggleGraph,
                _ => null,
            };
            if (command is { } c)
            {
                Command?.Invoke(this, c);
                return true;
            }
            if (keyData == (Keys.Control | Keys.A))
            {
                SelectAllData();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            GridCommand? command = e.KeyChar switch
            {
                '+' => GridCommand.IncrementCoarse,
                '_' => GridCommand.DecrementCoarse,
                ']' => GridCommand.IncrementFine,
                '[' => GridCommand.DecrementFine,
                '*' => GridCommand.Multiply,
                _ => null,
            };
            if (command is { } c)
            {
                Command?.Invoke(this, c);
                e.Handled = true;
                return;
            }
            if (char.IsDigit(e.KeyChar) || e.KeyChar is '.' or ',' or '-')
            {
                ValueEntryStarted?.Invoke(this, e.KeyChar);
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        // ── Live operating point ─────────────────────────────────────────────────────────────────────────

        /// <summary>The live operating point drawn over the grid; null hides it and clears the trail.</summary>
        public OperatingPointSample? OperatingPoint => cursor;

        public void SetCursor(OperatingPointSample? sample)
        {
            cursor = sample;
            if (sample is null) trail.Clear();
            else if (sample.X is not null || sample.Y is not null)
            {
                var at = (sample.X?.At ?? double.NaN, sample.Y?.At ?? double.NaN);
                if (trail.Count == 0 || trail[^1] != at) trail.Add(at);
                if (trail.Count > TrailLength) trail.RemoveAt(0);
            }
            Invalidate();
        }

        /// <summary>The on-screen rectangle of data cell (x, y), or null when it is scrolled out of view.</summary>
        private Rectangle? DataCellBounds(int x, int y)
        {
            if (Find(CellRole.Data, Math.Clamp(x, 0, item.CountX - 1), Math.Clamp(y, 0, item.CountY - 1)) is not { } at) return null;
            var bounds = GetCellDisplayRectangle(at.X, at.Y, false);
            return bounds.IsEmpty ? null : bounds;
        }

        /// <summary>The pixel centre of a fractional data position, interpolated between neighbouring cell centres.</summary>
        private PointF? CentreOf(double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            if (DataCellBounds(x0, y0) is not { } a) return null;
            var b = DataCellBounds(x0 + 1, y0) ?? a;
            var c = DataCellBounds(x0, y0 + 1) ?? a;
            float cx = a.X + a.Width / 2f + (float)(x - x0) * ((b.X + b.Width / 2f) - (a.X + a.Width / 2f));
            float cy = a.Y + a.Height / 2f + (float)(y - y0) * ((c.Y + c.Height / 2f) - (a.Y + a.Height / 2f));
            return new PointF(cx, cy);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (cursor is not { } sample || item.Kind is not (A2lItemKind.Map or A2lItemKind.Curve) || paintFailed) return;
            try
            {
                PaintCursor(e.Graphics, sample);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CalibrationGrid: drawing the cursor failed: {ex}");
                cursor = null;
            }
        }

        private void PaintCursor(Graphics g, OperatingPointSample sample)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool curve = item.Kind == A2lItemKind.Curve;
            var x = sample.X;
            var y = curve ? new AxisPosition(0, 0) : sample.Y;
            using var pen = new Pen(PlotStyle.Cursor, 2);
            // Data area: below the frozen X axis row, right of the frozen Y axis column.
            var first = DataCellBounds(0, 0);
            int top = first?.Top ?? (HasXAxis && RowCount > 0 ? Rows[0].Height : 0);
            int left = item.Kind == A2lItemKind.Map && ColumnCount > 0 ? Columns[0].Width : 0;
            var area = Rectangle.FromLTRB(left, top, ClientSize.Width, ClientSize.Height);
            g.SetClip(area);

            if (x is { } px && y is { } py)
            {
                // The cells the lookup interpolates between, then the trail and the point itself.
                int x1 = px.Fraction > 0 ? px.Index + 1 : px.Index, y1 = py.Fraction > 0 ? py.Index + 1 : py.Index;
                if (DataCellBounds(px.Index, py.Index) is { } a && DataCellBounds(x1, y1) is { } b)
                    g.DrawRectangle(pen, Rectangle.FromLTRB(a.Left + 1, a.Top + 1, b.Right - 2, b.Bottom - 2));
                for (int i = 0; i < trail.Count - 1; i++)
                {
                    var (tx, ty) = trail[i];
                    if (double.IsNaN(tx) || (!curve && double.IsNaN(ty)) || CentreOf(tx, curve ? 0 : ty) is not { } t) continue;
                    using var fade = new SolidBrush(Color.FromArgb(40 + 160 * i / trail.Count, PlotStyle.Cursor));
                    g.FillEllipse(fade, t.X - 2.5f, t.Y - 2.5f, 5, 5);
                }
                if (CentreOf(px.At, py.At) is { } p)
                {
                    using var fill = new SolidBrush(PlotStyle.Cursor);
                    g.FillEllipse(fill, p.X - 5, p.Y - 5, 10, 10);
                    g.DrawEllipse(Pens.White, p.X - 5, p.Y - 5, 10, 10);
                }
            }
            else if (x is { } onlyX && CentreOf(onlyX.At, 0) is { } vx)
                g.DrawLine(pen, vx.X, area.Top, vx.X, area.Bottom);       // only X is bound: the column band
            else if (y is { } onlyY && CentreOf(0, onlyY.At) is { } hy)
                g.DrawLine(pen, area.Left, hy.Y, area.Right, hy.Y);       // only Y is bound: the row band
            g.ResetClip();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                regular.Dispose();
                bold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
