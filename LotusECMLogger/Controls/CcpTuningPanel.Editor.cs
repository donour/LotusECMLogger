using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls
{
    /// <summary>The Tuning page's grid, status line and edits.</summary>
    internal sealed partial class CcpTuningPanel
    {
        // ── Editor ───────────────────────────────────────────────────────────────────────────────────────

        private void ShowItem(A2lCalibrationItem? item)
        {
            if (ReferenceEquals(item, current) && item is not null) return;
            current = item;
            if (grid is not null)
            {
                editorSplit.Panel1.Controls.Remove(grid);
                grid.Dispose();
                grid = null;
            }
            if (item is null || image is null)
            {
                titleLabel.Text = image is null ? "Open a calibration file to start." : a2l is null ? "No A2L loaded." : "Pick a table.";
                descriptionLabel.Text = "";
                graphPane.SetData(graphData = null);
                EnsureCursor();
                LayoutEditor();
                UpdateState();
                return;
            }
            grid = new CalibrationGrid(image, item, SyncState) { Dock = DockStyle.Fill, ShowChanges = changesBox.Checked };
            grid.Command += (_, command) => RunCommand(command);
            grid.ValueEntryStarted += (_, ch) => BeginValueEntry(ch);
            grid.SelectionChanged += (_, _) => OnGridSelectionChanged();
            grid.CurrentCellChanged += (_, _) => ShowStatus();
            editorSplit.Panel1.Controls.Add(grid);

            titleLabel.Text = $"{item.Name}  ·  {Shape(item)}{(item.IsEditable ? "" : "  ·  read-only")}";
            descriptionLabel.Text = item.LongIdentifier;
            toolTip.SetToolTip(descriptionLabel, Wrap(item.LongIdentifier));
            double step = TableEdits.RawStep(item.Conversion);
            fineField.Box.Text = step.ToString("G4", CultureInfo.CurrentCulture);
            coarseField.Box.Text = (step * 10).ToString("G4", CultureInfo.CurrentCulture);
            graphPane.SetData(graphData = GraphData.From(image, item));
            graphPane.Plot.SetSelection(grid.IsSelected);
            EnsureCursor();
            _ = UpdateActivityAsync();
            LayoutEditor();
            grid.ClearSelection();
            ShowStatus();
            UpdateState();
        }

        /// <summary>Graph to the right of a map, below a curve, hidden for single values or when switched off.</summary>
        private void LayoutEditor()
        {
            bool graph = graphBox.Checked && current is not null && GraphData.CanGraph(current);
            editorSplit.Panel2Collapsed = !graph;
            if (!graph || grid is null || editorSplit.Width <= 0 || editorSplit.Height <= 0) return;
            var content = grid.ContentSize;
            if (current!.Kind == A2lItemKind.Map)
            {
                editorSplit.Orientation = Orientation.Vertical;
                int wanted = content.Width + SystemInformation.VerticalScrollBarWidth + 4;
                // The grid gets what it needs up to 60%; the graph keeps the rest.
                editorSplit.SplitterDistance = Math.Clamp(wanted, editorSplit.Width * 30 / 100, editorSplit.Width * 60 / 100);
            }
            else
            {
                editorSplit.Orientation = Orientation.Horizontal;
                int wanted = content.Height + SystemInformation.HorizontalScrollBarHeight + 4;
                editorSplit.SplitterDistance = Math.Clamp(wanted, 50, Math.Max(50, editorSplit.Height - 150));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated) BeginInvoke(LayoutEditor);
        }

        private void OnGridSelectionChanged()
        {
            graphPane.Plot.Invalidate();
            ShowStatus();
            UpdateEditButtons();
        }

        /// <summary>The current cell, described as in RaiderLite's status bar.</summary>
        private void ShowStatus()
        {
            if (grid is null || current is null || image is null || grid.CurrentCell is null)
            {
                statusLabel.Text = "";
                return;
            }
            var gc = grid.CurrentGridCell;
            var parts = new List<string>();
            if (gc.Role == CellRole.Data && current.Kind != A2lItemKind.Ascii)
            {
                if (current.Kind is A2lItemKind.Map or A2lItemKind.Curve)
                    parts.Add($"X: {AxisText(0, gc.X)}");
                if (current.Kind == A2lItemKind.Map)
                    parts.Add($"Y: {AxisText(1, gc.Y)}");
                double raw = image.ReadRaw(current, gc.X, gc.Y);
                parts.Add($"{grid.GetText(gc)} {current.Conversion.Unit}".TrimEnd());
                parts.Add($"raw {raw.ToString(CultureInfo.CurrentCulture)} (0x{(long)raw & 0xFFFFFFFF:X})");
                parts.Add($"@0x{current.CellAddress(gc.X, gc.Y):X8}");
                if (SyncState(current.CellAddress(gc.X, gc.Y), current.ElementSize) is { } state)
                    parts.Add($"ECU {CalibrationGrid.Describe(state)}");
            }
            else if (gc.Role is CellRole.XAxis or CellRole.YAxis)
            {
                var axis = current.Axes[gc.Role == CellRole.XAxis ? 0 : 1];
                parts.Add($"{GraphData.AxisTitle(axis)} point {gc.Index}: {grid.GetText(gc)}");
                parts.Add(axis.InputQuantity is { } input ? $"input {input}" : "no input bound");
            }
            int selected = grid.SelectedData().Count;
            if (selected > 1) parts.Add($"{selected} cells selected");
            statusLabel.Text = string.Join("    ", parts);
        }

        private string AxisText(int axis, int index)
        {
            var layout = current!.Axes[axis];
            var values = image!.ReadAxisPhysical(layout, current.ByteOrder);
            return $"{CalibrationGrid.Format(values[index], layout.Conversion)} {layout.Conversion.Unit}".TrimEnd();
        }

        private void BeginValueEntry(char first)
        {
            if (!valueField.Enabled) return;
            valueField.Box.Text = first.ToString();
            valueField.Box.Focus();
            valueField.Box.SelectionStart = valueField.Box.Text.Length;
        }

        private void OnValueKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyValue(valueField.Box.Text);
                grid?.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                grid?.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        // ── Edits ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// What an edit changes: the selected data cells, or (when only axis points are selected) the shared axis item.
        /// Axes and values are read as [y, x] physical values.
        /// </summary>
        private (A2lCalibrationItem Item, List<(int X, int Y)> Cells, IReadOnlyList<double>? XAxis, IReadOnlyList<double>? YAxis)? Target()
        {
            // A replay shows what the ECU held; it is not edited.
            if (grid is null || current is null || image is null || recording is not null) return null;
            var data = grid.SelectedData();
            if (data.Count > 0)
            {
                if (current.Kind == A2lItemKind.Ascii) return null;
                return (current, data, AxisPositions(0), AxisPositions(1));
            }
            if (grid.SelectedAxisPoints() is { } axis && current.Axes[axis.Axis].AxisPtsName is { } name &&
                a2l?.Items.TryGetValue(name, out var axisItem) == true)
                return (axisItem, axis.Points.Select(p => (p, 0)).ToList(), null, null);
            return null;
        }

        private double[]? AxisPositions(int index) =>
            current is { Kind: A2lItemKind.Map or A2lItemKind.Curve } item && index < item.Axes.Count
                ? image!.ReadAxisPhysical(item.Axes[index], item.ByteOrder) : null;

        private double[,] Values(A2lCalibrationItem item)
        {
            var values = new double[item.CountY, item.CountX];
            for (int y = 0; y < item.CountY; y++)
                for (int x = 0; x < item.CountX; x++)
                    values[y, x] = image!.ReadPhysical(item, x, y);
            return values;
        }

        private delegate IReadOnlyList<CellValue> EditMaker(double[,] values, List<(int X, int Y)> cells, CellRange range,
            IReadOnlyList<double>? xAxis, IReadOnlyList<double>? yAxis);

        private void Edit(string what, EditMaker make)
        {
            if (Target() is not { } target)
            {
                context.Log(grid?.SelectedAxisPoints() is not null ? "That axis has fixed points, so it can't be edited." : "Select cells first.");
                return;
            }
            if (!target.Item.IsEditable)
            {
                context.Log($"{target.Item.Name} is read-only: {target.Item.ReadOnlyReason}");
                return;
            }
            try
            {
                var cells = make(Values(target.Item), target.Cells, CellRange.Bounding(target.Cells), target.XAxis, target.YAxis);
                image!.SetPhysical(target.Item, cells, $"{what} {target.Cells.Count} cell(s) of {target.Item.Name}");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                context.Log($"{what} refused: {ex.Message}");
            }
        }

        private static double? Parse(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ? v
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : null;

        private double StepSize(StepField field, double multiple) =>
            Parse(field.Box.Text) is { } v && v > 0 ? v : TableEdits.RawStep(current?.Conversion ?? A2lCompuMethod.Identity) * multiple;

        /// <summary>Sets the selection to a typed value.</summary>
        internal void ApplyValue(string text)
        {
            if (Parse(text) is not { } value)
            {
                if (text.Trim().Length > 0) context.Log($"'{text}' is not a number.");
                return;
            }
            Edit("Set", (values, cells, _, _, _) => TableEdits.Arithmetic(values, cells, TableOperation.Set, value));
        }

        internal void RunCommand(GridCommand command)
        {
            switch (command)
            {
                case GridCommand.IncrementFine: AddStep(StepSize(fineField, 1)); break;
                case GridCommand.DecrementFine: AddStep(-StepSize(fineField, 1)); break;
                case GridCommand.IncrementCoarse: AddStep(StepSize(coarseField, 10)); break;
                case GridCommand.DecrementCoarse: AddStep(-StepSize(coarseField, 10)); break;
                case GridCommand.Multiply:
                    if (Parse(valueField.Box.Text) is { } factor)
                        Edit($"Multiply by {factor.ToString(CultureInfo.CurrentCulture)}", (values, cells, _, _, _) => TableEdits.Arithmetic(values, cells, TableOperation.Multiply, factor));
                    else context.Log("Type a factor in the Value box to multiply by.");
                    break;
                case GridCommand.Interpolate:
                    Edit("Interpolate", (values, _, range, x, y) => TableEdits.Interpolate(values, range, x, y));
                    break;
                case GridCommand.VerticalInterpolate:
                    Edit("Vertical interpolate", (values, _, range, _, y) => TableEdits.InterpolateColumns(values, range, y));
                    break;
                case GridCommand.HorizontalInterpolate:
                    Edit("Horizontal interpolate", (values, _, range, x, _) => TableEdits.InterpolateRows(values, range, x));
                    break;
                case GridCommand.Copy: Copy(); break;
                case GridCommand.Paste:
                    if (Clipboard.ContainsText()) PasteBlock(Clipboard.GetText());
                    break;
                case GridCommand.Undo: image?.Undo(); break;
                case GridCommand.Redo: image?.Redo(); break;
                case GridCommand.ToggleGraph: graphBox.Checked = !graphBox.Checked; break;
            }

            void AddStep(double delta) =>
                Edit(delta > 0 ? "Increase" : "Decrease", (values, cells, _, _, _) => TableEdits.Arithmetic(values, cells, TableOperation.Add, delta));
        }

        private void Smooth() => Edit("Smooth", (values, cells, _, _, _) => TableEdits.Smooth(values, cells));

        /// <summary>Puts the selected cells back to the file's values.</summary>
        private void Revert()
        {
            if (Target() is not { } target || image is null) return;
            try
            {
                image.SetRaw(target.Item, target.Cells.Select(c => new CellValue(c.X, c.Y, image.ReadOriginalRaw(target.Item, c.X, c.Y))).ToList(),
                    $"Revert {target.Cells.Count} cell(s) of {target.Item.Name}");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                context.Log($"Revert refused: {ex.Message}");
            }
        }

        private void Copy()
        {
            if (Target() is not { } target || image is null) return;
            var range = CellRange.Bounding(target.Cells);
            var text = TableEdits.FormatBlock(Values(target.Item), range, TableEdits.Decimals(target.Item.Conversion.Format) ?? 3);
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { context.Log("The clipboard is busy; nothing was copied."); }
        }

        /// <summary>Pastes a spreadsheet block with its top-left at the selection's top-left.</summary>
        internal void PasteBlock(string text) => Edit("Paste into", (values, _, origin, _, _) =>
        {
            var block = TableEdits.ParseBlock(text);
            int rows = block.GetLength(0), columns = block.GetLength(1);
            if (origin.X0 + columns > values.GetLength(1) || origin.Y0 + rows > values.GetLength(0))
                throw new FormatException($"a {columns} × {rows} block does not fit at [{origin.X0},{origin.Y0}].");
            var pasted = new List<CellValue>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    pasted.Add(new CellValue(origin.X0 + c, origin.Y0 + r, block[r, c]));
            return pasted;
        });
    }
}
