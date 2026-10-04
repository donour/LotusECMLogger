using System.Drawing.Drawing2D;
using LotusECMLogger.Hc08;

namespace LotusECMLogger
{
    /// <summary>
    /// Guided dialog for raising the HC08 throttle monitor's TPS max table in a T6 PROG file so it
    /// matches the calibration, via <see cref="Hc08TpsPatcher"/>. The user picks the PROG and CAL
    /// files, reviews the per-breakpoint comparison, and approves the write. This tool only edits a
    /// local PROG file; it never talks to the ECU — the patched PROG still has to be flashed.
    /// </summary>
    public sealed class Hc08TpsPatchDialog : Form
    {
        private const string AutoProfile = "Auto (from calibration ID)";

        private static readonly Color NeutralBack = SystemColors.Info;
        private static readonly Color OkBack = Color.FromArgb(220, 242, 220);
        private static readonly Color UpdateBack = Color.FromArgb(255, 240, 200);
        private static readonly Color ErrorBack = Color.FromArgb(250, 215, 215);
        private static readonly Color RaiseRowBack = Color.FromArgb(220, 242, 220);
        private static readonly Color SkipRowBack = Color.FromArgb(255, 246, 220);
        private static readonly Color AxisCellBack = Color.FromArgb(214, 232, 255);
        private static readonly Font GridFont = new(FontFamily.GenericMonospace, 9F);
        private static readonly Font GridBoldFont = new(FontFamily.GenericMonospace, 9F, FontStyle.Bold);

        private readonly TextBox progPathTextBox;
        private readonly TextBox calPathTextBox;
        private readonly ComboBox profileCombo;
        private readonly Button checkButton;
        private readonly TextBox statusTextBox;
        private readonly DataGridView tableGrid;
        private readonly TpsCurveChart chart;
        private readonly Label detailsLabel;
        private readonly CheckBox backupCheckBox;
        private readonly Button copyReportButton;
        private readonly Button applyButton;

        private Hc08TpsAnalysis? analysis;

