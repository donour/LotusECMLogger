using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The Tuning page's ECU load strip (CCP-client-plan.md, 6g): the firmware's own timing and stack statistics, from
    /// its own DAQ lists while connected, from a running recording (which logs the same channels), or from a replay.
    /// Clicking the strip opens the per-slot table and Reset maxima.
    /// </summary>
    internal sealed partial class CcpTuningPanel
    {
        private readonly CheckBox ecuBox = new() { Text = "ECU", AutoSize = true, Appearance = Appearance.Button, Checked = true };
        private readonly EcuLoadStrip ecuStrip = new() { Dock = DockStyle.Bottom, Height = 20, Visible = false };
        private readonly System.Windows.Forms.Timer ecuTimer = new() { Interval = 250 };
        private readonly EcuLoadCounters ecuCounters = new();
        private EcuLoadMonitor? ecuMonitor;
        private int ecuGeneration;
        private (EcuLoadMap? Map, CcpMaster? Master) ecuFor;
        private double ecuReplayTime = double.NegativeInfinity;
        private ToolStripDropDown? ecuDetails;
        private ListView? ecuList;
        private Button? ecuResetButton;

        internal EcuLoadStrip EcuStrip => ecuStrip;
        internal EcuLoadMonitor? EcuMonitor => ecuMonitor;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool EcuLoadEnabled { get => ecuBox.Checked; set => ecuBox.Checked = value; }

        private void InitializeEcuLoad()
        {
            ecuBox.Image = GuiIcons.Render("", 14, SystemColors.ControlText);
            ecuBox.TextImageRelation = TextImageRelation.ImageBeforeText;
            toolTip.SetToolTip(ecuBox, "Show the ECU's own load figures: main-loop time, timer-interrupt time per rate, stack use " +
                "(streams them over two DAQ lists, about 50 frames a second)");
            toolTip.SetToolTip(ecuStrip, "The ECU's own timing and stack statistics. Click for the per-rate table and Reset maxima.");
            ecuBox.CheckedChanged += (_, _) => EnsureEcuLoad();
            ecuTimer.Tick += (_, _) => SampleEcuLoad();
            ecuStrip.Click += (_, _) => ShowEcuDetails();
        }

        /// <summary>
        /// Starts or stops the page's own ECU-load watch: wanted while the switch is on, connected to an ECU whose build has
        /// a map, and neither recording (the recording logs the channels) nor replaying.
        /// </summary>
        private void EnsureEcuLoad()
        {
            var liveMap = ecuBox.Checked && context.IsConnected && recorder is null && recording is null
                ? EcuLoadMap.For(context.Identification?.Build?.Build) : null;
            (EcuLoadMap?, CcpMaster?) wanted = liveMap is not null && context.Master is { } master ? (liveMap, master) : (null, null);
            if (wanted != ecuFor)
            {
                ecuFor = wanted;
                ecuCounters.Reset();
                _ = RestartEcuMonitorAsync(wanted.Item1, wanted.Item2);
            }
            bool show = ecuBox.Checked && (wanted.Item1 is not null || EcuSource() is not null);
            ecuStrip.Visible = show;
            if (show && !ecuTimer.Enabled) ecuTimer.Start();
            if (!show)
            {
                ecuTimer.Stop();
                ecuDetails?.Close();
            }
            if (show) SampleEcuLoad();
        }

        private async Task RestartEcuMonitorAsync(EcuLoadMap? map, CcpMaster? master)
        {
            int generation = ++ecuGeneration;
            await StopEcuMonitorAsync();
            if (IsGone || generation != ecuGeneration || map is null || master is null) return;
            ecuStrip.ShowMessage("ECU load: starting…");
            try
            {
                var started = await EcuLoadMonitor.StartAsync(master, map);
                if (IsGone || generation != ecuGeneration)
                {
                    await started.DisposeAsync();
                    return;
                }
                ecuMonitor = started;
                ecuStrip.ShowMessage("ECU load: waiting for the ECU…");
            }
            catch (Exception ex)
            {
                if (!IsGone && generation == ecuGeneration)
                {
                    ecuStrip.ShowMessage($"ECU load unavailable: {ex.Message}");
                    context.Log($"ECU load: could not start its DAQ lists: {ex.Message}");
                }
            }
        }

        private async Task StopEcuMonitorAsync()
        {
            if (ecuMonitor is not { } old) return;
            ecuMonitor = null;
            try { await old.DisposeAsync(); }
            catch (Exception ex) { Debug.WriteLine($"CcpTuningPanel: stopping the ECU load watch failed: {ex.Message}"); }
        }

        /// <summary>Where the figures come from now: the replay at its time, the running recording, or the page's own watch.</summary>
        private (IRawValueSource Source, EcuLoadMap Map, string Prefix)? EcuSource()
        {
            if (recording is not null)
                return EcuLoadMap.For(image?.Build.Build) is { } replayed ? (recording.At(replayTime), replayed, "Replay") : null;
            if (recorder is not null)
                return EcuLoadMap.For(recorder.Build.Build) is { } recorded ? (recorder, recorded, "Recording") : null;
            return ecuMonitor is { IsActive: true } monitor ? (monitor, monitor.Map, "") : null;
        }

        private void SampleEcuLoad()
        {
            if (IsGone || !ecuStrip.Visible) return;
            if (ecuMonitor is { IsActive: false } stopped)
            {
                ecuMonitor = null;
                ecuFor = default;
                _ = stopped.DisposeAsync().AsTask();
                ecuStrip.ShowMessage("ECU load stopped: its DAQ lists were stopped or the session closed. Toggle ECU to restart it.");
                return;
            }
            if (EcuSource() is not { } source) return; // starting, or unavailable: the message says which
            if (recording is not null)
            {
                if (replayTime < ecuReplayTime) ecuCounters.Reset();
                ecuReplayTime = replayTime;
            }
            var sample = EcuLoadSample.Read(source.Source, source.Map);
            if (!sample.HasAny)
            {
                ecuStrip.ShowMessage(recording is not null ? "ECU load: not in this recording (it was made without the ECU switch, or before 6g)."
                    : recorder is not null ? "ECU load: this recording does not log it (the ECU switch was off when it started)."
                    : "ECU load: waiting for the ECU…");
                if (ecuDetails?.Visible == true) FillEcuDetails(null);
                return;
            }
            ecuStrip.Show(EcuLoadText.Segments(sample, ecuCounters.Since(sample)));
            if (ecuDetails?.Visible == true) FillEcuDetails(sample);
        }

        // ── Details ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The details table's rows, for tests (empty until opened).</summary>
        internal IReadOnlyList<string[]> EcuDetailRows => ecuList?.Items.Cast<ListViewItem>()
            .Select(i => i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToArray()).ToList() ?? [];

        internal void CloseEcuDetails() => ecuDetails?.Close();

        internal void ShowEcuDetails()
        {
            if (ecuDetails is null)
            {
                ecuList = new ListView
                {
                    View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                };
                ecuList.Columns.Add("", 170);
                ecuList.Columns.Add("Runs every", 90);
                ecuList.Columns.Add("Last", 80, HorizontalAlignment.Right);
                ecuList.Columns.Add("Max", 80, HorizontalAlignment.Right);
                ecuList.Columns.Add("Max / period", 90, HorizontalAlignment.Right);
                ecuResetButton = new Button { Text = "Reset maxima", AutoSize = true };
                GuiIcons.ApplyToButton(ecuResetButton, "");
                ecuResetButton.Click += async (_, _) => await ResetEcuMaximaAsync();
                var note = new Label
                {
                    AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(4, 6, 0, 0),
                    Text = "Wall time, including higher-priority interrupts. Slot 2 includes slots 3–10, slot 6 includes 7–10. " +
                        "Reset maxima clears the slot and interrupt-period figures; the main-loop and CCP figures are since power-on.",
                };
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(2) };
                bottom.Controls.Add(ecuResetButton);
                bottom.Controls.Add(note);
                var host = new Panel { Size = new Size(530, 380) };
                host.Controls.Add(ecuList);
                host.Controls.Add(bottom);
                ecuDetails = new ToolStripDropDown { Padding = Padding.Empty };
                ecuDetails.Items.Add(new ToolStripControlHost(host) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = host.Size });
            }
            ecuDetails.Show(ecuStrip, new Point(0, -ecuDetails.PreferredSize.Height));
            SampleEcuLoad();
            if (EcuSource() is null) FillEcuDetails(null);
        }

        private void FillEcuDetails(EcuLoadSample? sample)
        {
            if (ecuList is null || ecuResetButton is null) return;
            string Period(double us) => us >= 1000 ? $"{(us / 1000).ToString("0.#", CultureInfo.CurrentCulture)} ms" : $"{us:0} µs";
            string Share(double? max, double period) => max is { } m ? (m / period).ToString("0%", CultureInfo.CurrentCulture) : "";
            var rows = new List<ListViewItem>();
            for (int slot = 0; slot < EcuLoadMap.SlotCount; slot++)
            {
                double period = EcuLoadMap.SlotPeriodsUs[slot];
                var max = sample?.SlotMaxUs[slot];
                // Only slots 0 and 2 stream their last time; every slot's maximum is streamed.
                var row = new ListViewItem([$"Slot {slot}: {EcuLoadMap.SlotRates[slot]}", Period(period),
                    slot is 0 or 2 ? Micro(sample?.SlotLastUs[slot]) : "", Micro(max), Share(max, period)]);
                if (slot == 0) row.ForeColor = EcuLoadStrip.ColorOf(EcuLoadSample.IsrLevel(max));
                rows.Add(row);
            }
            var periodMax = sample?.IsrPeriodMaxUs;
            rows.Add(new ListViewItem(["Interrupt period", "500 µs", Micro(sample?.IsrPeriodUs), Micro(periodMax), Share(periodMax, EcuLoadMap.IsrPeriodUs)])
            {
                ForeColor = EcuLoadStrip.ColorOf(EcuLoadSample.IsrPeriodLevel(periodMax)),
            });
            rows.Add(new ListViewItem(["Interrupt period, min", "", Micro(sample?.IsrPeriodMinUs), "", ""]));
            rows.Add(new ListViewItem(["Main loop", "free-running", Milli(sample?.MainLoopUs), Milli(sample?.MainLoopMaxUs), ""]));
            rows.Add(new ListViewItem(["CCP background step", "each loop", "", Micro(sample?.CcpBackgroundMaxUs), ""]));
            var used = sample?.StackUsedBytes;
            rows.Add(new ListViewItem(["Stack used (deepest)", "4 KB area",
                used is { } u ? $"{u:N0} B" : sample?.StackFound == false ? "measuring" : "—",
                used is { } u2 ? $"{EcuLoadMap.StackBytes - u2:N0} B free" : "", used is { } u3 ? Share(u3, EcuLoadMap.StackBytes) : ""])
            {
                ForeColor = EcuLoadStrip.ColorOf(EcuLoadSample.StackLevel(used)),
            });
            ecuList.BeginUpdate();
            ecuList.Items.Clear();
            ecuList.Items.AddRange(rows.ToArray());
            ecuList.EndUpdate();
            ecuResetButton.Enabled = ecuMonitor is { IsActive: true } && recorder is null && recording is null && context.IsConnected && !context.IsBusy;
            toolTip.SetToolTip(ecuResetButton, ecuResetButton.Enabled ? "Write the ECU's statistics reset request (one RAM byte, not calibration)"
                : "Needs the live ECU-load watch (connected, not recording or replaying)");

            static string Micro(double? us) => us is null ? "—" : EcuLoadText.Us(us) + " µs";
            static string Milli(double? us) => us is null ? "—" : EcuLoadText.Ms(us) + " ms";
        }

        internal async Task ResetEcuMaximaAsync()
        {
            if (ecuMonitor is not { IsActive: true } monitor || context.Master is not { } master) return;
            var map = monitor.Map;
            await context.RunAsync("Reset ECU maxima", async ct =>
            {
                await EcuLoadMonitor.ResetMaximaAsync(master, map, ct);
                context.Log($"ECU load: reset the slot and interrupt-period maxima (wrote 1 to 0x{map.StatsResetRequest:X8}; the ECU clears it).");
            });
        }

        private void DisposeEcuLoad()
        {
            ecuTimer.Dispose();
            ecuDetails?.Dispose();
            if (ecuMonitor is { } monitor)
            {
                ecuMonitor = null;
                _ = monitor.DisposeAsync().AsTask().ContinueWith(t => Debug.WriteLine($"CcpTuningPanel: stopping the ECU load watch failed: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
