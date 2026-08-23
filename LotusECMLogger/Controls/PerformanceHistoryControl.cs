using LotusECMLogger.Services;
using System.ComponentModel;
using System.Globalization;

namespace LotusECMLogger.Controls
{
    /// <summary>Overview, usage histograms, and retained events from Mode 22 0x03xx data.</summary>
    public sealed class PerformanceHistoryControl : UserControl
    {
        private readonly IPerformanceHistoryService service;
        private readonly Button readButton = new();
        private readonly Label statusLabel = new();
        private readonly ListView overviewView = NewListView();
        private readonly ListView usageView = NewListView();
        private readonly ListView eventsView = NewListView();
        private readonly Label notesLabel = new();
        private bool isLoggerActive;
        private bool isBusy;

        public PerformanceHistoryControl() : this(new J2534PerformanceHistoryService())
        {
        }

        internal PerformanceHistoryControl(IPerformanceHistoryService service)
        {
            this.service = service;
            BuildUi();
        }

        /// <summary>The history reader needs exclusive access to the J2534 device.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsLoggerActive
        {
            get => isLoggerActive;
            set
            {
                isLoggerActive = value;
                UpdateButtonState();
            }
        }

        private void BuildUi()
        {
            SuspendLayout();

            var header = new Panel { Dock = DockStyle.Top, Height = 56 };
            readButton.Location = new Point(12, 12);
            readButton.Size = new Size(184, 32);
            readButton.Text = "Read Performance History";
            readButton.UseVisualStyleBackColor = true;
            readButton.Click += ReadButton_Click;
            GuiIcons.ApplyToButton(readButton, GuiIcons.Read);

            statusLabel.AutoEllipsis = true;
            statusLabel.Location = new Point(208, 17);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.Size = new Size(700, 24);
            statusLabel.Text = "Connect to the vehicle and read the persistent ECU history.";
            header.Controls.Add(readButton);
            header.Controls.Add(statusLabel);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var overviewTab = new TabPage("Overview");
            var usageTab = new TabPage("Usage");
            var eventsTab = new TabPage("Events");
            tabs.TabPages.AddRange([overviewTab, usageTab, eventsTab]);

            SetupOverviewTab(overviewTab);
            SetupUsageTab(usageTab);
            SetupEventsTab(eventsTab);

            Controls.Add(tabs);
            Controls.Add(header);
            ResumeLayout(false);
        }

        private void SetupOverviewTab(TabPage tab)
        {
            overviewView.Columns.Add("Parameter", 230);
            overviewView.Columns.Add("Value", 180);
            overviewView.Columns.Add("Details", 500);

            notesLabel.Dock = DockStyle.Bottom;
            notesLabel.Height = 66;
            notesLabel.Padding = new Padding(8, 5, 8, 5);
            notesLabel.AutoEllipsis = true;
            notesLabel.ForeColor = SystemColors.GrayText;

            tab.Controls.Add(overviewView);
            tab.Controls.Add(notesLabel);
        }

        private void SetupUsageTab(TabPage tab)
        {
            usageView.Columns.Add("Category", 190);
            usageView.Columns.Add("ECU range", 150);
            usageView.Columns.Add("Time", 150);
            usageView.Columns.Add("Share", 90);
            usageView.Columns.Add("Raw 100 ms samples", 160);
            usageView.Columns.Add("PID", 90);
            tab.Controls.Add(usageView);
        }

        private void SetupEventsTab(TabPage tab)
        {
            eventsView.Columns.Add("Event", 190);
            eventsView.Columns.Add("Rank", 60);
            eventsView.Columns.Add("Value", 110);
            eventsView.Columns.Add("Vehicle speed", 120);
            eventsView.Columns.Add("Engine speed", 120);
            eventsView.Columns.Add("Context", 150);
            eventsView.Columns.Add("At engine runtime", 170);
            tab.Controls.Add(eventsView);
        }

        private static ListView NewListView() => new()
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            View = View.Details,
            UseCompatibleStateImageBehavior = false,
        };

