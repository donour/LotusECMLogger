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
    /// <summary>The Tuning page's live operating point (Phase 6e).</summary>
    internal sealed partial class CcpTuningPanel
    {
        // ── Live operating point ─────────────────────────────────────────────────────────────────────────

        /// <summary>Starts or stops the cursor when the item, the session or the Cursor switch changes.</summary>
        private void EnsureCursor()
        {
            (A2lCalibrationItem?, CcpMaster?) wanted = cursorBox.Checked && current is not null && a2l is not null && image is not null && context.IsConnected
                ? (current, context.Master) : (null, null);
            if (wanted == cursorFor) return;
            cursorFor = wanted;
            _ = RestartCursorAsync(wanted.Item1, wanted.Item2);
        }

        private async Task RestartCursorAsync(A2lCalibrationItem? item, CcpMaster? master)
        {
            int generation = ++cursorGeneration;
            cursorTimer.Stop();
            if (liveCursor is { } old)
            {
                liveCursor = null;
                try { await old.DisposeAsync(); }
                catch (Exception ex) { Debug.WriteLine($"CcpTuningPanel: stopping the cursor failed: {ex.Message}"); }
            }
            if (IsGone || generation != cursorGeneration) return;
            grid?.SetCursor(null);
            graphPane.Plot.SetCursor(null);
            liveLabel.Text = "";
            if (item is null || master is null || a2l is null) return;
            var (x, y, problems) = LiveCursor.Inputs(a2l.Module, item);
            if (x is null && y is null)
            {
                liveLabel.Text = item.Kind is A2lItemKind.Map or A2lItemKind.Curve
                    ? (problems.Count > 0 ? $"No live cursor: {problems[0]}" : "No live cursor: no axis of this table has an input bound in the A2L.") : "";
                return;
            }
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
                cursorTimer.Start();
            }
            catch (Exception ex)
            {
                if (!IsGone && generation == cursorGeneration) liveLabel.Text = $"Live cursor unavailable: {ex.Message}";
            }
        }

        private void SampleCursor()
        {
            if (liveCursor is not { } cursor || IsGone) return;
            if (!cursor.IsActive)
            {
                cursorTimer.Stop();
                liveCursor = null;
                cursorFor = default;
                _ = cursor.DisposeAsync().AsTask();
                grid?.SetCursor(null);
                graphPane.Plot.SetCursor(null);
                liveLabel.Text = "Live cursor stopped: its DAQ list was stopped or the session closed.";
                return;
            }
            if (grid is null || image is null || !ReferenceEquals(cursor.Item, current) || cursor.Read(image) is not { } sample) return;
            grid.SetCursor(sample);
            var at = graphData?.CursorAt(sample);
            graphPane.Plot.SetCursor(at);
            liveLabel.Text = LiveText(cursor.Item, sample, at?.Value);
        }

        /// <summary>"Live  X coolant_tmp: 30.0 °C (raw 112)   Y rpm: 2406 rpm (raw 60)   → 14.60 AFR"</summary>
        private static string LiveText(A2lCalibrationItem item, OperatingPointSample sample, double? value)
        {
            var parts = new List<string> { "Live" };
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
