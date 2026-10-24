using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab's Tuning page (Phase 6d): open a calibration file, browse its A2L tables, edit them in a heatmap
    /// grid (type, paste from a spreadsheet, or set / add / multiply / percent / interpolate / smooth a selection),
    /// and keep the ECU in step through a <see cref="LiveTuningSession"/>. Without a session it is an offline editor.
    /// Cell text colour shows sync state; bold marks cells that differ from the file as opened.
    /// </summary>
    internal sealed class CcpTuningPanel : UserControl
    {
        private const int BlockColumns = 16;
        private static readonly string[] Kinds = ["Tables", "Axes", "Values", "All"];
        private static readonly Color PendingColor = Color.DarkOrange;
        private static readonly Color WritingColor = Color.RoyalBlue;
        private static readonly Color FailedColor = Color.Firebrick;

        private readonly ICcpTabContext context;

        private readonly Button openButton = new() { Text = "Open Calibration…", AutoSize = true };
        private readonly Button openA2lButton = new() { Text = "Open A2L…", AutoSize = true };
        private readonly Button saveAsButton = new() { Text = "Save As…", AutoSize = true };
        private readonly Button undoButton = new() { Text = "Undo", AutoSize = true };
        private readonly Button redoButton = new() { Text = "Redo", AutoSize = true };
        private readonly Button startButton = new() { Text = "Start Live Sync", AutoSize = true };
        private readonly Button stopButton = new() { Text = "Stop Sync", AutoSize = true };
        private readonly CheckBox holdBox = new() { Text = "Hold edits", AutoSize = true, Padding = new Padding(4, 5, 0, 0) };
        private readonly Button applyButton = new() { Text = "Apply", AutoSize = true };
        private readonly Button refreshButton = new() { Text = "Refresh from ECU", AutoSize = true };
        private readonly Button sendButton = new() { Text = "Send File to ECU", AutoSize = true };
        private readonly Button takeButton = new() { Text = "Take ECU Values", AutoSize = true };
        private readonly Label fileLabel = new() { AutoSize = true, Padding = new Padding(6, 2, 0, 0) };
        private readonly Label syncLabel = new() { AutoSize = true, Padding = new Padding(6, 2, 0, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

        private readonly TextBox searchBox = new() { Dock = DockStyle.Top, PlaceholderText = "filter tables (space-separated terms)" };
        private readonly ComboBox kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly ComboBox unitBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly CheckBox changedBox = new() { Text = "Changed", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        private readonly ListView itemsView = new()
        {
            View = View.Details, FullRowSelect = true, VirtualMode = true, Dock = DockStyle.Fill, HideSelection = false, MultiSelect = false,
        };

        private readonly Label titleLabel = new() { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Padding = new Padding(4, 3, 0, 0) };
        private readonly TextBox operandBox = new() { Width = 70, PlaceholderText = "value" };
        private readonly Button setOpButton = new() { Text = "Set", AutoSize = true };
        private readonly Button addOpButton = new() { Text = "Add", AutoSize = true };
        private readonly Button multiplyOpButton = new() { Text = "Multiply", AutoSize = true };
        private readonly Button percentOpButton = new() { Text = "+ %", AutoSize = true };
        private readonly Button interpolateButton = new() { Text = "Interpolate", AutoSize = true };
        private readonly Button smoothButton = new() { Text = "Smooth", AutoSize = true };
        private readonly ComboBox viewBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly DataGridView grid = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersWidth = 90, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            BackgroundColor = SystemColors.Window, Font = new Font("Consolas", 9F),
        };
        private readonly Label detailLabel = new() { Dock = DockStyle.Bottom, Height = 36, ForeColor = SystemColors.GrayText, Padding = new Padding(4, 2, 4, 0) };
        private readonly ToolTip toolTip = new();
        private readonly Font boldFont;

        private CalibrationImage? image;
        private A2lResolvedModule? a2l;
        private string? a2lPath;
        private LiveTuningSession? session;
        private List<A2lCalibrationItem> allItems = [];
        private List<A2lCalibrationItem> filtered = [];
        private A2lCalibrationItem? current;
        private bool refreshQueued;
        private bool updatingGrid;

        public CcpTuningPanel(ICcpTabContext context)
        {
            this.context = context;
            boldFont = new Font(grid.Font, FontStyle.Bold);
            itemsView.Columns.Add("Name", 250);
            itemsView.Columns.Add("Size", 50);
            itemsView.Columns.Add("Unit", 55);
            itemsView.Columns.Add("Δ", 24);
            kindBox.Items.AddRange(Kinds);
            kindBox.SelectedIndex = 0;
            viewBox.Items.AddRange(["Values", "Change from file"]);
            viewBox.SelectedIndex = 0;
            GuiIcons.ApplyToButton(openButton, GuiIcons.OpenFile);
            GuiIcons.ApplyToButton(openA2lButton, GuiIcons.OpenFile);
            GuiIcons.ApplyToButton(saveAsButton, GuiIcons.Save);
            GuiIcons.ApplyToButton(startButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(refreshButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(sendButton, GuiIcons.Write);
            GuiIcons.ApplyToButton(takeButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(applyButton, GuiIcons.Write);

            var fileBar = Bar(openButton, openA2lButton, saveAsButton, undoButton, redoButton, fileLabel);
            var syncBar = Bar(startButton, stopButton, holdBox, applyButton, refreshButton, sendButton, takeButton, syncLabel);
            var filterBar = Bar(kindBox, unitBox, changedBox);
            filterBar.Dock = DockStyle.Top;
            var browser = new Panel { Dock = DockStyle.Fill };
            browser.Controls.Add(itemsView);
            browser.Controls.Add(filterBar);
            browser.Controls.Add(searchBox);

            var opBar = Bar(new Label { Text = "Selection:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, operandBox,
                setOpButton, addOpButton, multiplyOpButton, percentOpButton, interpolateButton, smoothButton,
                new Label { Text = "Show:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, viewBox);
            opBar.Dock = DockStyle.Top;
            var editor = new Panel { Dock = DockStyle.Fill };
            editor.Controls.Add(grid);
            editor.Controls.Add(detailLabel);
            editor.Controls.Add(opBar);
            editor.Controls.Add(titleLabel);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.HandleCreated += (_, _) => split.SplitterDistance = Math.Clamp(split.Width / 3, 220, 400);
            split.Panel1.Controls.Add(browser);
            split.Panel2.Controls.Add(editor);
            Controls.Add(split);
            syncBar.Dock = DockStyle.Top;
            fileBar.Dock = DockStyle.Top;
            Controls.Add(syncBar);
            Controls.Add(fileBar);

            Confirm = text => MessageBox.Show(this, text, "Live tuning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            openButton.Click += (_, _) => BrowseCalibration();
            openA2lButton.Click += (_, _) => BrowseA2l();
            saveAsButton.Click += (_, _) => BrowseSaveAs();
            undoButton.Click += (_, _) => image?.Undo();
            redoButton.Click += (_, _) => image?.Redo();
            startButton.Click += async (_, _) => await StartSyncAsync();
            stopButton.Click += async (_, _) => await StopSyncAsync();
            holdBox.CheckedChanged += (_, _) => { if (session is not null) session.Mode = holdBox.Checked ? LiveTuningMode.Hold : LiveTuningMode.Live; };
            applyButton.Click += async (_, _) => await RunSessionAsync("Apply edits", (s, ct) => s.ApplyAsync(null, ct));
            refreshButton.Click += async (_, _) => await RunSessionAsync("Refresh from ECU", (s, ct) => s.RefreshAsync(null, ct));
            sendButton.Click += async (_, _) => await SendFileAsync();
            takeButton.Click += (_, _) => TakeEcuValues();
            searchBox.TextChanged += (_, _) => ApplyFilter();
            kindBox.SelectedIndexChanged += (_, _) => ApplyFilter();
            unitBox.SelectedIndexChanged += (_, _) => ApplyFilter();
            changedBox.CheckedChanged += (_, _) => ApplyFilter();
            itemsView.RetrieveVirtualItem += (_, e) => e.Item = ItemRow(filtered[e.ItemIndex]);
            itemsView.SelectedIndexChanged += (_, _) => ShowItem(itemsView.SelectedIndices.Count == 1 ? filtered[itemsView.SelectedIndices[0]] : null);
            setOpButton.Click += (_, _) => ApplyOperation(TableOperation.Set);
            addOpButton.Click += (_, _) => ApplyOperation(TableOperation.Add);
            multiplyOpButton.Click += (_, _) => ApplyOperation(TableOperation.Multiply);
            percentOpButton.Click += (_, _) => ApplyOperation(TableOperation.Percent);
            interpolateButton.Click += (_, _) => EditSelection("Interpolate", (values, cells) =>
                TableEdits.Interpolate(values, CellRange.Bounding(cells), AxisValues(0), AxisValues(1)));
            smoothButton.Click += (_, _) => EditSelection("Smooth", TableEdits.Smooth);
            viewBox.SelectedIndexChanged += (_, _) => RefreshGrid();
            grid.CellBeginEdit += (_, e) => e.Cancel = !CanEditCells || viewBox.SelectedIndex != 0 || ItemCell(e.ColumnIndex, e.RowIndex) is null;
            grid.CellValidating += OnCellValidating;
            grid.CellEndEdit += (_, _) => QueueRefresh();
            grid.KeyDown += OnGridKeyDown;
            grid.CellToolTipTextNeeded += (_, e) => e.ToolTipText = CellToolTip(e.ColumnIndex, e.RowIndex);
            grid.SelectionChanged += (_, _) => UpdateState();
            context.SessionChanged += (_, _) => QueueRefresh();
            UpdateState();
        }

        private static FlowLayoutPanel Bar(params Control[] controls)
        {
            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(2) };
            bar.Controls.AddRange(controls);
            return bar;
        }

        /// <summary>Asks the user to approve something that writes to the ECU. Replaceable so UI tests can answer it.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<string, bool> Confirm { get; set; }

        internal CalibrationImage? Image => image;
        internal LiveTuningSession? Session => session;
        private bool IsGone => IsDisposed || Disposing;
        private bool CanEditCells => image is not null && current is { IsEditable: true, Kind: not A2lItemKind.Ascii };

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
            (image, a2l, a2lPath, current) = (opened, null, null, null);
            image.Changed += OnImageChanged;
            context.Log($"Opened {Path.GetFileName(path)}: {image.CalibrationIdText} ({Describe(image.Format)}, {image.Size:N0} bytes of calibration).");
            if (FindA2l(image.Build.Build) is { } found) LoadA2l(found);
            else context.Log($"No A2L for {image.Build.Build} in config/a2l; open one to see its tables.");
            LoadItems();
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
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                LoadA2l(dialog.FileName);
                LoadItems();
            }
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

        // ── Browser ──────────────────────────────────────────────────────────────────────────────────────

        private void LoadItems()
        {
            allItems = a2l?.Items.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            unitBox.Items.Clear();
            unitBox.Items.Add("All units");
            unitBox.Items.AddRange(allItems.Select(i => i.Conversion.Unit).Where(u => u.Length > 0).Distinct()
                .Order(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
            unitBox.SelectedIndex = 0;
            ShowItem(null);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var terms = searchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? unit = unitBox.SelectedIndex > 0 ? unitBox.SelectedItem as string : null;
            string kind = kindBox.SelectedItem as string ?? "All";
            filtered = allItems.Where(i =>
                (kind == "All" || kind == KindGroup(i.Kind)) &&
                (unit is null || i.Conversion.Unit == unit) &&
                (!changedBox.Checked || image!.IsChanged(i)) &&
                terms.All(t => i.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    i.LongIdentifier.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
            itemsView.SelectedIndices.Clear();
            itemsView.VirtualListSize = filtered.Count;
            itemsView.Invalidate();
            if (current is not null && filtered.IndexOf(current) is int index and >= 0)
            {
                itemsView.SelectedIndices.Add(index);
                itemsView.EnsureVisible(index);
            }
        }

        private static string KindGroup(A2lItemKind kind) => kind switch
        {
            A2lItemKind.Map or A2lItemKind.Curve => "Tables",
            A2lItemKind.AxisPts => "Axes",
            _ => "Values",
        };

        private ListViewItem ItemRow(A2lCalibrationItem item) => new(
        [
            item.Name,
            item.Kind == A2lItemKind.Map ? $"{item.CountX}×{item.CountY}" : item.ElementCount.ToString(CultureInfo.InvariantCulture),
            item.Conversion.Unit,
            image is not null && image.IsChanged(item) ? "●" : "",
        ]) { ToolTipText = item.LongIdentifier, ForeColor = item.IsEditable ? SystemColors.WindowText : SystemColors.GrayText };

        /// <summary>Selects an item by name, as if picked in the list.</summary>
        internal bool SelectItem(string name)
        {
            var item = allItems.FirstOrDefault(i => i.Name == name);
            if (item is null) return false;
            searchBox.Text = "";
            kindBox.SelectedItem = "All";
            changedBox.Checked = false;
            unitBox.SelectedIndex = unitBox.Items.Count > 0 ? 0 : -1;
            ShowItem(item);
            ApplyFilter();
            return true;
        }

        // ── Grid ─────────────────────────────────────────────────────────────────────────────────────────

        private void ShowItem(A2lCalibrationItem? item)
        {
            if (ReferenceEquals(item, current) && item is not null) return;
            current = item;
            grid.Rows.Clear();
            grid.Columns.Clear();
            if (item is null || image is null)
            {
                titleLabel.Text = image is null ? "Open a calibration file to start." : a2l is null ? "No A2L loaded." : "Pick a table.";
                detailLabel.Text = "";
                UpdateState();
                return;
            }
            var (columns, rows) = Shape(item);
            updatingGrid = true;
            for (int c = 0; c < columns; c++)
                grid.Columns.Add(new DataGridViewTextBoxColumn { SortMode = DataGridViewColumnSortMode.NotSortable, Width = 62 });
            grid.RowCount = rows;
            updatingGrid = false;
            titleLabel.Text = item.LongIdentifier.Length > 0 ? $"{item.Name} — {item.LongIdentifier}" : item.Name;
            toolTip.SetToolTip(titleLabel, titleLabel.Text);
            detailLabel.Text = Details(item);
            RefreshGrid();
            UpdateState();
        }

        private static (int Columns, int Rows) Shape(A2lCalibrationItem item) => item.Kind switch
        {
            A2lItemKind.Map => (item.CountX, item.CountY),
            A2lItemKind.ValBlk => (Math.Min(BlockColumns, item.ElementCount), (item.ElementCount + BlockColumns - 1) / BlockColumns),
            A2lItemKind.Value or A2lItemKind.Ascii => (1, 1),
            _ => (item.CountX, 1),
        };

        /// <summary>The item cell (x, y) shown at a grid cell, or null for the padding after a VAL_BLK's last element.</summary>
        private (int X, int Y)? ItemCell(int column, int row)
        {
            if (current is null || column < 0 || row < 0) return null;
            return current.Kind switch
            {
                A2lItemKind.Map => (column, row),
                A2lItemKind.ValBlk => row * BlockColumns + column < current.ElementCount ? (row * BlockColumns + column, 0) : null,
                _ => (column, 0),
            };
        }

        private int Decimals(A2lCompuMethod conversion) => TableEdits.Decimals(conversion.Format) ?? 3;

        /// <summary>A value as shown: a verbal table's text, else the number at the conversion's precision.</summary>
        private string Format(double value, A2lCompuMethod conversion) => conversion.ToText(value) ?? Number(value, conversion);

        private string Number(double value, A2lCompuMethod conversion) => value.ToString("F" + Decimals(conversion), CultureInfo.CurrentCulture);

        private double[] AxisPhysical(int index) =>
            current is { } item && index < item.Axes.Count && image is not null ? image.ReadAxisPhysical(item.Axes[index], item.ByteOrder) : [];

        private IReadOnlyList<double>? AxisValues(int index) => current?.Kind is A2lItemKind.Map or A2lItemKind.Curve ? AxisPhysical(index) : null;

        private double[,] Values(A2lCalibrationItem item)
        {
            var values = new double[item.CountY, item.CountX];
            for (int y = 0; y < item.CountY; y++)
                for (int x = 0; x < item.CountX; x++)
                    values[y, x] = image!.ReadPhysical(item, x, y);
            return values;
        }

        private double OriginalPhysical(A2lCalibrationItem item, int x, int y) => item.Conversion.ToPhysical(image!.ReadOriginalRaw(item, x, y));

        private void RefreshGrid()
        {
            if (current is not { } item || image is null || grid.IsCurrentCellInEditMode) return;
            bool diff = viewBox.SelectedIndex == 1;
            updatingGrid = true;
            try
            {
                // Axis headers in physical units; plain indices for blocks and stand-alone axes.
                var xAxis = item.Kind is A2lItemKind.Map or A2lItemKind.Curve ? AxisPhysical(0) : null;
                var yAxis = item.Kind == A2lItemKind.Map ? AxisPhysical(1) : null;
                for (int c = 0; c < grid.ColumnCount; c++)
                    grid.Columns[c].HeaderText = xAxis is not null ? Format(xAxis[c], item.Axes[0].Conversion)
                        : item.Kind == A2lItemKind.ValBlk ? $"+{c}" : item.Kind is A2lItemKind.Value or A2lItemKind.Ascii ? "value" : c.ToString(CultureInfo.InvariantCulture);
                for (int r = 0; r < grid.RowCount; r++)
                    grid.Rows[r].HeaderCell.Value = yAxis is not null ? Format(yAxis[r], item.Axes[1].Conversion)
                        : item.Kind == A2lItemKind.ValBlk ? (r * BlockColumns).ToString(CultureInfo.InvariantCulture) : "";

                if (item.Kind == A2lItemKind.Ascii)
                {
                    grid.Rows[0].Cells[0].Value = image.ReadText(item);
                    grid.Columns[0].Width = 300;
                    return;
                }
                var values = Values(item);
                double min = double.MaxValue, max = double.MinValue;
                foreach (double v in values) { min = Math.Min(min, v); max = Math.Max(max, v); }
                for (int r = 0; r < grid.RowCount; r++)
                {
                    for (int c = 0; c < grid.ColumnCount; c++)
                    {
                        var cell = grid.Rows[r].Cells[c];
                        if (ItemCell(c, r) is not var (x, y))
                        {
                            cell.Value = "";
                            cell.Style.BackColor = SystemColors.Control;
                            continue;
                        }
                        double value = values[y, x];
                        double change = value - OriginalPhysical(item, x, y);
                        cell.Value = diff ? (change == 0 ? "" : (change > 0 ? "+" : "") + Number(change, item.Conversion)) : Format(value, item.Conversion);
                        cell.Style.BackColor = diff ? DiffColor(change, max - min) : Heat(value, min, max);
                        cell.Style.Font = image.IsChanged(item.CellAddress(x, y), item.ElementSize) ? boldFont : null;
                        cell.Style.ForeColor = SyncColor(item, x, y);
                    }
                }
            }
            finally { updatingGrid = false; }
        }

        private Color SyncColor(A2lCalibrationItem item, int x, int y) => session?.StateOf(item, x, y) switch
        {
            CalibrationSyncState.Pending => PendingColor,
            CalibrationSyncState.Writing => WritingColor,
            CalibrationSyncState.Failed => FailedColor,
            _ => SystemColors.WindowText,
        };

        // Pastel blue (low) through green to red (high), light enough for black text.
        private static Color Heat(double value, double min, double max)
        {
            double t = max > min ? (value - min) / (max - min) : 0.5;
            double hue = 240 * (1 - t);
            return FromHsv(hue, 0.35, 1);
        }

        private static Color DiffColor(double change, double span)
        {
            if (change == 0) return SystemColors.Window;
            double strength = Math.Clamp(Math.Abs(change) / Math.Max(span, 1e-9), 0.15, 0.6);
            return FromHsv(change > 0 ? 0 : 220, strength, 1);
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            double c = value * saturation, h = hue / 60, x = c * (1 - Math.Abs(h % 2 - 1)), m = value - c;
            var (r, g, b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        private string Details(A2lCalibrationItem item)
        {
            string axes = string.Join("; ", item.Axes.Select((a, i) =>
                $"{(i == 0 ? "X" : "Y")}: {a.AxisPtsName ?? "own points"} [{a.Conversion.Unit}], input {a.InputQuantity ?? "none"}"));
            string limits = string.Create(CultureInfo.InvariantCulture, $"{item.LowerLimit:G6} to {item.UpperLimit:G6}");
            return $"0x{item.Address:X8}, {item.ValueType.Keyword()} × {item.ElementCount}, unit {(item.Conversion.Unit.Length > 0 ? item.Conversion.Unit : "none")}, " +
                $"limits {limits}" + (axes.Length > 0 ? $". {axes}" : "") + (item.ReadOnlyReason is { } r ? $". Read-only: {r}" : "");
        }

        private string CellToolTip(int column, int row)
        {
            if (current is not { } item || image is null || item.Kind == A2lItemKind.Ascii || ItemCell(column, row) is not var (x, y)) return "";
            string text = $"raw {image.ReadRaw(item, x, y):G}, file {Format(OriginalPhysical(item, x, y), item.Conversion)}";
            if (session?.StateOf(item, x, y) is { } state) text += $", ECU {state switch
            {
                CalibrationSyncState.InSync => "in sync",
                CalibrationSyncState.Pending => "pending",
                CalibrationSyncState.Writing => "writing",
                _ => "write failed",
            }}";
            return text;
        }

        // ── Editing ──────────────────────────────────────────────────────────────────────────────────────

        private List<(int X, int Y)> SelectedItemCells() => grid.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => ItemCell(c.ColumnIndex, c.RowIndex)).OfType<(int, int)>().Distinct().ToList();

        private void ApplyOperation(TableOperation operation)
        {
            if (!double.TryParse(operandBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double operand) &&
                !double.TryParse(operandBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out operand))
            {
                context.Log($"'{operandBox.Text}' is not a number.");
                return;
            }
            EditSelection(operation.ToString(), (values, cells) => TableEdits.Arithmetic(values, cells, operation, operand));
        }

        private void EditSelection(string what, Func<double[,], List<(int X, int Y)>, IReadOnlyList<CellValue>> make)
        {
            if (!CanEditCells) return;
            var cells = SelectedItemCells();
            if (cells.Count == 0) return;
            Edit($"{what} {cells.Count} cell(s) of {current!.Name}", () => make(Values(current), cells));
        }

        private void Edit(string description, Func<IReadOnlyList<CellValue>> make)
        {
            try { image!.SetPhysical(current!, make(), description); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                context.Log($"Edit refused: {ex.Message}");
            }
        }

        private void OnCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
        {
            if (updatingGrid || !grid.IsCurrentCellInEditMode || current is null || ItemCell(e.ColumnIndex, e.RowIndex) is not var (x, y)) return;
            string text = e.FormattedValue?.ToString()?.Trim() ?? "";
            if (text == grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                context.Log($"'{text}' is not a number.");
                grid.CancelEdit();
                return;
            }
            Edit($"Edit {current.Name}[{x},{y}]", () => [new CellValue(x, y, value)]);
            grid.CancelEdit(); // the refresh shows the stored (rounded) value
        }

        private void OnGridKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.Control) return;
            switch (e.KeyCode)
            {
                case Keys.C when current is { Kind: not A2lItemKind.Ascii } item && SelectedItemCells() is { Count: > 0 } cells:
                    var range = CellRange.Bounding(cells);
                    Clipboard.SetText(TableEdits.FormatBlock(Values(item), range, Decimals(item.Conversion)));
                    break;
                case Keys.V when CanEditCells && Clipboard.ContainsText():
                    PasteBlock(Clipboard.GetText());
                    break;
                case Keys.Z: image?.Undo(); break;
                case Keys.Y: image?.Redo(); break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>Pastes a spreadsheet block with its top-left at the selection's top-left.</summary>
        internal void PasteBlock(string text)
        {
            if (!CanEditCells || SelectedItemCells() is not { Count: > 0 } selected) return;
            var item = current!;
            var origin = CellRange.Bounding(selected);
            Edit($"Paste into {item.Name}", () =>
            {
                var block = TableEdits.ParseBlock(text);
                int rows = block.GetLength(0), columns = block.GetLength(1);
                bool flat = item.Kind != A2lItemKind.Map;
                if (flat && rows > 1) throw new FormatException($"{item.Name} has one row; the clipboard has {rows}.");
                if (origin.X0 + columns > item.CountX || origin.Y0 + rows > item.CountY)
                    throw new FormatException($"A {columns} × {rows} block does not fit at [{origin.X0},{origin.Y0}] of {item.Name}.");
                var cells = new List<CellValue>();
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < columns; c++)
                        cells.Add(new CellValue(origin.X0 + c, origin.Y0 + r, block[r, c]));
                return cells;
            });
        }

        // ── State ────────────────────────────────────────────────────────────────────────────────────────

        private void QueueRefresh()
        {
            if (refreshQueued || IsGone || !IsHandleCreated) { if (!IsHandleCreated && !IsGone) UpdateState(); return; }
            refreshQueued = true;
            BeginInvoke(() =>
            {
                refreshQueued = false;
                if (IsGone) return;
                RefreshGrid();
                itemsView.Invalidate();
                UpdateState();
            });
        }

        private void UpdateState()
        {
            bool idle = !context.IsBusy;
            var status = session?.Status;
            bool canSync = idle && image is not null && context.IsConnected && session is null;
            openButton.Enabled = idle;
            openA2lButton.Enabled = idle && image is not null;
            saveAsButton.Enabled = image is not null;
            undoButton.Enabled = image?.CanUndo == true;
            redoButton.Enabled = image?.CanRedo == true;
            toolTip.SetToolTip(undoButton, image?.UndoDescription ?? "");
            toolTip.SetToolTip(redoButton, image?.RedoDescription ?? "");
            startButton.Enabled = canSync;
            stopButton.Enabled = session is not null;
            holdBox.Enabled = session is null || status == LiveTuningStatus.Syncing;
            applyButton.Enabled = idle && status == LiveTuningStatus.Syncing && holdBox.Checked && session!.PendingBytes > 0;
            refreshButton.Enabled = idle && session is not null && context.IsConnected;
            sendButton.Enabled = takeButton.Enabled = idle && status is LiveTuningStatus.Comparing or LiveTuningStatus.Syncing
                && session!.PendingBytes > 0;
            bool editable = CanEditCells && grid.SelectedCells.Count > 0;
            setOpButton.Enabled = addOpButton.Enabled = multiplyOpButton.Enabled = percentOpButton.Enabled =
                interpolateButton.Enabled = smoothButton.Enabled = editable;
            grid.ReadOnly = !CanEditCells;

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
                boldFont.Dispose();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
