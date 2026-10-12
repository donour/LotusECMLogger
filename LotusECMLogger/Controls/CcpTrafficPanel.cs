using System.Globalization;
using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The CCP tab's Traffic page: session counters and command latency, which DAQ list belongs to whom, and a
    /// decoded trace of recent commands and replies. Refreshes twice a second while visible.
    /// </summary>
    internal sealed class CcpTrafficPanel : UserControl
    {
        private const int MaxTraceRows = 1000;

        private readonly ICcpTabContext context;
        private readonly Label statsLabel = new()
        {
            Dock = DockStyle.Top, Height = 54, Font = new Font("Consolas", 9F), Padding = new Padding(6, 4, 6, 0),
        };
        private readonly ListView allocationsView = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
        private readonly ListView traceView = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        private readonly CheckBox pauseBox = new() { Text = "Pause", AutoSize = true };
        private readonly Button clearButton = new() { Text = "Clear View", AutoSize = true };
        private readonly Button saveButton = new() { Text = "Save Trace…", AutoSize = true };
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 500 };
        private CcpMaster? shown;
        private long lastSequence;

        public CcpTrafficPanel(ICcpTabContext context)
        {
            this.context = context;
            foreach (var (name, width) in new[] { ("List", 40), ("Owner", 140), ("Event", 50), ("Rate", 60), ("Prescaler", 70),
                ("ODTs", 45), ("First PID", 70), ("Samples", 80), ("Overruns", 70), ("Dropped", 70), ("Gaps", 50) })
                allocationsView.Columns.Add(name, width);
            foreach (var (name, width) in new[] { ("#", 70), ("Time", 110), ("Kind", 90), ("Detail", 520), ("Latency", 80) })
                traceView.Columns.Add(name, width);
            GuiIcons.ApplyToButton(clearButton, GuiIcons.Clear);
            GuiIcons.ApplyToButton(saveButton, GuiIcons.Save);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            bar.Controls.AddRange([pauseBox, clearButton, saveButton,
                new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(8, 6, 0, 0),
                    Text = "DAQ frames are counted above, not traced." }]);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            // Allocations need a few rows; give the trace the rest once the page has its real height.
            split.HandleCreated += (_, _) => split.SplitterDistance = Math.Clamp(split.Height / 3, 80, 160);
            split.Panel1.Controls.Add(allocationsView);
            split.Panel2.Controls.Add(traceView);
            split.Panel2.Controls.Add(bar);
            Controls.Add(split);
            Controls.Add(statsLabel);

            clearButton.Click += (_, _) => traceView.Items.Clear();
            saveButton.Click += (_, _) => SaveTrace();
            refreshTimer.Tick += (_, _) => { if (Visible) RefreshView(); };
            refreshTimer.Start();
            RefreshView();
        }

        private void RefreshView()
        {
            var master = context.Master;
            if (!ReferenceEquals(master, shown))
            {
                shown = master;
                lastSequence = 0;
                traceView.Items.Clear();
            }
            if (master is null)
            {
                statsLabel.Text = "Not connected.";
                allocationsView.Items.Clear();
                return;
            }
            ShowStatistics(master.GetStatistics());
            ShowAllocations(master);
            if (!pauseBox.Checked) AppendTrace(master.Trace.Snapshot(lastSequence));
        }

        private void ShowStatistics(CcpMasterStatistics s)
        {
            string latency = s.MeanLatencyMs is { } mean
                ? string.Create(CultureInfo.InvariantCulture,
                    $"latency last {s.LastLatencyMs:F2} / mean {mean:F2} / p95 {s.P95LatencyMs:F2} / max {s.MaxLatencyMs:F2} ms")
                : "latency: no replies yet";
            statsLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"commands {s.Commands:N0}  replies {s.Replies:N0}  error replies {s.ErrorReplies}  timeouts {s.Timeouts}  " +
                $"cancelled {s.Cancellations}  stray {s.StrayReplies}  uncertain {s.UncertainTransitions}\r\n" +
                $"DAQ frames {s.DaqFrames:N0}  unrouted {s.UnroutedDaqFrames}  malformed {s.MalformedFrames}  events {s.EventPackets}\r\n{latency}");
            statsLabel.ForeColor = s.Timeouts + s.StrayReplies + s.MalformedFrames + s.UnroutedDaqFrames > 0
                ? Color.DarkOrange : SystemColors.ControlText;
        }

        private void ShowAllocations(CcpMaster master)
        {
            var rates = master.Target.EventRatesHz;
            allocationsView.BeginUpdate();
            allocationsView.Items.Clear();
            foreach (var a in master.GetDaqAllocations())
            {
                string rate = rates.TryGetValue(a.EventChannel, out int hz)
                    ? (hz / (double)a.Prescaler).ToString("0.##", CultureInfo.InvariantCulture) + " Hz" : "?";
                allocationsView.Items.Add(new ListViewItem(
                [
                    a.SlaveList.ToString(CultureInfo.InvariantCulture), a.Owner, a.EventChannel.ToString(CultureInfo.InvariantCulture),
                    rate, a.Prescaler.ToString(CultureInfo.InvariantCulture), a.Odts.ToString(CultureInfo.InvariantCulture),
                    a.FirstPid.ToString(CultureInfo.InvariantCulture), a.Samples.ToString("N0", CultureInfo.InvariantCulture),
                    a.Overruns.ToString(CultureInfo.InvariantCulture), a.Dropped.ToString(CultureInfo.InvariantCulture),
                    a.Gaps.ToString(CultureInfo.InvariantCulture),
                ]));
            }
            allocationsView.EndUpdate();
        }

        private void AppendTrace(IReadOnlyList<CcpTraceEntry> entries)
        {
            if (entries.Count == 0) return;
            traceView.BeginUpdate();
            foreach (var entry in entries.TakeLast(MaxTraceRows))
            {
                traceView.Items.Add(new ListViewItem(
                [
                    entry.Sequence.ToString(CultureInfo.InvariantCulture), entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    entry.Kind.ToString(), CcpTrace.Describe(entry),
                    entry.LatencyMs?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                ]) { ForeColor = ColorFor(entry.Kind) });
            }
            while (traceView.Items.Count > MaxTraceRows) traceView.Items.RemoveAt(0);
            traceView.EndUpdate();
            traceView.Items[^1].EnsureVisible();
            lastSequence = entries[^1].Sequence;
        }

        private static Color ColorFor(CcpTraceKind kind) => kind switch
        {
            CcpTraceKind.ErrorReply or CcpTraceKind.Timeout or CcpTraceKind.StrayReply or CcpTraceKind.Malformed => Color.Firebrick,
            CcpTraceKind.Cancelled or CcpTraceKind.Event => Color.DarkOrange,
            CcpTraceKind.State => Color.SteelBlue,
            CcpTraceKind.Reply => SystemColors.GrayText,
            _ => SystemColors.WindowText,
        };

        private void SaveTrace()
        {
            if (context.Master is not { } master) return;
            Directory.CreateDirectory(LoggerPaths.OutputDirectory);
            using var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                InitialDirectory = LoggerPaths.OutputDirectory,
                FileName = $"ccp-trace_{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var text = new StringBuilder();
            var s = master.GetStatistics();
            text.AppendLine(CultureInfo.InvariantCulture, $"CCP trace, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {master.Target.Name}");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"commands {s.Commands}, replies {s.Replies}, error replies {s.ErrorReplies}, timeouts {s.Timeouts}, stray {s.StrayReplies}, " +
                $"DAQ frames {s.DaqFrames}, unrouted {s.UnroutedDaqFrames}, malformed {s.MalformedFrames}");
            var entries = master.Trace.Snapshot();
            foreach (var entry in entries)
            {
                string bytes = entry.Bytes is { } b ? $"  [{Convert.ToHexString(b)}]" : "";
                string latency = entry.LatencyMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"  {ms:F2} ms") : "";
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"{entry.Sequence,8}  {entry.Time:HH:mm:ss.fff}  {entry.Kind,-10}  {CcpTrace.Describe(entry)}{bytes}{latency}");
            }
            File.WriteAllText(dialog.FileName, text.ToString());
            context.Log($"Saved {entries.Count:N0} trace entries to {dialog.FileName}.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refreshTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
