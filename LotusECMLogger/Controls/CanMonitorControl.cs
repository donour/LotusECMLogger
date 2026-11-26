using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CAN Monitor tab: a passive view of the raw frames on the bus (<see cref="CanBusMonitor"/>), either as a
    /// scrolling log or one row per arbitration ID, narrowed by an ID filter that applies to what was already
    /// captured as well as what arrives.
    /// </summary>
    public sealed class CanMonitorControl : UserControl
    {
        /// <summary>Frames kept for the log; the oldest tenth is dropped when it is full.</summary>
        internal const int MaxFrames = 500_000;

        private readonly CanBusMonitor monitor;
        private readonly AdapterAvailability adapter;
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 100 };

        private readonly Button startButton = new() { AutoSize = true, Text = "Start", UseVisualStyleBackColor = true };
        private readonly Button clearButton = new() { AutoSize = true, Text = "Clear", UseVisualStyleBackColor = true };
        private readonly Button saveButton = new() { AutoSize = true, Text = "Save CSV…", UseVisualStyleBackColor = true };
        private readonly TextBox filterBox = new() { Width = 260, PlaceholderText = "e.g. 7E0-7EF, 0A2, !3E9" };
        private readonly CheckBox groupBox = new() { AutoSize = true, Text = "One row per ID", Checked = true };
        private readonly CheckBox pauseBox = new() { AutoSize = true, Text = "Pause display" };
        private readonly CheckBox scrollBox = new() { AutoSize = true, Text = "Auto-scroll", Checked = true };
        private readonly Label statusLabel = new() { Dock = DockStyle.Top, Height = 24, Padding = new Padding(6, 4, 6, 0), AutoEllipsis = true };
        private readonly ListView view = new BufferedListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true,
            Font = new Font("Consolas", 9.75f), HideSelection = false,
        };

        private readonly List<CanBusFrame> frames = [];
        private readonly List<CanBusFrame> shown = [];
        private readonly Dictionary<(bool Extended, uint Id), IdStats> stats = [];
        private List<IdStats> rows = [];
        private CanIdFilter filter = CanIdFilter.All;
        private long totalFrames;
        private string? lastError;
        private string? filterError;
        private readonly Queue<(long Tick, long Total)> rateWindow = new();

        public CanMonitorControl() : this(new CanBusMonitor()) { }

        internal CanMonitorControl(CanBusMonitor monitor)
        {
            this.monitor = monitor;
            BuildUi();
            monitor.Failed += (_, error) => BeginInvokeSafe(() =>
            {
                lastError = $"Stopped: {error}";
                UpdateState();
            });
            adapter = AdapterAvailability.Track(this, UpdateState, null, CanBusMonitor.Holder);
            refreshTimer.Tick += (_, _) => RefreshFromMonitor();
            Disposed += (_, _) =>
            {
                refreshTimer.Dispose();
                monitor.Dispose();
            };
            ConfigureColumns();
            UpdateState();
        }

        internal ListView FrameView => view;
        internal TextBox FilterBox => filterBox;
        internal CheckBox GroupByIdBox => groupBox;
        internal Button StartButton => startButton;
        internal string StatusText => statusLabel.Text;

        /// <summary>Stops capturing and releases the adapter (when the app closes).</summary>
        public void StopMonitor()
        {
            monitor.Stop();
            RefreshFromMonitor();
            UpdateState();
        }

        private void BuildUi()
        {
            SuspendLayout();
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 6, 6, 2),
            };
            var filterLabel = new Label { AutoSize = true, Text = "Arbitration ID filter (hex):", Margin = new Padding(16, 8, 2, 0) };
            foreach (var box in new[] { groupBox, pauseBox, scrollBox }) box.Margin = new Padding(12, 6, 0, 0);
            filterBox.Margin = new Padding(2, 4, 0, 0);
            bar.Controls.AddRange([startButton, clearButton, saveButton, filterLabel, filterBox, groupBox, pauseBox, scrollBox]);

            GuiIcons.ApplyToButton(startButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(clearButton, GuiIcons.Clear);
            GuiIcons.ApplyToButton(saveButton, GuiIcons.Save);

            startButton.Click += StartButton_Click;
            clearButton.Click += (_, _) => ClearCapture();
            saveButton.Click += SaveButton_Click;
            filterBox.TextChanged += (_, _) => ApplyFilter();
            groupBox.CheckedChanged += (_, _) => { ConfigureColumns(); RefreshView(force: true); };
            pauseBox.CheckedChanged += (_, _) => RefreshView(force: true);
            view.RetrieveVirtualItem += View_RetrieveVirtualItem;

            Controls.Add(view);
            Controls.Add(statusLabel);
            Controls.Add(bar);
            ResumeLayout(true);
        }

        private void ConfigureColumns()
        {
            view.BeginUpdate();
            view.VirtualListSize = 0;
            view.Columns.Clear();
            if (groupBox.Checked)
            {
                view.Columns.Add("ID", 90);
                view.Columns.Add("Type", 50);
                view.Columns.Add("DLC", 45, HorizontalAlignment.Right);
                view.Columns.Add("Data", 260);
                view.Columns.Add("Count", 80, HorizontalAlignment.Right);
                view.Columns.Add("Period (ms)", 90, HorizontalAlignment.Right);
                view.Columns.Add("Last (s)", 90, HorizontalAlignment.Right);
            }
            else
            {
                view.Columns.Add("Time (s)", 100, HorizontalAlignment.Right);
                view.Columns.Add("ID", 90);
                view.Columns.Add("Type", 50);
                view.Columns.Add("DLC", 45, HorizontalAlignment.Right);
                view.Columns.Add("Data", 260);
            }
            view.EndUpdate();
        }

        private void StartButton_Click(object? sender, EventArgs e)
        {
            if (monitor.IsRunning)
            {
                StopMonitor();
                return;
            }
            try
            {
                lastError = null;
                monitor.Start();
                refreshTimer.Start();
            }
            catch (Exception ex) when (ex is BrokerBusyException or IOException or InvalidOperationException)
            {
                lastError = ex.Message;
            }
            UpdateState();
        }

        private void ClearCapture()
        {
            monitor.Drain();
            frames.Clear();
            shown.Clear();
            stats.Clear();
            rows.Clear();
            totalFrames = 0;
            rateWindow.Clear();
            RefreshView(force: true);
        }

        private void ApplyFilter()
        {
            if (!CanIdFilter.TryParse(filterBox.Text, out var parsed, out var error))
            {
                filterBox.BackColor = Color.MistyRose;
                filterError = error;
                UpdateStatus();
                return;
            }
            filterBox.BackColor = SystemColors.Window;
            filterError = null;
            filter = parsed;
            shown.Clear();
            foreach (var frame in frames)
                if (filter.Matches(frame.Id)) shown.Add(frame);
            RefreshView(force: true);
        }

        /// <summary>Moves captured frames into the log and the per-ID table, then refreshes the view.</summary>
        internal void RefreshFromMonitor()
        {
            var batch = monitor.Drain();
            foreach (var frame in batch)
            {
                frames.Add(frame);
                if (filter.Matches(frame.Id)) shown.Add(frame);

                var key = (frame.Extended, frame.Id);
                if (!stats.TryGetValue(key, out var s)) stats[key] = s = new IdStats(frame.Id, frame.Extended);
                s.Add(frame);
            }
            totalFrames += batch.Count;

            if (frames.Count > MaxFrames)
            {
                frames.RemoveRange(0, MaxFrames / 10);
                shown.Clear();
                foreach (var frame in frames)
                    if (filter.Matches(frame.Id)) shown.Add(frame);
            }

            if (!monitor.IsRunning && refreshTimer.Enabled)
                refreshTimer.Stop();
            RefreshView(force: false);
        }

        private void RefreshView(bool force)
        {
            if (!pauseBox.Checked || force)
            {
                if (groupBox.Checked)
                {
                    rows = stats.Values.Where(s => filter.Matches(s.Id))
                        .OrderBy(s => s.Extended).ThenBy(s => s.Id).ToList();
                    if (view.VirtualListSize != rows.Count) view.VirtualListSize = rows.Count;
                    view.Invalidate();
                }
                else
                {
                    if (view.VirtualListSize != shown.Count)
                    {
                        view.VirtualListSize = shown.Count;
                        if (scrollBox.Checked && shown.Count > 0 && !pauseBox.Checked)
                            view.EnsureVisible(shown.Count - 1);
                    }
                    view.Invalidate();
                }
            }
            UpdateStatus();
        }

        private void View_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            if (groupBox.Checked)
            {
                if (e.ItemIndex >= rows.Count) { e.Item = Blank(7); return; }
                var s = rows[e.ItemIndex];
                e.Item = new ListViewItem([
                    s.IdText, s.Extended ? "Ext" : "Std", s.Data.Length.ToString(CultureInfo.InvariantCulture),
                    s.DataText, s.Count.ToString("N0", CultureInfo.InvariantCulture),
                    s.PeriodMs is double p ? p.ToString("F1", CultureInfo.InvariantCulture) : "",
                    (s.LastMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture),
                ]);
            }
            else
            {
                if (e.ItemIndex >= shown.Count) { e.Item = Blank(5); return; }
                var f = shown[e.ItemIndex];
                e.Item = new ListViewItem([
                    (f.TimeMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture), f.IdText,
                    f.Extended ? "Ext" : "Std", f.Data.Length.ToString(CultureInfo.InvariantCulture), f.DataText,
                ]);
            }
        }

        private static ListViewItem Blank(int columns) => new(Enumerable.Repeat("", columns).ToArray());

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"canbus_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var sb = new StringBuilder("time_ms,id,extended,dlc,data\n");
                foreach (var f in shown)
                    sb.Append(CultureInfo.InvariantCulture, $"{f.TimeMs:F3},{f.IdText},{(f.Extended ? 1 : 0)},{f.Data.Length},{f.DataText}\n");
                File.WriteAllText(dialog.FileName, sb.ToString());
                lastError = null;
                statusLabel.Text = $"Saved {shown.Count:N0} frames to {dialog.FileName}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Save CSV", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateState()
        {
            bool running = monitor.IsRunning;
            startButton.Text = running ? "Stop" : "Start";
            GuiIcons.ApplyToButton(startButton, running ? GuiIcons.Stop : GuiIcons.Play);
            startButton.Enabled = running || !adapter.Blocked;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            long now = Environment.TickCount64;
            rateWindow.Enqueue((now, totalFrames));
            while (rateWindow.Count > 1 && now - rateWindow.Peek().Tick > 2000) rateWindow.Dequeue();
            var (t0, n0) = rateWindow.Peek();
            double rate = now > t0 ? (totalFrames - n0) * 1000.0 / (now - t0) : 0;

            string state = monitor.IsRunning ? $"Capturing, {rate:F0} frames/s" :
                adapter.Blocked ? adapter.Reason ?? "The adapter is in use." : "Stopped. Start listens to the bus without transmitting.";
            string counts = $"{totalFrames:N0} frames, {stats.Count} IDs, {(groupBox.Checked ? rows.Count + " IDs" : shown.Count.ToString("N0", CultureInfo.InvariantCulture) + " frames")} shown";
            if (monitor.Dropped > 0) counts += $", {monitor.Dropped:N0} dropped";
            string? error = filterError ?? lastError;
            statusLabel.Text = error is null ? $"{state} — {counts}" : $"{error} — {counts}";
            statusLabel.ForeColor = error is null ? SystemColors.ControlText : Color.Firebrick;
        }

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        /// <summary>The latest frame and timing of one arbitration ID.</summary>
        private sealed class IdStats(uint id, bool extended)
        {
            public uint Id { get; } = id;
            public bool Extended { get; } = extended;
            public string IdText => Extended ? $"{Id:X8}" : $"{Id:X3}";
            public long Count { get; private set; }
            public byte[] Data { get; private set; } = [];
            public string DataText { get; private set; } = "";
            public double LastMs { get; private set; }
            private double firstMs;

            /// <summary>Mean interval between frames since the first one.</summary>
            public double? PeriodMs => Count > 1 ? (LastMs - firstMs) / (Count - 1) : null;

            public void Add(CanBusFrame frame)
            {
                if (Count == 0) firstMs = frame.TimeMs;
                Count++;
                LastMs = frame.TimeMs;
                Data = frame.Data;
                DataText = frame.DataText;
            }
        }

        private sealed class BufferedListView : ListView
        {
            public BufferedListView() => DoubleBuffered = true;
        }
    }
}
