using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls
{
    /// <summary>The Tuning page's operating point (Phase 6e): where the ECU is, or was, reading the shown table.</summary>
    internal sealed partial class CcpTuningPanel
    {
        // ── Operating point ──────────────────────────────────────────────────────────────────────────────

        // The shown table's bound inputs, resolved once per table.
        private TableInputs? tableInputs;

        /// <summary>
        /// Points the cursor at the shown table when the item, the session, a recording or the Cursor switch changes.
        /// A replay or a running recording supplies every input; otherwise the table's own DAQ watch streams them while
        /// connected.
        /// </summary>
        private void EnsureCursor()
        {
            var item = cursorBox.Checked && image is not null && a2l is not null ? current : null;
            if (!ReferenceEquals(tableInputs?.Item, item)) tableInputs = item is null ? null : TableInputs.For(a2l!.Module, item);
            bool ownWatch = item is not null && tableInputs!.Any && recorder is null && recording is null && context.IsConnected;
            (A2lCalibrationItem?, CcpMaster?) wanted = ownWatch ? (item, context.Master) : (null, null);
            if (wanted != cursorFor)
            {
                cursorFor = wanted;
                _ = RestartCursorAsync(wanted.Item1, wanted.Item2);
            }
            if (item is null || !tableInputs!.Any)
            {
                cursorTimer.Stop();
                grid?.SetCursor(null);
                graphPane.Plot.SetCursor(null);
                liveLabel.Text = item is { Kind: A2lItemKind.Map or A2lItemKind.Curve } && tableInputs is { } none
                    ? (none.Problems.Count > 0 ? $"No live cursor: {none.Problems[0]}" : "No live cursor: no axis of this table has an input bound in the A2L.")
                    : "";
                return;
            }
            if (!cursorTimer.Enabled) cursorTimer.Start();
            if (recording is not null || recorder is not null) SampleCursor();
        }

        /// <summary>Starts the table's own watch, or just stops the old one when <paramref name="item"/> is null.</summary>
        private async Task RestartCursorAsync(A2lCalibrationItem? item, CcpMaster? master)
        {
            int generation = ++cursorGeneration;
            if (liveCursor is { } old)
            {
                liveCursor = null;
                try { await old.DisposeAsync(); }
                catch (Exception ex) { Debug.WriteLine($"CcpTuningPanel: stopping the cursor failed: {ex.Message}"); }
            }
            if (IsGone || generation != cursorGeneration || item is null || master is null || a2l is null) return;
            grid?.SetCursor(null);
            graphPane.Plot.SetCursor(null);
            try
            {
                var started = await LiveCursor.StartAsync(master, a2l.Module, item);
                if (IsGone || generation != cursorGeneration)
                {
                    if (started is not null) await started.DisposeAsync();
                    return;
                }
                liveCursor = started;
                liveLabel.Text = "Live: waiting for the ECU…";
            }
            catch (Exception ex)
            {
                if (!IsGone && generation == cursorGeneration) liveLabel.Text = $"Live cursor unavailable: {ex.Message}";
            }
        }

        private void SampleCursor()
        {
            if (IsGone || grid is null || image is null || tableInputs is not { Any: true } inputs || !ReferenceEquals(inputs.Item, current)) return;
            IRawValueSource source;
            string prefix;
            if (recording is not null) (source, prefix) = (recording.At(replayTime), $"Replay {ReplayClock(replayTime)}");
            else if (recorder is not null) (source, prefix) = (recorder, "Live (recording)");
            else if (liveCursor is { } cursor)
            {
                if (!cursor.IsActive)
                {
                    liveCursor = null;
                    cursorFor = default;
                    _ = cursor.DisposeAsync().AsTask();
                    cursorTimer.Stop();
                    grid.SetCursor(null);
                    graphPane.Plot.SetCursor(null);
                    liveLabel.Text = "Live cursor stopped: its DAQ list was stopped or the session closed.";
                    return;
                }
                if (!ReferenceEquals(cursor.Item, current)) return;
                (source, prefix) = (cursor, "Live");
            }
            else
            {
                // Offline (nothing to show), or the table's watch is still starting (its label says so).
                if (cursorFor.Item1 is null)
                {
                    grid.SetCursor(null);
                    graphPane.Plot.SetCursor(null);
                    liveLabel.Text = "";
                }
                return;
            }
            if (inputs.Sample(source, image) is not { } sample)
            {
                if (recording is not null)
                {
                    grid.SetCursor(null);
                    graphPane.Plot.SetCursor(null);
                    liveLabel.Text = $"{prefix}: not every input of this table has a recorded value yet.";
                }
                return;
            }
            grid.SetCursor(sample);
            var at = graphData?.CursorAt(sample);
            graphPane.Plot.SetCursor(at);
            liveLabel.Text = LiveText(prefix, inputs.Item, sample, at?.Value);
        }

        /// <summary>"Live  X coolant_tmp: 30.0 °C (raw 112)   Y rpm: 2406 rpm (raw 60)   → 14.60 AFR"</summary>
        private static string LiveText(string prefix, A2lCalibrationItem item, OperatingPointSample sample, double? value)
        {
            var parts = new List<string> { prefix };
            void Axis(string name, int index, double? input)
            {
                if (input is not { } raw || index >= item.Axes.Count) return;
                var axis = item.Axes[index];
                parts.Add($"{name} {GraphData.AxisTitle(axis)}: {CalibrationGrid.Format(axis.Conversion.ToPhysical(raw), axis.Conversion)} " +
                    $"(raw {raw.ToString(CultureInfo.CurrentCulture)})");
            }
            Axis("X", 0, sample.XInput);
            Axis("Y", 1, sample.YInput);
            if (value is { } v) parts.Add($"→ {CalibrationGrid.Format(v, item.Conversion)} {item.Conversion.Unit}".TrimEnd());
            return string.Join("    ", parts);
        }
    }
}