        private async void ReadButton_Click(object? sender, EventArgs e)
        {
            if (isLoggerActive)
            {
                MessageBox.Show("Stop active logging before reading performance history.", "Logger Active",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            readButton.Text = "Reading...";
            statusLabel.Text = "Reading Mode 22 0x03xx data...";

            try
            {
                PerformanceHistorySnapshot snapshot = await Task.Run(service.LoadPerformanceHistory);
                Populate(snapshot);
                statusLabel.Text = $"Loaded {snapshot.Variant} — calibration {snapshot.CalibrationId}";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Performance history unavailable";
                MessageBox.Show($"Failed to read performance history: {ex.Message}", "Read Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                readButton.Text = "Read Performance History";
                SetBusy(false);
            }
        }

        private void Populate(PerformanceHistorySnapshot snapshot)
        {
            overviewView.BeginUpdate();
            usageView.BeginUpdate();
            eventsView.BeginUpdate();
            try
            {
                overviewView.Items.Clear();
                usageView.Items.Clear();
                eventsView.Items.Clear();

                AddOverview("ECU variant", snapshot.Variant, "Selected from the CAL program version");
                AddOverview("Calibration", snapshot.CalibrationId, "Mode 22 PIDs 0x021C–0x0224");
                AddOverview("Engine runtime", FormatDuration(snapshot.EngineRuntime), "PID 0x0338");
                AddOverview("Distance recorded", snapshot.DistanceKm is double distance
                    ? $"{distance:N0} km" : "Not published by this variant", "PID 0x033A or 0x0341, variant-dependent");
                AddOverview("Standing starts", snapshot.StandingStartCount.ToString("N0"), "PID 0x0339");
                AddOverview("Fastest 0–100 km/h", FormatSeconds(snapshot.FastestZeroTo100Seconds), "PID 0x0334");
                AddOverview("Fastest 0–160 km/h", FormatSeconds(snapshot.FastestZeroTo160Seconds), "PID 0x0335");
                AddOverview("Last 0–100 km/h", FormatSeconds(snapshot.LastZeroTo100Seconds), "PID 0x0336");
                AddOverview("Last 0–160 km/h", FormatSeconds(snapshot.LastZeroTo160Seconds), "PID 0x0337");
                AddOverview("Low-oil-pressure events", snapshot.LowOilPressureEventCount.ToString("N0"), "PID 0x0361");

                foreach (IGrouping<string, PerformanceUsageBucket> group in snapshot.Usage.GroupBy(x => x.Category))
                {
                    ulong total = (ulong)group.Sum(x => (decimal)x.Samples);
                    foreach (PerformanceUsageBucket bucket in group)
                    {
                        string share = total == 0 ? "—" : $"{bucket.Samples * 100.0 / total:F1}%";
                        var item = new ListViewItem(bucket.Category);
                        item.SubItems.Add(bucket.Band);
                        item.SubItems.Add(FormatDuration(bucket.Duration));
                        item.SubItems.Add(share);
                        item.SubItems.Add(bucket.Samples.ToString("N0"));
                        item.SubItems.Add($"0x{bucket.Pid:X4}");
                        usageView.Items.Add(item);
                    }
                }

                foreach (PerformanceHistoryEvent historyEvent in snapshot.Events
                    .OrderBy(x => x.Category).ThenBy(x => x.Rank))
                {
                    var item = new ListViewItem(historyEvent.Category);
                    item.SubItems.Add($"#{historyEvent.Rank}");
                    item.SubItems.Add(FormatValue(historyEvent.Value, historyEvent.Unit));
                    item.SubItems.Add(historyEvent.VehicleSpeedKph is int speed ? $"{speed} km/h" : "—");
                    item.SubItems.Add(historyEvent.EngineSpeedRpm is int rpm ? $"{rpm:N0} rpm" : "—");
                    item.SubItems.Add(historyEvent.ContextValue is double context
                        ? FormatValue(context, historyEvent.ContextUnit ?? "") : "—");
                    item.SubItems.Add(historyEvent.EngineRuntime is TimeSpan runtime ? FormatDuration(runtime) : "—");
                    eventsView.Items.Add(item);
                }

                notesLabel.Text = string.Join("  ", snapshot.Notes.Select(note => $"• {note}"));
            }
            finally
            {
                overviewView.EndUpdate();
                usageView.EndUpdate();
                eventsView.EndUpdate();
            }
        }

        private void AddOverview(string parameter, string value, string details)
        {
            var item = new ListViewItem(parameter);
            item.SubItems.Add(value);
            item.SubItems.Add(details);
            overviewView.Items.Add(item);
        }

        private static string FormatSeconds(double? seconds) =>
            seconds.HasValue ? $"{seconds.Value:F1} s" : "No valid run recorded";

        private static string FormatValue(double value, string unit)
        {
            string number = unit is "rpm" or "km/h" ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("0.0", CultureInfo.CurrentCulture);
            return string.IsNullOrEmpty(unit) ? number : $"{number} {unit}";
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalDays >= 1)
                return $"{(int)value.TotalDays}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 100}";
        }

        private void SetBusy(bool value)
        {
            isBusy = value;
            UpdateButtonState();
        }

        private void UpdateButtonState() => readButton.Enabled = !isBusy && !isLoggerActive;
    }
}