        public Hc08TpsPatchDialog()
        {
            Text = "HC08 TPS Table Patch";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(960, 820);
            MinimumSize = new Size(820, 700);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 7
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));  // intro
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F)); // inputs
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));  // status
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // grid + chart
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));  // details
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));  // next step
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));  // buttons

            var introLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "The T6 keeps a second copy of the TPS max vs rpm table inside the HC08 throttle monitor " +
                       "firmware, which is embedded in the PROG file. Tuning tools only change the calibration copy. " +
                       "This tool raises the HC08 copy wherever your calibration is higher (it never lowers it) and " +
                       "repairs the HC08 checksum. It only edits the PROG file on disk; nothing is sent to the ECU."
            };

            // Inputs: PROG, CAL, profile.
            var inputs = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Margin = new Padding(0)
            };
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            for (int i = 0; i < 3; i++)
                inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));

            progPathTextBox = NewPathTextBox();
            calPathTextBox = NewPathTextBox();
            var progBrowseButton = NewRowButton("Browse…");
            progBrowseButton.Click += (_, _) => BrowseFor(isProg: true);
            var calBrowseButton = NewRowButton("Browse…");
            calBrowseButton.Click += (_, _) => BrowseFor(isProg: false);

            profileCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left,
                Width = 240
            };
            profileCombo.Items.Add(AutoProfile);
            foreach (string name in Hc08TpsProfiles.Known.Keys)
                profileCombo.Items.Add(name);
            profileCombo.SelectedIndex = 0;
            profileCombo.SelectedIndexChanged += (_, _) => RunCheckIfReady();

            checkButton = NewRowButton("Check");
            checkButton.Enabled = false;
            checkButton.Click += (_, _) => RunCheck();

            inputs.Controls.Add(NewRowLabel("1. Firmware (PROG, *_BIN.cpt) — patched:"), 0, 0);
            inputs.Controls.Add(progPathTextBox, 1, 0);
            inputs.Controls.Add(progBrowseButton, 2, 0);
            inputs.Controls.Add(NewRowLabel("2. Calibration (CAL, *_TAB.cpt) — read only:"), 0, 1);
            inputs.Controls.Add(calPathTextBox, 1, 1);
            inputs.Controls.Add(calBrowseButton, 2, 1);
            inputs.Controls.Add(NewRowLabel("Firmware profile:"), 0, 2);
            inputs.Controls.Add(profileCombo, 1, 2);
            inputs.Controls.Add(checkButton, 2, 2);

            statusTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                TabStop = false
            };

            tableGrid = BuildGrid();
            chart = new TpsCurveChart { Dock = DockStyle.Fill };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Margin = new Padding(0, 6, 0, 0)
            };
            split.Panel1.Controls.Add(tableGrid);
            split.Panel2.Controls.Add(chart);

            detailsLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(FontFamily.GenericMonospace, 9F),
                ForeColor = SystemColors.GrayText
            };

            var nextStepLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "3. Review the table, then Apply.   4. Flash the patched PROG together with the calibration: " +
                       "build a CRP from both with Tools > Create CRP File…, then flash it with Tools > T6E Calibration Flasher."
            };

            // Bottom row: backup option on the left, actions on the right.
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            backupCheckBox = new CheckBox
            {
                Text = "Save a timestamped backup of the PROG file before patching",
                Checked = true,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0)
            };
            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Size = new Size(88, 32)
            };
            applyButton = new Button
            {
                Text = "Apply Patch…",
                Enabled = false,
                Size = new Size(120, 32)
            };
            applyButton.Click += applyButton_Click;
            copyReportButton = new Button
            {
                Text = "Copy Report",
                Enabled = false,
                Size = new Size(110, 32)
            };
            copyReportButton.Click += (_, _) =>
            {
                if (analysis != null)
                    Clipboard.SetText(analysis.FormatReport());
            };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(applyButton);
            buttons.Controls.Add(copyReportButton);

            bottom.Controls.Add(backupCheckBox, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);

            root.Controls.Add(introLabel, 0, 0);
            root.Controls.Add(inputs, 0, 1);
            root.Controls.Add(statusTextBox, 0, 2);
            root.Controls.Add(split, 0, 3);
            root.Controls.Add(detailsLabel, 0, 4);
            root.Controls.Add(nextStepLabel, 0, 5);
            root.Controls.Add(bottom, 0, 6);
            Controls.Add(root);
            CancelButton = closeButton;

            // The grid needs room for its six columns; give the chart the rest.
            Load += (_, _) => split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Width * 56 / 100);

            ShowGuidance();
        }

        private static TextBox NewPathTextBox() => new()
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Margin = new Padding(3, 6, 3, 3)
        };

        private static Button NewRowButton(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 0, 3)
        };

        private static Label NewRowLabel(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        private static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            // Compact rows so all 16 breakpoints fit without scrolling.
            grid.RowTemplate.Height = 21;
            grid.DefaultCellStyle.Font = GridFont;
            grid.DefaultCellStyle.SelectionBackColor = SystemColors.ControlLight;
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;

            AddColumn("Idx", 34, DataGridViewContentAlignment.MiddleRight);
            AddColumn("RPM", 50, DataGridViewContentAlignment.MiddleRight);
            AddColumn("HC08 axis", 96, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("HC08 now", 80, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("Calibration", 80, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("HC08 new", 80, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("Action", 140, DataGridViewContentAlignment.MiddleLeft);
            foreach (DataGridViewColumn c in grid.Columns)
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return grid;

            void AddColumn(string header, float weight, DataGridViewContentAlignment align)
            {
                int i = grid.Columns.Add(header.Replace(" ", ""), header);
                grid.Columns[i].FillWeight = weight;
                grid.Columns[i].DefaultCellStyle.Alignment = align;
            }
        }

        private void BrowseFor(bool isProg)
        {
            using var dialog = new OpenFileDialog
            {
                Title = isProg ? "Select Firmware (PROG) File" : "Select Calibration (CAL) File",
                Filter = isProg
                    ? "Firmware files (*.cpt;*.bin)|*.cpt;*.bin|All files (*.*)|*.*"
                    : "Calibration files (*.cpt;*.bin)|*.cpt;*.bin|All files (*.*)|*.*",
                CheckFileExists = true
            };
            string current = isProg ? progPathTextBox.Text : calPathTextBox.Text;
            string other = isProg ? calPathTextBox.Text : progPathTextBox.Text;
            string? startIn = Path.GetDirectoryName(current.Length > 0 ? current : other);
            if (!string.IsNullOrEmpty(startIn))
                dialog.InitialDirectory = startIn;

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var target = isProg ? progPathTextBox : calPathTextBox;
            target.Text = dialog.FileName;
            // Long paths are cut off from the right; keep the file name in view.
            target.SelectionStart = target.Text.Length;
            RunCheckIfReady();
        }

        private bool BothFilesSelected => progPathTextBox.Text.Length > 0 && calPathTextBox.Text.Length > 0;

        private string? SelectedProfile =>
            profileCombo.SelectedItem is string s && s != AutoProfile ? s : null;

        private void RunCheckIfReady()
        {
            checkButton.Enabled = BothFilesSelected;
            if (BothFilesSelected)
                RunCheck();
            else
                ShowGuidance();
        }

        /// <summary>Status text for the steps before both files have been chosen.</summary>
        private void ShowGuidance()
        {
            ClearResult();
            SetStatus(NeutralBack, progPathTextBox.Text.Length == 0
                ? "Step 1: select the firmware (PROG) file you intend to flash, e.g. T6EVRGT430E01_BIN.cpt. " +
                  "If you only have a full 1 MB flash dump, extract PROG (bytes 0x40000 to end) first."
                : "Step 2: select the calibration (CAL) file you will flash with this PROG, e.g. your tuned C132E0278_TAB.cpt.");
        }

        private void RunCheck(string? successNote = null)
        {
            if (!BothFilesSelected)
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                analysis = Hc08TpsPatcher.AnalyzeFiles(progPathTextBox.Text, calPathTextBox.Text, SelectedProfile);
                ShowAnalysis(analysis, successNote);
            }
            catch (Exception ex) when (ex is Hc08PatchException or IOException or UnauthorizedAccessException)
            {
                ClearResult();
                SetStatus(ErrorBack, $"Cannot patch: {ex.Message}{Environment.NewLine}The PROG file has not been changed.");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ClearResult()
        {
            analysis = null;
            tableGrid.Rows.Clear();
            chart.SetRows([]);
            detailsLabel.Text = string.Empty;
            applyButton.Enabled = false;
            copyReportButton.Enabled = false;
        }

        private void ShowAnalysis(Hc08TpsAnalysis a, string? successNote)
        {
            tableGrid.Rows.Clear();
            foreach (var r in a.Rows)
            {
                int i = tableGrid.Rows.Add(
                    r.Index,
                    Math.Round(r.Rpm, MidpointRounding.ToEven).ToString("F0"),
                    AxisCell(r),
                    Cell(r.Hc08Old),
                    Cell(r.Cal),
                    Cell(r.Hc08New),
                    GridAction(r));
                var style = tableGrid.Rows[i].DefaultCellStyle;
                if (r.Action == Hc08TpsRowAction.Raise)
                {
                    style.BackColor = RaiseRowBack;
                    style.Font = GridBoldFont;
                }
                else if (r.Action == Hc08TpsRowAction.DecreaseSkipped)
                {
                    style.BackColor = SkipRowBack;
                }
                if (r.AxisChanged)
                    tableGrid.Rows[i].Cells[2].Style.BackColor = AxisCellBack;
            }
            tableGrid.ClearSelection();
            chart.SetRows(a.Rows);
            copyReportButton.Enabled = true;

            string checksum = a.Status == Hc08TpsStatus.UpdateNeeded
                ? $"{a.OldChecksum:X4} → {a.NewChecksum:X4}"
                : $"{a.OldChecksum:X4} (OK)";
            detailsLabel.Text =
                $"Calibration {a.CalibrationId} · profile {a.Profile.Name} · HC08 image @ PROG+0x{a.Hc08ImageOffset:X} · " +
                $"checksum imm @ PROG+0x{a.ChecksumImmediateOffset:X} · checksum {checksum}";

            int skipped = a.Rows.Count(r => r.Action == Hc08TpsRowAction.DecreaseSkipped);
            string skippedNote = skipped == 0 ? string.Empty
                : $" The calibration is lower than the HC08 at {skipped} breakpoint(s) (yellow); those are left " +
                  "unchanged because this tool never lowers the HC08 limit.";

            if (a.Status == Hc08TpsStatus.UpdateNeeded)
            {
                int raised = a.Rows.Count(r => r.Action == Hc08TpsRowAction.Raise);
                int axisChanged = a.Rows.Count(r => r.AxisChanged);
                string what = raised > 0
                    ? $"the calibration exceeds the HC08 TPS max table at {raised} breakpoint(s) (green)."
                    : "no table value needs raising.";
                string axisNote = axisChanged == 0 ? string.Empty
                    : " The HC08 rpm axis does not match the calibration axis, so it will be replaced with the calibration " +
                      $"axis ({axisChanged} breakpoint(s) change, blue) and the tables compared on the calibration breakpoints.";
                applyButton.Enabled = true;
                SetStatus(UpdateBack,
                    $"Update needed: {what}{axisNote} " +
                    $"All safety checks passed. Review the table, then click Apply Patch.{skippedNote}");
            }
            else
            {
                applyButton.Enabled = false;
                SetStatus(OkBack, (successNote ?? "Up to date: no calibration value exceeds the HC08 TPS max table. Nothing to patch.") + skippedNote);
            }
        }

        // Shorter than Hc08TpsRow.ActionText so it fits the column; the status text explains skips.
        private static string GridAction(Hc08TpsRow r) => r.Action switch
        {
            Hc08TpsRowAction.Raise => $"raise {Hc08TpsRow.Percent(r.Hc08New) - Hc08TpsRow.Percent(r.Hc08Old):+0.0}%",
            Hc08TpsRowAction.DecreaseSkipped => $"kept (cal {Hc08TpsRow.Percent(r.Cal) - Hc08TpsRow.Percent(r.Hc08Old):+0.0;-0.0}%)",
            _ => string.Empty
        };

        // HC08 axis breakpoint in rpm; shows old → new when the axis is being rewritten.
        private static string AxisCell(Hc08TpsRow r) => r.AxisChanged
            ? $"{r.Hc08RpmOld:F0} → {r.Hc08RpmNew:F0}"
            : $"{r.Hc08RpmOld:F0}";

        private static string Cell(byte x) => $"{x:X2} {Hc08TpsRow.Percent(x),5:F1}%";

        private void SetStatus(Color back, string text)
        {
            statusTextBox.BackColor = back;
            statusTextBox.Text = text;
        }

        private void applyButton_Click(object? sender, EventArgs e)
        {
            if (analysis is not { Status: Hc08TpsStatus.UpdateNeeded } a)
                return;

            string progPath = progPathTextBox.Text;
            int raised = a.Rows.Count(r => r.Action == Hc08TpsRowAction.Raise);
            int axisChanged = a.Rows.Count(r => r.AxisChanged);
            string backupNote = backupCheckBox.Checked
                ? "A timestamped .bak copy of the original is saved next to it first."
                : "No backup will be made.";

            var confirm = MessageBox.Show(
                $"Patch {Path.GetFileName(progPath)}?\r\n\r\n" +
                (axisChanged == 0 ? string.Empty
                    : $"• Replace the HC08 rpm axis with the calibration axis ({axisChanged} breakpoint(s) change)\r\n") +
                (raised == 0 ? string.Empty
                    : $"• Raise {raised} HC08 TPS max breakpoint(s) to the calibration values\r\n") +
                $"• Update the HC08 checksum {a.OldChecksum:X4} → {a.NewChecksum:X4}\r\n\r\n" +
                $"{backupNote}\r\n\r\n" +
                "This only changes the file. Flash the patched PROG with the matching calibration for it to take effect.",
                "Confirm HC08 Patch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
                return;

            string? backupPath;
            try
            {
                backupPath = Hc08TpsPatcher.Apply(progPath, a, backupCheckBox.Checked);
            }
            catch (Exception ex) when (ex is Hc08PatchException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Failed to patch the PROG file:\r\n\r\n{ex.Message}",
                    "Patch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RunCheck();
                return;
            }

            string done = $"Patched {Path.GetFileName(progPath)}: HC08 checksum {a.OldChecksum:X4} → {a.NewChecksum:X4}." +
                          (backupPath is null ? string.Empty : $" Backup: {Path.GetFileName(backupPath)}.") +
                          " Next: flash this PROG together with the calibration (step 4 below).";

            // Re-check from disk so the grid shows what was actually written.
            RunCheck(done);

            MessageBox.Show(
                $"{progPath} was patched.\r\n\r\n" +
                (backupPath is null ? string.Empty : $"Original saved as:\r\n{backupPath}\r\n\r\n") +
                "Next step: flash the patched PROG together with the calibration. Use Tools > Create CRP File… " +
                "to package both files, then flash the CRP with Tools > T6E Calibration Flasher.",
                "HC08 Patch Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Plots the three TPS max curves against rpm: the HC08 table now, the calibration, and the
        /// HC08 table after the patch.
        /// </summary>
        private sealed class TpsCurveChart : Control
        {
            private static readonly Color NowColor = Color.FromArgb(128, 128, 128);
            private static readonly Color CalColor = Color.FromArgb(0, 102, 204);
            private static readonly Color NewColor = Color.FromArgb(40, 160, 60);

            private IReadOnlyList<Hc08TpsRow> rows = [];

            public TpsCurveChart()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                BackColor = SystemColors.Window;
            }

            public void SetRows(IReadOnlyList<Hc08TpsRow> newRows)
            {
                rows = newRows;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (rows.Count < 2)
                {
                    TextRenderer.DrawText(g, "Select both files to compare the TPS max tables.", Font,
                        ClientRectangle, SystemColors.GrayText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }

                var plot = new Rectangle(44, 30, Width - 44 - 14, Height - 30 - 30);
                if (plot.Width < 40 || plot.Height < 40)
                    return;

                double xMin = Math.Min(rows[0].Rpm, rows[0].Hc08RpmOld);
                double xMax = Math.Max(rows[^1].Rpm, rows[^1].Hc08RpmOld);
                double lowest = rows.Min(r => Math.Min(Hc08TpsRow.Percent(r.Hc08Old), Hc08TpsRow.Percent(r.Cal)));
                double yMin = Math.Max(0, Math.Floor(lowest / 10) * 10 - 10), yMax = 100;

                float X(double rpm) => plot.Left + (float)((rpm - xMin) / (xMax - xMin) * plot.Width);
                float Y(double pct) => plot.Bottom - (float)((pct - yMin) / (yMax - yMin) * plot.Height);

                using (var gridPen = new Pen(SystemColors.ControlLight))
                using (var axisPen = new Pen(SystemColors.ControlDark))
                {
                    for (double p = yMin; p <= yMax + 0.01; p += 10)
                    {
                        float y = Y(p);
                        g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                        TextRenderer.DrawText(g, $"{p:F0}%", Font, new Rectangle(0, (int)y - 8, plot.Left - 4, 16),
                            SystemColors.GrayText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                    for (double rpm = Math.Ceiling(xMin / 1000) * 1000; rpm <= xMax; rpm += 1000)
                    {
                        float x = X(rpm);
                        g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                        TextRenderer.DrawText(g, $"{rpm / 1000:F0}k", Font, new Rectangle((int)x - 20, plot.Bottom + 4, 40, 16),
                            SystemColors.GrayText, TextFormatFlags.HorizontalCenter);
                    }
                    g.DrawRectangle(axisPen, plot);
                }
                TextRenderer.DrawText(g, "rpm", Font, new Rectangle(0, plot.Bottom + 4, plot.Left - 4, 16),
                    SystemColors.GrayText, TextFormatFlags.Right);

                // The new curve is drawn first and wide so it reads as the envelope the other two sit in.
                // HC08 now is plotted on its current axis, which differs from the calibration axis
                // when the patch rewrites it; the patched HC08 curve uses the calibration axis.
                DrawSeries(g, new Pen(Color.FromArgb(150, NewColor), 5f), r => r.Rpm, r => r.Hc08New, markers: true);
                DrawSeries(g, new Pen(NowColor, 1.5f), r => r.Hc08RpmOld, r => r.Hc08Old, markers: false);
                DrawSeries(g, new Pen(CalColor, 1.5f) { DashStyle = DashStyle.Dash }, r => r.Rpm, r => r.Cal, markers: false);

                int lx = plot.Left;
                lx = DrawLegend(g, lx, "HC08 new", NewColor, 5f);
                lx = DrawLegend(g, lx, "HC08 now", NowColor, 1.5f);
                DrawLegend(g, lx, "Calibration", CalColor, 1.5f, DashStyle.Dash);

                void DrawSeries(Graphics gr, Pen pen, Func<Hc08TpsRow, double> rpm, Func<Hc08TpsRow, byte> value, bool markers)
                {
                    using (pen)
                    {
                        var pts = rows.Select(r => new PointF(X(rpm(r)), Y(Hc08TpsRow.Percent(value(r))))).ToArray();
                        gr.DrawLines(pen, pts);
                        if (markers)
                        {
                            using var brush = new SolidBrush(NewColor);
                            foreach (var pt in pts)
                                gr.FillEllipse(brush, pt.X - 2.5f, pt.Y - 2.5f, 5, 5);
                        }
                    }
                }
            }

            private int DrawLegend(Graphics g, int x, string label, Color color, float width, DashStyle dash = DashStyle.Solid)
            {
                using var pen = new Pen(color, width) { DashStyle = dash };
                g.DrawLine(pen, x, 15, x + 22, 15);
                var size = TextRenderer.MeasureText(label, Font);
                TextRenderer.DrawText(g, label, Font, new Point(x + 26, 15 - size.Height / 2), SystemColors.ControlText);
                return x + 26 + size.Width + 14;
            }
        }
    }
}
