using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Tuning;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab's Tuning page: open a calibration file, browse its A2L items as a tree of subsystems, and edit them
    /// in a <see cref="CalibrationGrid"/> beside a Direct2D graph (lines or a 3D surface), in the manner of RaiderLite.
    /// Values change by typing (which starts value entry), fine and coarse steps, multiply, interpolate (both ways,
    /// vertical, horizontal), smooth, revert to the file, and spreadsheet copy and paste; each is one undo step.
    /// A <see cref="LiveTuningSession"/> keeps the ECU in step; without one it is an offline editor.
    /// </summary>
    internal sealed class CcpTuningPanel : UserControl
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
        private readonly Label fileLabel = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
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

            var fileBar = Bar(openButton, openA2lButton, saveAsButton, fileLabel);
            var syncBar = Bar(startButton, stopButton, holdBox, applyButton, refreshButton, sendButton, takeButton, syncLabel);
            var filterBar = Bar(kindBox, changedBox);
            filterBar.Dock = DockStyle.Top;
            var browser = new Panel { Dock = DockStyle.Fill };
            browser.Controls.Add(tree);
            browser.Controls.Add(filterBar);
            browser.Controls.Add(searchBox);

            var valueBar = Bar(Caption("Fine"), fineField, Caption("Coarse"), coarseField, Caption("Value"), valueField, multiplyButton,
                undoButton, redoButton, copyButton, pasteButton);
            var actionBar = Bar(Caption("Interpolate"), interpolateButton, verticalButton, horizontalButton, smoothButton, revertButton,
                graphBox, changesBox);
            valueBar.Dock = actionBar.Dock = DockStyle.Top;
            editorSplit.Panel2.Controls.Add(graphPane);
            var editor = new Panel { Dock = DockStyle.Fill };
            editor.Controls.Add(editorSplit);
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
            syncBar.Dock = fileBar.Dock = DockStyle.Top;
            Controls.Add(syncBar);
            Controls.Add(fileBar);

            Confirm = text => MessageBox.Show(this, text, "Live tuning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            openButton.Click += (_, _) => BrowseCalibration();
            openA2lButton.Click += (_, _) => BrowseA2l();
            saveAsButton.Click += (_, _) => BrowseSaveAs();
            startButton.Click += async (_, _) => await StartSyncAsync();
            stopButton.Click += async (_, _) => await StopSyncAsync();
            holdBox.CheckedChanged += (_, _) => { if (session is not null) session.Mode = holdBox.Checked ? LiveTuningMode.Hold : LiveTuningMode.Live; };
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
            if (image is null) return false;
            A2lResolvedModule resolved;
            try { resolved = A2lLayout.Resolve(A2lParser.ParseFile(path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                context.Log($"Could not read {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
            var problems = image.CheckAgainst(resolved);
            if (problems.Count > 0)
            {
                context.Log($"{Path.GetFileName(path)} does not fit this calibration, so it was not loaded:");
                foreach (var problem in problems) context.Log("  " + problem);
                return false;
            }
            (a2l, a2lPath) = (resolved, path);
            context.Log($"A2L {Path.GetFileName(path)}: {resolved.Items.Count:N0} items, {resolved.Unavailable.Count} unavailable, " +
                $"{resolved.Items.Values.Count(i => !i.IsEditable)} read-only.");
            foreach (var d in resolved.Module.Diagnostics.Take(10)) context.Log("  " + d);
            return true;
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

        // ── Live sync ────────────────────────────────────────────────────────────────────────────────────

        private async Task StartSyncAsync()
        {
            if (image is null || session is not null || context.Master is not { } master) return;
            var build = context.Identification?.Build;
            if (build?.Build != image.Build.Build)
            {
                context.Log($"Live sync needs the ECU to be identified as {image.Build.Build}; it is {build?.Build ?? "not identified"}.");
                return;
            }
            if (!Confirm($"Live tuning writes to {build.Build}'s calibration RAM.\r\n\r\n" +
                (holdBox.Checked ? "• Hold: edits are written when you press Apply.\r\n"
                    : "• Live: every edit, undo and redo is written at once, engine running or not.\r\n") +
                "• Writes are not atomic: a lookup can see a table part-way through a change.\r\n" +
                "• Changes are lost at power-off; Save As keeps them in a file.\r\n" +
                $"• The ECU's calibration is first read and saved to {context.BackupDirectory}.\r\n\r\nStart live sync?"))
                return;
            await context.RunAsync("Start live sync", async ct =>
            {
                session = await LiveTuningSession.StartAsync(master, image, new LiveTuningOptions
                {
                    BackupDirectory = context.BackupDirectory,
                    EngineSpeed = CcpControl.EngineSpeedFor(build),
                    Mode = holdBox.Checked ? LiveTuningMode.Hold : LiveTuningMode.Live,
                }, cancellationToken: ct);
                session.SyncChanged += OnSyncChanged;
                context.Log($"Live sync started; the ECU's calibration is backed up to {session.BackupPath}.");
                if (session.Status == LiveTuningStatus.Comparing)
                    context.Log($"The file and the ECU differ in {session.PendingBytes:N0} byte(s) ({session.Differences.Count} run(s)). " +
                        "Send the file to the ECU or take the ECU's values.");
                if (session.EnableByteDiffers) context.Log("The file's CCP enable byte differs from the ECU's; the ECU's is kept.");
            });
            QueueRefresh();
        }

        private async Task StopSyncAsync()
        {
            if (session is not { } stopping) return;
            session = null;
            stopping.SyncChanged -= OnSyncChanged;
            await stopping.DisposeAsync();
            context.Log("Live sync stopped.");
            QueueRefresh();
        }

        private async Task SendFileAsync()
        {
            if (session is null) return;
            if (!Confirm($"Write {session.PendingBytes:N0} byte(s) of the file to the ECU in {session.Differences.Count} run(s)?\r\n\r\n" +
                "The engine must be off: a write this size is not atomic.")) return;
            await RunSessionAsync("Send the file", (s, ct) => s.SendImageAsync(null, ct));
        }

        private void TakeEcuValues()
        {
            if (session is null) return;
            try { session.TakeEcuValues(); }
            catch (InvalidOperationException ex) { context.Log(ex.Message); }
        }

        private Task RunSessionAsync(string what, Func<LiveTuningSession, CancellationToken, Task> action) =>
            session is { } s ? context.RunAsync(what, ct => action(s, ct)) : Task.CompletedTask;

        private void OnSyncChanged(object? sender, EventArgs e) => QueueRefresh();

        private void OnImageChanged(object? sender, CalibrationChangedEventArgs e) => QueueRefresh();

        private CalibrationSyncState? SyncState(uint address, int length) => session?.StateOf(address, length);

        // ── Browser ──────────────────────────────────────────────────────────────────────────────────────

        private void LoadTree()
        {
            treeModel = a2l is null ? null : CalibrationTree.Build(a2l);
            ShowItem(null);
            RebuildTree();
        }

        /// <summary>Rebuilds the visible tree from the model, keeping only items that pass the filters.</summary>
        private void RebuildTree()
        {
            var terms = searchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string kind = kindBox.SelectedItem as string ?? "All";
            bool Matches(A2lCalibrationItem item) =>
                (kind == "All" || kind == KindGroup(item.Kind)) &&
                (!changedBox.Checked || image!.IsChanged(item)) &&
                terms.All(t => item.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    item.LongIdentifier.Contains(t, StringComparison.OrdinalIgnoreCase));

            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                itemNodes.Clear();
                if (treeModel is null) return;
                bool filtering = terms.Length > 0 || changedBox.Checked;
                foreach (var child in treeModel.Children)
                    if (Build(child, Matches) is { } node) tree.Nodes.Add(node);
                if (filtering) tree.ExpandAll();
                if (current is not null && itemNodes.TryGetValue(current, out var selected)) tree.SelectedNode = selected;
            }
            finally { tree.EndUpdate(); }
            MarkChangedItems();
        }

        private TreeNode? Build(CalibrationTreeNode model, Func<A2lCalibrationItem, bool> matches)
        {
            if (model.Item is { } item)
            {
                if (!matches(item)) return null;
                int icon = item.Kind switch
                {
                    A2lItemKind.Map => MapIcon, A2lItemKind.Curve => CurveIcon, A2lItemKind.AxisPts => AxisIcon,
                    A2lItemKind.Ascii => TextIcon, _ => ValueIcon,
                };
                var leaf = new TreeNode(model.Title, icon, icon)
                {
                    Tag = item,
                    ToolTipText = Wrap($"{item.Name}  ({Shape(item)})\n{item.LongIdentifier}"),
                    ForeColor = item.IsEditable ? SystemColors.WindowText : SystemColors.GrayText,
                };
                itemNodes[item] = leaf;
                return leaf;
            }
            var folder = new TreeNode(model.Title, FolderIcon, FolderIcon) { ToolTipText = model.Description };
            foreach (var child in model.Children)
                if (Build(child, matches) is { } node) folder.Nodes.Add(node);
            return folder.Nodes.Count > 0 ? folder : null;
        }

        private static string KindGroup(A2lItemKind kind) => kind switch
        {
            A2lItemKind.Map or A2lItemKind.Curve => "Tables",
            A2lItemKind.AxisPts => "Axes",
            _ => "Values",
        };

        private static string Shape(A2lCalibrationItem item) => item.Kind switch
        {
            A2lItemKind.Map => $"{item.CountX} × {item.CountY} map",
            A2lItemKind.Curve => $"{item.CountX}-point curve",
            A2lItemKind.AxisPts => $"{item.CountX}-point axis",
            A2lItemKind.ValBlk => $"{item.ElementCount} values",
            A2lItemKind.Ascii => "text",
            _ => "value",
        };

        /// <summary>Tooltips don't wrap on their own; break long lines at about 90 characters.</summary>
        private static string Wrap(string text)
        {
            if (text.Length > 1500) text = text[..1500] + "…";
            var lines = new List<string>();
            foreach (var paragraph in text.Split('\n'))
            {
                var line = new System.Text.StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    if (line.Length > 0 && line.Length + word.Length > 90)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Bold for items that differ from the file.</summary>
        private void MarkChangedItems()
        {
            if (image is null) return;
            foreach (var (item, node) in itemNodes)
            {
                bool changed = image.IsChanged(item);
                var font = changed ? new Font(tree.Font, FontStyle.Bold) : null;
                if ((node.NodeFont is not null) != changed) node.NodeFont = font;
            }
        }

        /// <summary>Selects an item by name, as if picked in the tree.</summary>
        internal bool SelectItem(string name)
        {
            if (a2l is null || !a2l.Items.TryGetValue(name, out var item)) return false;
            if (!itemNodes.ContainsKey(item))
            {
                searchBox.Text = "";
                changedBox.Checked = false;
                kindBox.SelectedItem = "All";
            }
            if (!itemNodes.TryGetValue(item, out var node)) return false;
            tree.SelectedNode = node;
            node.EnsureVisible();
            return true;
        }

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
                graphPane.SetData(null);
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
            graphPane.SetData(GraphData.From(image, item));
            graphPane.Plot.SetSelection(grid.IsSelected);
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
                parts.Add($"{grid.GetText(gc with { })} {current.Conversion.Unit}".TrimEnd());
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
            if (grid is null || current is null || image is null) return null;
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
        internal void PasteBlock(string text) => Edit("Paste into", (_, cells, origin, _, _) =>
        {
            var block = TableEdits.ParseBlock(text);
            int rows = block.GetLength(0), columns = block.GetLength(1);
            var item = Target()!.Value.Item;
            if (origin.X0 + columns > item.CountX || origin.Y0 + rows > item.CountY)
                throw new FormatException($"A {columns} × {rows} block does not fit at [{origin.X0},{origin.Y0}] of {item.Name}.");
            var pasted = new List<CellValue>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    pasted.Add(new CellValue(origin.X0 + c, origin.Y0 + r, block[r, c]));
            return pasted;
        });

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
                    graphPane.SetData(GraphData.From(image, current));
                }
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
            openButton.Enabled = idle;
            openA2lButton.Enabled = idle && image is not null;
            saveAsButton.Enabled = image is not null;
            undoButton.Enabled = image?.CanUndo == true;
            redoButton.Enabled = image?.CanRedo == true;
            toolTip.SetToolTip(undoButton, image?.UndoDescription is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)");
            toolTip.SetToolTip(redoButton, image?.RedoDescription is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)");
            startButton.Enabled = idle && image is not null && context.IsConnected && session is null;
            stopButton.Enabled = session is not null;
            holdBox.Enabled = session is null || status == LiveTuningStatus.Syncing;
            applyButton.Enabled = idle && status == LiveTuningStatus.Syncing && holdBox.Checked && session!.PendingBytes > 0;
            refreshButton.Enabled = idle && session is not null && context.IsConnected;
            sendButton.Enabled = takeButton.Enabled = idle && status is LiveTuningStatus.Comparing or LiveTuningStatus.Syncing
                && session!.PendingBytes > 0;
            graphBox.Enabled = current is not null && GraphData.CanGraph(current);
            changesBox.Enabled = grid is not null;
            UpdateEditButtons();

            fileLabel.Text = image is null ? "No calibration open."
                : $"{Path.GetFileName(image.SourcePath ?? image.Build.Build)} · {image.Build.Build} · " +
                  $"{(a2lPath is null ? "no A2L" : Path.GetFileName(a2lPath))} · {image.ChangedByteCount:N0} byte(s) changed" +
                  (image.HasUnsavedChanges ? " · unsaved" : "");
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
                foreach (var node in itemNodes.Values) node.NodeFont?.Dispose();
                tree.ImageList?.Dispose();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
