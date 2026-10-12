using System.Globalization;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab's Variables page: browse the build's variables (symbol catalog joined with the ECU's variable
    /// table), pick some to watch live over DAQ, or read them once (including objects larger than four bytes).
    /// </summary>
    internal sealed class CcpVariablesPanel : UserControl
    {
        private static readonly int[] Rates = [1, 2, 5, 10, 20, 50, 100];

        private readonly ICcpTabContext context;
        private readonly Button loadButton = new() { Text = "Load Variables", AutoSize = true };
        private readonly TextBox searchBox = new() { Width = 220, PlaceholderText = "search names (space-separated terms)" };
        private readonly CheckBox calibrationBox = new() { Text = "Include calibration", AutoSize = true };
        private readonly Button addButton = new() { Text = "Add to Watch →", AutoSize = true };
        private readonly Label countLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0) };
        private readonly ListView variablesView = new() { View = View.Details, FullRowSelect = true, VirtualMode = true, Dock = DockStyle.Fill, HideSelection = false };
        private readonly ComboBox rateBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        private readonly Button startButton = new() { Text = "Start Watch", AutoSize = true };
        private readonly Button stopButton = new() { Text = "Stop Watch", AutoSize = true };
        private readonly Button readOnceButton = new() { Text = "Read Once", AutoSize = true };
        private readonly Button removeButton = new() { Text = "Remove", AutoSize = true };
        private readonly Button clearButton = new() { Text = "Clear", AutoSize = true };
        private readonly ListView watchView = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 250 };

        private IReadOnlyList<CcpVariable> all = [];
        private List<CcpVariable> filtered = [];
        private readonly List<CcpVariable> watched = [];
        private readonly Dictionary<CcpVariable, string> readValues = [];
        private CcpVariableWatch? watch;
        private CcpBuildProfile? loadedFor;

        public CcpVariablesPanel(ICcpTabContext context)
        {
            this.context = context;
            variablesView.Columns.Add("Name", 300);
            variablesView.Columns.Add("Address", 90);
            variablesView.Columns.Add("Size", 45);
            variablesView.Columns.Add("Unit", 70);
            variablesView.Columns.Add("Table #", 60);
            watchView.Columns.Add("Name", 230);
            watchView.Columns.Add("Value", 110);
            watchView.Columns.Add("Unit", 60);
            watchView.Columns.Add("Raw", 80);
            watchView.Columns.Add("Updates", 65);
            rateBox.Items.AddRange(Rates.Select(r => (object)$"{r} Hz").ToArray());
            rateBox.SelectedIndex = Array.IndexOf(Rates, 10);
            GuiIcons.ApplyToButton(loadButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(startButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(stopButton, GuiIcons.Stop);
            GuiIcons.ApplyToButton(readOnceButton, GuiIcons.Read);
            GuiIcons.ApplyToButton(removeButton, GuiIcons.Delete);
            GuiIcons.ApplyToButton(clearButton, GuiIcons.Clear);

            var browseBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
            browseBar.Controls.AddRange([loadButton, searchBox, calibrationBox, addButton, countLabel]);
            var watchBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
            watchBar.Controls.AddRange([new Label { Text = "Rate", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
                rateBox, startButton, stopButton, readOnceButton, removeButton, clearButton]);
            var split = new SplitContainer { Dock = DockStyle.Fill };
            // Size the panes once the page has its real width; a distance set at construction time is lost.
            split.HandleCreated += (_, _) => split.SplitterDistance = Math.Max(200, split.Width * 55 / 100);
            split.Panel1.Controls.Add(variablesView);
            split.Panel2.Controls.Add(watchView);
            split.Panel2.Controls.Add(watchBar);
            Controls.Add(split);
            Controls.Add(browseBar);

            loadButton.Click += async (_, _) => await LoadAsync();
            searchBox.TextChanged += (_, _) => ApplyFilter();
            calibrationBox.CheckedChanged += (_, _) => ApplyFilter();
            addButton.Click += (_, _) => AddSelected();
            variablesView.DoubleClick += (_, _) => AddSelected();
            variablesView.RetrieveVirtualItem += (_, e) => e.Item = VariableItem(filtered[e.ItemIndex]);
            startButton.Click += async (_, _) => await StartWatchAsync();
            stopButton.Click += async (_, _) => await StopWatchAsync();
            readOnceButton.Click += async (_, _) => await ReadOnceAsync();
            removeButton.Click += (_, _) => RemoveSelected();
            clearButton.Click += (_, _) => { watched.Clear(); readValues.Clear(); ShowWatched(); };
            refreshTimer.Tick += (_, _) => RefreshValues();
            context.SessionChanged += (_, _) => OnSessionChanged();
            UpdateState();
        }

        private CcpBuildProfile? Build => context.Identification?.Build;

        /// <summary>Joins the catalog with the variable table, reading the table first if it has not been read.</summary>
        private async Task LoadAsync()
        {
            if (Build is not { } build)
            {
                context.Log("Variables need a recognised build: no catalog or variable-table location is known.");
                return;
            }
            await context.RunAsync("Load variables", async ct =>
            {
                if (context.Registry is null && build.SymbolEntriesAddress is not null)
                    context.Registry = await context.Master!.ReadSymbolRegistryAsync(build, cancellationToken: ct);
                var catalog = SymbolCatalogLoader.LoadByName(build.Build);
                all = CcpVariableCatalog.Build(catalog, context.Registry, build);
                loadedFor = build;
                context.Log($"Variables: {all.Count:N0} for {build.Build} (catalog {(catalog is null ? "missing" : $"{catalog.Entries.Count:N0} symbols")}, " +
                    $"variable table {(context.Registry is null ? "not available" : $"{context.Registry.Entries.Count} entries")}).");
                ApplyFilter();
            });
        }

        private void ApplyFilter()
        {
            var terms = searchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = all.Where(v => (calibrationBox.Checked || !v.IsCalibration) &&
                terms.All(t => v.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
            variablesView.VirtualListSize = filtered.Count;
            variablesView.Invalidate();
            countLabel.Text = all.Count == 0 ? "" : $"{filtered.Count:N0} of {all.Count:N0} shown";
        }

        private static ListViewItem VariableItem(CcpVariable v) => new(
        [
            v.Name, $"0x{v.Address:X8}", v.Size.ToString(CultureInfo.InvariantCulture), v.Unit,
            v.RegistryIndex?.ToString(CultureInfo.InvariantCulture) ?? "",
        ]) { ToolTipText = v.Comment, ForeColor = v.Watchable ? SystemColors.WindowText : SystemColors.GrayText };

        private void AddSelected()
        {
            if (watch is not null) return;
            foreach (int index in variablesView.SelectedIndices)
                if (!watched.Contains(filtered[index])) watched.Add(filtered[index]);
            ShowWatched();
        }

        private void RemoveSelected()
        {
            if (watch is not null) return;
            foreach (var variable in watchView.SelectedItems.Cast<ListViewItem>().Select(i => (CcpVariable)i.Tag!).ToList())
            {
                watched.Remove(variable);
                readValues.Remove(variable);
            }
            ShowWatched();
        }

        private void ShowWatched()
        {
            watchView.BeginUpdate();
            watchView.Items.Clear();
            foreach (var v in watched)
                watchView.Items.Add(new ListViewItem([v.Name, readValues.GetValueOrDefault(v, ""), v.Unit, "", ""]) { Tag = v });
            watchView.EndUpdate();
            UpdateState();
        }

        private async Task StartWatchAsync()
        {
            var master = context.Master;
            if (master is null || watched.Count == 0 || watch is not null) return;
            int rate = Rates[Math.Max(0, rateBox.SelectedIndex)];
            await context.RunAsync("Start watch", async ct =>
            {
                watch = await CcpVariableWatch.StartAsync(master, watched.ToList(), rate, ct);
                refreshTimer.Start();
                context.Log($"Watching {watched.Count} variable(s) at {rate} Hz over DAQ.");
            });
            UpdateState();
        }

        private async Task StopWatchAsync()
        {
            if (watch is not { } running) return;
            watch = null;
            refreshTimer.Stop();
            RefreshValues(running);
            await running.DisposeAsync();
            context.Log("Watch stopped.");
            UpdateState();
        }

        private async Task ReadOnceAsync()
        {
            if (context.Master is not { } master || watched.Count == 0) return;
            await context.RunAsync("Read variables", async ct =>
            {
                foreach (var v in watched)
                {
                    var bytes = await master.ReadAsync(v.Address, v.Size, cancellationToken: ct);
                    readValues[v] = v.Watchable
                        ? v.ToChannel().Decode(bytes).ToString("G6", CultureInfo.InvariantCulture)
                        : Convert.ToHexString(bytes);
                }
                ShowWatched();
                context.Log($"Read {watched.Count} variable(s).");
            });
        }

        private void RefreshValues() => RefreshValues(watch);

        private void RefreshValues(CcpVariableWatch? source)
        {
            if (source is null) return;
            var values = source.Snapshot();
            for (int i = 0; i < values.Count && i < watchView.Items.Count; i++)
            {
                if (values[i] is not { } value) continue;
                var item = watchView.Items[i];
                item.SubItems[1].Text = value.Value.ToString("G6", CultureInfo.InvariantCulture);
                item.SubItems[3].Text = value.RawHex;
                item.SubItems[4].Text = value.Updates.ToString(CultureInfo.InvariantCulture);
            }
            if (!source.IsActive && ReferenceEquals(source, watch))
            {
                watch = null;
                refreshTimer.Stop();
                context.Log("Watch ended: the session closed or its DAQ lists were stopped.");
                UpdateState();
            }
        }

        private void OnSessionChanged()
        {
            if (Build != loadedFor && loadedFor is not null)
            {
                all = [];
                loadedFor = null;
                ApplyFilter();
            }
            if (watch is not null && !watch.IsActive) RefreshValues();
            UpdateState();
        }

        private void UpdateState()
        {
            bool idle = !context.IsBusy;
            bool connected = context.IsConnected;
            loadButton.Enabled = idle && connected && Build is not null;
            addButton.Enabled = watch is null && all.Count > 0;
            removeButton.Enabled = clearButton.Enabled = watch is null && watched.Count > 0;
            rateBox.Enabled = watch is null;
            startButton.Enabled = idle && connected && watch is null && watched.Count > 0;
            stopButton.Enabled = watch is not null;
            readOnceButton.Enabled = idle && connected && watched.Count > 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                if (watch is { } running)
                {
                    watch = null;
                    _ = running.DisposeAsync().AsTask();
                }
            }
            base.Dispose(disposing);
        }
    }
}
