using System.ComponentModel;
using System.Globalization;
using LotusECMLogger.Models;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The Tuning page's recording (step 2 of recording and replay): Record logs every A2L axis input plus a chosen
    /// preset's channels, the calibration writes and the calibration at start and stop, while the live cursor reads the
    /// recording instead of its own watch.
    /// </summary>
    internal sealed partial class CcpTuningPanel
    {
        private const string InputsOnly = "Axis inputs only";

        private readonly ComboBox recordChannelsBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, DropDownWidth = 260 };
        private readonly Button recordButton = new() { Text = "Record", AutoSize = true };
        private readonly Label recordLabel = new() { AutoSize = true, Padding = new Padding(4, 7, 0, 2), ForeColor = Color.Firebrick };
        private readonly System.Windows.Forms.Timer recordTimer = new() { Interval = 1000 };
        private TuningRecorder? recorder;
        private string? lastRecordingPath;

        /// <summary>Where recordings are saved. Replaceable so UI tests keep their files out of Documents.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string RecordingDirectory { get; set; } = Path.Combine(LoggerPaths.OutputDirectory, "recordings");

        internal TuningRecorder? Recorder => recorder;

        private void InitializeRecording()
        {
            recordChannelsBox.Items.Add(InputsOnly);
            recordChannelsBox.SelectedIndex = 0;
            recordChannelsBox.DropDown += (_, _) => FillRecordChannels();
            toolTip.SetToolTip(recordChannelsBox, "Every variable an A2L axis reads is always recorded, so any table can be replayed. " +
                "Pick a High-Speed Log preset to record its channels as well.");
            recordButton.Click += async (_, _) => await (recorder is null ? StartRecordingAsync() : StopRecordingAsync(null));
            toolTip.SetToolTip(recordButton, "Record the axis inputs, the chosen channels and every calibration write, to replay later");
            recordTimer.Tick += (_, _) => UpdateRecordLabel();
        }

        /// <summary>The presets for the open calibration's build, keeping the current choice.</summary>
        private void FillRecordChannels()
        {
            string? chosen = recordChannelsBox.SelectedItem as string;
            string prefix = image is null ? "" : image.Build.Build + "_";
            var presets = HighSpeedLogPresetLoader.GetAvailablePresets().Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            recordChannelsBox.BeginUpdate();
            recordChannelsBox.Items.Clear();
            recordChannelsBox.Items.Add(InputsOnly);
            foreach (var preset in presets) recordChannelsBox.Items.Add(preset);
            recordChannelsBox.SelectedItem = chosen is not null && recordChannelsBox.Items.Contains(chosen) ? chosen : InputsOnly;
            recordChannelsBox.EndUpdate();
        }

        /// <summary>The chosen preset's selected channels at their rates, or none.</summary>
        private (string? Preset, List<(HighSpeedChannel, int)> Channels) PresetChannels()
        {
            if (recordChannelsBox.SelectedItem is not string preset || preset == InputsOnly) return (null, []);
            var loaded = HighSpeedLogPresetLoader.LoadByName(preset);
            foreach (var warning in loaded.Warnings) context.Log($"{preset}: {warning}");
            return (preset, loaded.Channels.Where(c => c.DefaultSelected).Select(c => (c, c.DefaultRate)).ToList());
        }

        /// <summary>Starts recording to <paramref name="path"/> (default: a timestamped file in <see cref="RecordingDirectory"/>).</summary>
        internal async Task StartRecordingAsync(string? path = null)
        {
            if (recorder is not null || recording is not null || image is null || a2l is null) return;
            if (context.Host is not { } host || context.Master is not { } master || !context.IsConnected)
            {
                context.Log("Recording needs the CCP session: connect first.");
                return;
            }
            if (context.Identification?.Build?.Build != image.Build.Build)
            {
                context.Log($"Recording needs the ECU to be identified as {image.Build.Build}; it is {context.Identification?.Build?.Build ?? "not identified"}.");
                return;
            }
            (string? Preset, List<(HighSpeedChannel, int)> Channels) extra;
            try { extra = PresetChannels(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                context.Log($"Could not load the preset: {ex.Message}");
                return;
            }
            string log = path ?? Path.Combine(RecordingDirectory,
                string.Create(CultureInfo.InvariantCulture, $"{image.Build.Build}_{DateTime.Now:yyyyMMdd-HHmmss}.csv"));
            var (module, build, sync) = (a2l, image.Build, session);
            await context.RunAsync("Start recording", async ct =>
            {
                var started = await TuningRecorder.StartAsync(host, master, module, build, extra.Channels, log, sync, cancellationToken: ct);
                started.Failed += OnRecordingFailed;
                recorder = started;
                context.Log($"Recording to {started.Files.Log}: {started.InputCount} axis input(s) at {TuningRecorder.InputRateHz} Hz" +
                    (extra.Preset is { } p ? $" and {started.Channels.Count - started.InputCount} channel(s) from {p}" : "") +
                    $". The calibration at the start is saved{(sync is null ? "" : "; live sync's writes are recorded")}.");
            });
            if (recorder is not null) recordTimer.Start();
            QueueRefresh();
        }

        /// <summary>Stops recording and saves the calibration the ECU then holds. <paramref name="reason"/> is logged first.</summary>
        internal async Task StopRecordingAsync(string? reason)
        {
            if (recorder is not { } stopping) return;
            recorder = null;
            stopping.Failed -= OnRecordingFailed;
            recordTimer.Stop();
            if (reason is not null) context.Log(reason);
            async Task Stop(CancellationToken ct)
            {
                var result = await stopping.StopAsync(context.IsConnected ? context.Master : null, cancellationToken: ct);
                lastRecordingPath = result.Files.Log;
                context.Log($"Recording saved: {result.Files.Log} ({result.Edits} calibration write(s)" +
                    (result.DroppedFrames > 0 ? $", {result.DroppedFrames} frame(s) dropped" : "") + "). Open Recording replays it.");
                if (result.CalibrationEndProblem is { } problem) context.Log(problem);
            }
            if (!context.IsBusy) await context.RunAsync("Stop recording", Stop);
            else
            {
                try { await Stop(CancellationToken.None); }
                catch (Exception ex) { context.Log($"Stopping the recording failed: {ex.Message}"); }
            }
            QueueRefresh();
        }

        // The log stops by itself when the session or transport fails; raised on a worker thread.
        private void OnRecordingFailed(object? sender, string message)
        {
            if (IsGone || !IsHandleCreated) return;
            BeginInvoke(async () => await StopRecordingAsync($"Recording stopped: {message}"));
        }

        private void UpdateRecordLabel()
        {
            if (recorder is not { } r)
            {
                recordLabel.Text = "";
                return;
            }
            var elapsed = DateTime.Now - r.Started;
            recordLabel.Text = string.Create(CultureInfo.CurrentCulture,
                $"● {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} · {r.EditCount} write(s)") +
                (r.DroppedFrames > 0 ? $" · {r.DroppedFrames} dropped" : "");
            toolTip.SetToolTip(recordLabel, $"{r.Files.Log}\n{r.Channels.Count} channel(s), {r.InputCount} of them axis inputs");
        }
    }
}
