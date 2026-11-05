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
    /// <summary>
    /// The CCP tab's Tuning page: open a calibration file, browse its A2L items as a tree of subsystems, and edit them
    /// in a <see cref="CalibrationGrid"/> beside a Direct2D graph (lines or a 3D surface), in the manner of RaiderLite.
    /// Values change by typing (which starts value entry), fine and coarse steps, multiply, interpolate (both ways,
    /// vertical, horizontal), smooth, revert to the file, and spreadsheet copy and paste; each is one undo step.
    /// A <see cref="LiveTuningSession"/> keeps the ECU in step; without one it is an offline editor. Record saves a
    /// replayable recording of the session; Open Recording replays one (CcpTuningPanel.Recording.cs and .Replay.cs).
    /// </summary>
    internal sealed partial class CcpTuningPanel : UserControl
    {
        private static readonly string[] Kinds = ["Tables", "Axes", "Values", "All"];
        private static readonly Color PendingColor = Color.DarkOrange;
        private static readonly Color FailedColor = Color.Firebrick;
        private const int FolderIcon = 0, MapIcon = 1, CurveIcon = 2, AxisIcon = 3, ValueIcon = 4, TextIcon = 5;
        // Graph shown or not: the last choice is used for items opened later in this run.
        private static bool showGraph = true;

        private readonly ICcpTabContext context;

        private readonly Button openButton = new() { Text = "Open Calibration…", AutoSize = true };
        private readonly Button openA2lButton = new() { Text = "Open A2L…", AutoSize = true };
        private readonly Button saveAsButton = new() { Text = "Save As…", AutoSize = true };
        private readonly Label fileLabel = new() { Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, ForeColor = SystemColors.GrayText, Padding = new Padding(2, 3, 0, 0) };
        private readonly Button startButton = new() { Text = "Start Live Sync", AutoSize = true };
        private readonly Button stopButton = new() { Text = "Stop Sync", AutoSize = true };
        private readonly CheckBox holdBox = new() { Text = "Hold edits", AutoSize = true, Padding = new Padding(4, 5, 0, 0) };
        private readonly Button applyButton = new() { Text = "Apply", AutoSize = true };
        private readonly Button refreshButton = new() { Text = "Refresh from ECU", AutoSize = true };
        private readonly Button sendButton = new() { Text = "Send File to ECU", AutoSize = true };
        private readonly Button takeButton = new() { Text = "Take ECU Values", AutoSize = true };
        private readonly Label syncLabel = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

        private readonly TextBox searchBox = new() { Dock = DockStyle.Top, PlaceholderText = "filter tables (space-separated terms)" };
        private readonly ComboBox kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        private readonly CheckBox changedBox = new() { Text = "Changed only", AutoSize = true, Padding = new Padding(4, 4, 0, 0) };
        private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, BorderStyle = BorderStyle.None };

        private readonly Label titleLabel = new() { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Padding = new Padding(4, 3, 0, 0), AutoEllipsis = true };
        private readonly Label descriptionLabel = new() { Dock = DockStyle.Top, Height = 18, ForeColor = SystemColors.GrayText, Padding = new Padding(5, 0, 0, 0), AutoEllipsis = true };
        private readonly StepField fineField = new(56, 26, 22);
        private readonly StepField coarseField = new(56, 26, 22);
        private readonly StepField valueField = new(72, 26, 22);
        private readonly Button multiplyButton = new() { Text = "Multiply", AutoSize = true };
        private readonly Button interpolateButton = new() { Text = "Both", AutoSize = true };
        private readonly Button verticalButton = new() { Text = "Vertical", AutoSize = true };
        private readonly Button horizontalButton = new() { Text = "Horizontal", AutoSize = true };
        private readonly Button smoothButton = new() { Text = "Smooth", AutoSize = true };
        private readonly Button revertButton = new() { Text = "Revert", AutoSize = true };
        private readonly Button undoButton = new() { Text = "Undo", AutoSize = true };
        private readonly Button redoButton = new() { Text = "Redo", AutoSize = true };
        private readonly Button copyButton = new() { Text = "Copy", AutoSize = true };
        private readonly Button pasteButton = new() { Text = "Paste", AutoSize = true };
        private readonly CheckBox graphBox = new() { Text = "Graph", AutoSize = true, Appearance = Appearance.Button, Checked = true };
        private readonly CheckBox changesBox = new() { Text = "Changes", AutoSize = true, Appearance = Appearance.Button };
        private readonly CheckBox cursorBox = new() { Text = "Cursor", AutoSize = true, Appearance = Appearance.Button, Checked = true };
        private readonly Label liveLabel = new() { Dock = DockStyle.Bottom, Height = 20, Padding = new Padding(4, 2, 4, 0), ForeColor = PlotStyle.Cursor, AutoEllipsis = true };
        private readonly System.Windows.Forms.Timer cursorTimer = new() { Interval = 100 };
        private readonly SplitContainer editorSplit = new() { Dock = DockStyle.Fill, Panel2MinSize = 120 };
        private readonly GraphPane graphPane = new() { Dock = DockStyle.Fill };
        private readonly Label statusLabel = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 4, 4, 0), ForeColor = SystemColors.ControlText };
        private readonly ToolTip toolTip = new();

        private CalibrationImage? image;
        private A2lResolvedModule? a2l;
        private string? a2lPath;
        private LiveTuningSession? session;
        private CalibrationTreeNode? treeModel;
        private readonly Dictionary<A2lCalibrationItem, TreeNode> itemNodes = [];
        private A2lCalibrationItem? current;
        private CalibrationGrid? grid;
        private bool refreshQueued;
        private GraphData? graphData;
        // The live operating point (Phase 6e): streamed for the shown item while connected.
        private LiveCursor? liveCursor;
        private int cursorGeneration;
        private (A2lCalibrationItem? Item, CcpMaster? Master) cursorFor;

        public CcpTuningPanel(ICcpTabContext context)
        {
            this.context = context;
            kindBox.Items.AddRange(Kinds);
            kindBox.SelectedIndex = 0;
            graphBox.Checked = showGraph;
            tree.ImageList = GuiIcons.BuildImageList(16, Color.FromArgb(70, 74, 82),
                "\uE80A", "\uE80A", "\uE9D2", "\uE8CB", "\uE8EF", "\uE8D2"); // (folder below), map, curve, axis, value, text
            tree.ImageList.Images[FolderIcon] = FolderImage(16);
            foreach (var (button, glyph) in new (Button, string)[]
            {
                (openButton, GuiIcons.OpenFile), (openA2lButton, GuiIcons.OpenFile), (saveAsButton, GuiIcons.Save),
                (startButton, GuiIcons.Play), (stopButton, GuiIcons.Stop), (refreshButton, GuiIcons.Read), (sendButton, GuiIcons.Write),
                (takeButton, GuiIcons.Read), (applyButton, GuiIcons.Write), (multiplyButton, "\uE947"), (interpolateButton, "\uE9D2"),
                (verticalButton, "\uE8CB"), (horizontalButton, "\uE8AB"), (smoothButton, "\uE9E9"), (revertButton, "\uE777"),
                (undoButton, "\uE7A7"), (redoButton, "\uE7A6"), (copyButton, "\uE8C8"), (pasteButton, "\uE77F"),
            })
                GuiIcons.ApplyToButton(button, glyph);
            // Icon-only: the editor needs its height for the grid.
            foreach (var (button, tip) in new (Button, string)[]
            {
                (undoButton, "Undo (Ctrl+Z)"), (redoButton, "Redo (Ctrl+Y)"), (copyButton, "Copy (Ctrl+C)"), (pasteButton, "Paste (Ctrl+V)"),
            })
            {
                button.Text = "";
                button.AutoSize = false;
                button.Size = new Size(30, 26);
                button.ImageAlign = ContentAlignment.MiddleCenter;
                toolTip.SetToolTip(button, tip);
            }
            graphBox.Image = GuiIcons.Render("\uE9F9", 14, SystemColors.ControlText);
            graphBox.TextImageRelation = TextImageRelation.ImageBeforeText;
            changesBox.Image = GuiIcons.Render("\uE7C4", 14, SystemColors.ControlText);
            changesBox.TextImageRelation = TextImageRelation.ImageBeforeText;

            var minus = GuiIcons.Render("\uE949", 12, SystemColors.ControlText);
            var plus = GuiIcons.Render("\uE948", 12, SystemColors.ControlText);
            fineField.AddButton(true, minus, "Decrease by the fine step (Ctrl+Shift+Down, [)", () => RunCommand(GridCommand.DecrementFine));
            fineField.AddButton(false, plus, "Increase by the fine step (Ctrl+Shift+Up, ])", () => RunCommand(GridCommand.IncrementFine));
            coarseField.AddButton(true, minus, "Decrease by the coarse step (Ctrl+Down, _)", () => RunCommand(GridCommand.DecrementCoarse));
            coarseField.AddButton(false, plus, "Increase by the coarse step (Ctrl+Up, +)", () => RunCommand(GridCommand.IncrementCoarse));
            valueField.AddButton(false, GuiIcons.Render("\uE73E", 12, SystemColors.ControlText), "Set the selected cells (Enter)", () => ApplyValue(valueField.Box.Text));
            valueField.AddButton(false, GuiIcons.Render("\uE711", 12, SystemColors.ControlText), "Clear", () => valueField.Box.Clear());
            fineField.ToolTipText = "Fine step: one raw count by default";
            coarseField.ToolTipText = "Coarse step";
            valueField.ToolTipText = "Type a value, then Enter to set the selection, or Multiply";

            // One bar for files and sync; sync buttons show only when they apply, so it stays one row.
            var topBar = Bar(openButton, openA2lButton, saveAsButton, openRecordingButton, new Label { Width = 12 }, startButton, stopButton,
                holdBox, applyButton, refreshButton, sendButton, takeButton, syncLabel, new Label { Width = 12 }, recordChannelsBox, recordButton,
                recordLabel);
            recordChannelsBox.Margin = new Padding(3, 5, 3, 3);
            var filterBar = Bar(kindBox, changedBox);
            filterBar.Dock = DockStyle.Top;
            var browser = new Panel { Dock = DockStyle.Fill };
            browser.Controls.Add(tree);
            browser.Controls.Add(filterBar);
            browser.Controls.Add(searchBox);
            browser.Controls.Add(fileLabel);

            var valueBar = Bar(Caption("Fine"), fineField, Caption("Coarse"), coarseField, Caption("Value"), valueField, multiplyButton,
                undoButton, redoButton, copyButton, pasteButton);
            var actionBar = Bar(Caption("Interpolate"), interpolateButton, verticalButton, horizontalButton, smoothButton, revertButton,
                graphBox, changesBox, cursorBox);
            valueBar.Dock = actionBar.Dock = DockStyle.Top;
            editorSplit.Panel2.Controls.Add(graphPane);
            var editor = new Panel { Dock = DockStyle.Fill };
            editor.Controls.Add(editorSplit);
            editor.Controls.Add(liveLabel);
            editor.Controls.Add(statusLabel);
            editor.Controls.Add(actionBar);
            editor.Controls.Add(valueBar);
            editor.Controls.Add(descriptionLabel);
            editor.Controls.Add(titleLabel);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.HandleCreated += (_, _) => split.SplitterDistance = Math.Clamp(split.Width / 4, 220, 340);
            split.Panel1.Controls.Add(browser);
            split.Panel2.Controls.Add(editor);
            Controls.Add(split);
            Controls.Add(replayBar);
            topBar.Dock = DockStyle.Top;
            Controls.Add(topBar);
            InitializeRecording();
            InitializeReplay();
            GuiIcons.ApplyToButton(recordButton, "\uE7C8");

            Confirm = text => MessageBox.Show(this, text, "Live tuning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            openButton.Click += (_, _) => BrowseCalibration();
            openA2lButton.Click += (_, _) => BrowseA2l();
            saveAsButton.Click += (_, _) => BrowseSaveAs();
            startButton.Click += async (_, _) => await StartSyncAsync();
            stopButton.Click += async (_, _) => await StopSyncAsync();
            holdBox.CheckedChanged += (_, _) =>
            {
                if (session is not null) session.Mode = holdBox.Checked ? LiveTuningMode.Hold : LiveTuningMode.Live;
                UpdateState();
            };
            applyButton.Click += async (_, _) => await RunSessionAsync("Apply edits", (s, ct) => s.ApplyAsync(null, ct));
            refreshButton.Click += async (_, _) => await RunSessionAsync("Refresh from ECU", (s, ct) => s.RefreshAsync(null, ct));
            sendButton.Click += async (_, _) => await SendFileAsync();
            takeButton.Click += (_, _) => TakeEcuValues();
            searchBox.TextChanged += (_, _) => RebuildTree();
            kindBox.SelectedIndexChanged += (_, _) => RebuildTree();
            changedBox.CheckedChanged += (_, _) => RebuildTree();
            tree.AfterSelect += (_, e) => ShowItem(e.Node?.Tag as A2lCalibrationItem);
            multiplyButton.Click += (_, _) => RunCommand(GridCommand.Multiply);
            interpolateButton.Click += (_, _) => RunCommand(GridCommand.Interpolate);
            verticalButton.Click += (_, _) => RunCommand(GridCommand.VerticalInterpolate);
            horizontalButton.Click += (_, _) => RunCommand(GridCommand.HorizontalInterpolate);
            smoothButton.Click += (_, _) => Smooth();
            revertButton.Click += (_, _) => Revert();
            undoButton.Click += (_, _) => RunCommand(GridCommand.Undo);
            redoButton.Click += (_, _) => RunCommand(GridCommand.Redo);
            copyButton.Click += (_, _) => RunCommand(GridCommand.Copy);
            pasteButton.Click += (_, _) => RunCommand(GridCommand.Paste);
            graphBox.CheckedChanged += (_, _) => { showGraph = graphBox.Checked; LayoutEditor(); };
            changesBox.CheckedChanged += (_, _) => { if (grid is not null) { grid.ShowChanges = changesBox.Checked; grid.Invalidate(); } };
            cursorBox.Image = GuiIcons.Render("\uE81D", 14, SystemColors.ControlText);
            cursorBox.TextImageRelation = TextImageRelation.ImageBeforeText;
            toolTip.SetToolTip(cursorBox, "Show where the ECU is reading this table (streams its axis inputs over DAQ)");
            cursorBox.CheckedChanged += (_, _) => EnsureCursor();
            cursorTimer.Tick += (_, _) => SampleCursor();
            valueField.Box.KeyDown += OnValueKeyDown;
            graphPane.Plot.CellClicked += (_, cell) => { grid?.SelectData([cell]); grid?.Focus(); };
            context.SessionChanged += (_, _) => QueueRefresh();
            ShowItem(null);
        }

        /// <summary>A manila folder, drawn: the icon font's folder reads as an empty checkbox at this size.</summary>
        private static Bitmap FolderImage(int size)
        {
            var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(240, 200, 90));
            using var edge = new Pen(Color.FromArgb(180, 140, 40));
            var tab = new[] { new PointF(1.5f, 4.5f), new PointF(6, 4.5f), new PointF(7.5f, 3), new PointF(1.5f, 3) };
            g.FillPolygon(fill, tab);
            g.DrawPolygon(edge, tab);
            var body = new RectangleF(1.5f, 4.5f, size - 3, size - 7.5f);
            g.FillRectangle(fill, body);
            g.DrawRectangle(edge, body.X, body.Y, body.Width, body.Height);
            return bitmap;
        }

        private static FlowLayoutPanel Bar(params Control[] controls)
        {
            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(2) };
            bar.Controls.AddRange(controls);
            return bar;
        }

        private static Label Caption(string text) =>
            new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 7, 0, 0) };

        /// <summary>Asks the user to approve something that writes to the ECU. Replaceable so UI tests can answer it.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<string, bool> Confirm { get; set; }

        internal CalibrationImage? Image => image;
        internal LiveTuningSession? Session => session;
        internal CalibrationGrid? Grid => grid;
        internal GraphPane GraphPane => graphPane;
        private bool IsGone => IsDisposed || Disposing;

        // ── Files ────────────────────────────────────────────────────────────────────────────────────────

        private void BrowseCalibration()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Calibration files (*.bin;*.cpt;*.crp)|*.bin;*.cpt;*.crp|All files (*.*)|*.*",
                InitialDirectory = LoggerPaths.OutputDirectory,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) _ = OpenCalibrationAsync(dialog.FileName);
        }

        /// <summary>Opens a calibration file and the build's A2L from config/a2l when there is one.</summary>
        internal async Task OpenCalibrationAsync(string path)
        {
            if (image is { HasUnsavedChanges: true } && !Confirm("The open calibration has unsaved changes. Discard them?")) return;
            CalibrationImage opened;
            try { opened = CalibrationImage.Load(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                context.Log($"Could not open {Path.GetFileName(path)}: {ex.Message}");
                return;
            }
            await StopSyncAsync();
            if (image is not null) image.Changed -= OnImageChanged;
            (image, a2l, a2lPath) = (opened, null, null);
            image.Changed += OnImageChanged;
            context.Log($"Opened {Path.GetFileName(path)}: {image.CalibrationIdText} ({Describe(image.Format)}, {image.Size:N0} bytes of calibration).");
            if (FindA2l(image.Build.Build) is { } found) LoadA2l(found);
            else context.Log($"No A2L for {image.Build.Build} in config/a2l; open one to see its tables.");
            LoadTree();
        }

        private static string Describe(CalibrationFileFormat format) => format switch
        {
            CalibrationFileFormat.RawImage => "raw calibration image",
            CalibrationFileFormat.CrpChunk => "CRP calibration chunk",
            CalibrationFileFormat.SnapshotDownload => "Snapshots calibration download",
            _ => "CRP file",
        };

        private static string? FindA2l(string build)
        {
            foreach (var directory in new[] { Path.Combine("config", "a2l"), Path.Combine(AppContext.BaseDirectory, "config", "a2l") })
            {
                string path = Path.Combine(directory, build + ".a2l");
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
            return null;
        }

        private void BrowseA2l()
        {
            using var dialog = new OpenFileDialog { Filter = "A2L files (*.a2l)|*.a2l|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK && LoadA2l(dialog.FileName)) LoadTree();
        }

        /// <summary>Reads an A2L and keeps it only if it matches the open calibration (EPK, segment, item ranges).</summary>
        internal bool LoadA2l(string path)
        {
            if (image is null || ReadA2l(path, image) is not { } resolved) return false;
            (a2l, a2lPath) = (resolved, path);
            return true;
        }

        /// <summary>An A2L that fits <paramref name="calibration"/>, or null with the reasons logged.</summary>
        private A2lResolvedModule? ReadA2l(string path, CalibrationImage calibration)
        {
            A2lResolvedModule resolved;
            try { resolved = A2lLayout.Resolve(A2lParser.ParseFile(path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                context.Log($"Could not read {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
            var problems = calibration.CheckAgainst(resolved);
            if (problems.Count > 0)
            {
                context.Log($"{Path.GetFileName(path)} does not fit this calibration, so it was not loaded:");
                foreach (var problem in problems) context.Log("  " + problem);
                return null;
            }
            context.Log($"A2L {Path.GetFileName(path)}: {resolved.Items.Count:N0} items, {resolved.Unavailable.Count} unavailable, " +
                $"{resolved.Items.Values.Count(i => !i.IsEditable)} read-only.");
            foreach (var d in resolved.Module.Diagnostics.Take(10)) context.Log("  " + d);
            return resolved;
        }

        private void BrowseSaveAs()
        {
            if (image is null) return;
            string stem = image.SourcePath is { } source ? Path.GetFileNameWithoutExtension(source) : image.Build.Build;
            using var dialog = new SaveFileDialog
            {
                Filter = "Calibration files (*.bin)|*.bin|All files (*.*)|*.*",
                InitialDirectory = image.SourcePath is { } p ? Path.GetDirectoryName(p) : LoggerPaths.OutputDirectory,
                FileName = $"{stem}_tuned_{DateTime.Now:yyyyMMdd-HHmm}.bin",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) SaveAs(dialog.FileName);
        }

        internal void SaveAs(string path)
        {
            if (image is null) return;
            try
            {
                var saved = image.SaveAs(path);
                context.Log($"Saved the {Describe(saved.Format)} to {saved.Path}{(saved.BackupPath is { } b ? $" (the file it replaced is {b})" : "")}.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                context.Log($"Save failed: {ex.Message}");
            }
            UpdateState();
        }

        // ── State ────────────────────────────────────────────────────────────────────────────────────────

        private void QueueRefresh()
        {
            if (IsGone) return;
            if (!IsHandleCreated)
            {
                UpdateState();
                return;
            }
            if (refreshQueued) return;
            refreshQueued = true;
            BeginInvoke(() =>
            {
                refreshQueued = false;
                if (IsGone) return;
                if (grid is not null && current is not null && image is not null)
                {
                    grid.RefreshValues();
                    graphPane.SetData(graphData = GraphData.From(image, current));
                }
                EnsureCursor();
                MarkChangedItems();
                ShowStatus();
                UpdateState();
            });
        }

        private void UpdateEditButtons()
        {
            var target = Target();
            bool editable = target is { Item.IsEditable: true };
            bool cells = editable && target!.Value.Cells.Count > 0;
            var range = target is { } t ? CellRange.Bounding(t.Cells) : default;
            fineField.Enabled = coarseField.Enabled = valueField.Enabled = multiplyButton.Enabled = smoothButton.Enabled = cells;
            interpolateButton.Enabled = cells && range.Width * range.Height >= 2;
            horizontalButton.Enabled = cells && range.Width >= 2;
            verticalButton.Enabled = cells && range.Height >= 2;
            revertButton.Enabled = cells && image is not null && target!.Value.Cells.Any(c => image.ReadRaw(target.Value.Item, c.X, c.Y) != image.ReadOriginalRaw(target.Value.Item, c.X, c.Y));
            copyButton.Enabled = target is not null;
            pasteButton.Enabled = cells;
        }

        private void UpdateState()
        {
            bool idle = !context.IsBusy;
            var status = session?.Status;
            bool replaying = recording is not null, recordingNow = recorder is not null;
            openButton.Enabled = idle && !replaying && !recordingNow;
            openA2lButton.Enabled = idle && image is not null && !replaying && !recordingNow;
            saveAsButton.Enabled = image is not null;
            recordButton.Text = recordingNow ? "Stop Recording" : "Record";
            recordButton.Enabled = recordingNow || (idle && image is not null && a2l is not null && context.IsConnected && !replaying);
            recordChannelsBox.Enabled = !recordingNow && !replaying;
            openRecordingButton.Enabled = idle && !recordingNow && session is null;
            replayBar.Visible = replaying;
            UpdateRecordLabel();
            undoButton.Enabled = image?.CanUndo == true;
            redoButton.Enabled = image?.CanRedo == true;
            toolTip.SetToolTip(undoButton, image?.UndoDescription is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)");
            toolTip.SetToolTip(redoButton, image?.RedoDescription is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)");
            startButton.Enabled = idle && image is not null && context.IsConnected && session is null && !replaying;
            stopButton.Enabled = session is not null;
            holdBox.Enabled = session is null || status == LiveTuningStatus.Syncing;
            applyButton.Enabled = idle && status == LiveTuningStatus.Syncing && holdBox.Checked && session!.PendingBytes > 0;
            refreshButton.Enabled = idle && session is not null && context.IsConnected;
            sendButton.Enabled = takeButton.Enabled = idle && status is LiveTuningStatus.Comparing or LiveTuningStatus.Syncing
                && session!.PendingBytes > 0;
            startButton.Visible = session is null;
            stopButton.Visible = refreshButton.Visible = session is not null;
            applyButton.Visible = session is not null && holdBox.Checked;
            sendButton.Visible = takeButton.Visible = status == LiveTuningStatus.Comparing;
            graphBox.Enabled = current is not null && GraphData.CanGraph(current);
            changesBox.Enabled = grid is not null;
            UpdateEditButtons();

            fileLabel.Text = image is null ? "No calibration open."
                : recording is not null ? $"Replay: {Path.GetFileName(recording.Files.Log)} · {image.ChangedByteCount:N0} B written by then"
                : $"{Path.GetFileName(image.SourcePath ?? image.Build.Build)} · {image.ChangedByteCount:N0} B changed" +
                  (image.HasUnsavedChanges ? " · unsaved" : "");
            toolTip.SetToolTip(fileLabel, image is null ? "" : $"{image.SourcePath} ({image.Build.Build})\n" +
                $"A2L: {a2lPath ?? "none"}\n{image.ChangedByteCount:N0} byte(s) changed from the file{(image.HasUnsavedChanges ? ", not saved" : "")}");
            (syncLabel.Text, syncLabel.ForeColor) = status switch
            {
                null => (context.IsConnected ? "Not syncing." : "Offline: connect to sync with the ECU.", SystemColors.GrayText),
                LiveTuningStatus.Comparing => ($"File and ECU differ ({session!.PendingBytes:N0} bytes): send the file or take the ECU's values.", PendingColor),
                LiveTuningStatus.Syncing when session!.PendingBytes > 0 => ($"{(holdBox.Checked ? "Holding" : "Writing")} {session.PendingBytes:N0} byte(s)…", PendingColor),
                LiveTuningStatus.Syncing => ("ECU in sync.", Color.ForestGreen),
                LiveTuningStatus.Stopped => ($"Sync stopped: {session!.StopReason}", FailedColor),
                _ => ("Sync closed.", SystemColors.GrayText),
            };
            toolTip.SetToolTip(syncLabel, syncLabel.Text);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (image is not null) image.Changed -= OnImageChanged;
                if (session is { } closing)
                {
                    session = null;
                    closing.SyncChanged -= OnSyncChanged;
                    _ = closing.DisposeAsync().AsTask().ContinueWith(t => Debug.WriteLine($"CcpTuningPanel: closing sync failed: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                cursorTimer.Dispose();
                playTimer.Dispose();
                recordTimer.Dispose();
                if (recorder is { } running)
                {
                    recorder = null;
                    _ = running.StopAsync(null).ContinueWith(t => Debug.WriteLine($"CcpTuningPanel: stopping the recording failed: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                if (liveCursor is { } cursor)
                {
                    liveCursor = null;
                    _ = cursor.DisposeAsync().AsTask().ContinueWith(t => Debug.WriteLine($"CcpTuningPanel: stopping the cursor failed: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                foreach (var node in itemNodes.Values) node.NodeFont?.Dispose();
                tree.ImageList?.Dispose();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
