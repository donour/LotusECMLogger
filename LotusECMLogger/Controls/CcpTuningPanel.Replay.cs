using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// The Tuning page's replay (steps 3 and 4 of recording and replay): open a recording to see its calibration as it
    /// stood at any moment, the cursor where the ECU was reading the shown table, and (Cells read) how long it spent at
    /// each cell. A timeline scrubs or plays the recording, with a tick at each calibration write. Replay is read-only:
    /// closing it returns to the calibration that was open before.
    /// </summary>
    internal sealed partial class CcpTuningPanel
    {
        private static readonly double[] Speeds = [0.25, 0.5, 1, 2, 4, 8, 16];

        private readonly Button openRecordingButton = new() { Text = "Open Recording…", AutoSize = true };
        // Play and speed on the left, the time, Cells read and Close on the right, the timeline filling the rest.
        private readonly Panel replayBar = new() { Dock = DockStyle.Top, Height = 34, Visible = false, BackColor = Color.FromArgb(246, 240, 250) };
        private readonly Button playButton = new() { Text = "Play", AutoSize = true };
        private readonly ComboBox speedBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
        private readonly ReplayTimeline timeline = new() { Dock = DockStyle.Fill };
        private readonly Label replayTimeLabel = new() { AutoSize = true, Padding = new Padding(2, 7, 4, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        private readonly CheckBox heatBox = new() { Text = "Cells read", AutoSize = true, Appearance = Appearance.Button };
        private readonly Label activityLabel = new() { AutoSize = true, Padding = new Padding(4, 7, 4, 0), ForeColor = SystemColors.GrayText };
        private readonly Button closeReplayButton = new() { Text = "Close Recording", AutoSize = true };
        private readonly System.Windows.Forms.Timer playTimer = new() { Interval = 40 };
        private readonly Stopwatch playClock = new();

        private TuningRecording? recording;
        private ReplayCalibration? replayCalibration;
        private byte[]? replayStart;
        private (CalibrationImage? Image, A2lResolvedModule? A2l, string? A2lPath) beforeReplay;
        private double replayTime, playFrom, replaySpeed = 1;
        private int activityGeneration;

        internal TuningRecording? Recording => recording;
        internal double ReplayTime => replayTime;
        internal ReplayTimeline Timeline => timeline;
        internal bool IsPlaying => playTimer.Enabled;

        private void InitializeReplay()
        {
            GuiIcons.ApplyToButton(openRecordingButton, GuiIcons.OpenFile);
            GuiIcons.ApplyToButton(playButton, GuiIcons.Play);
            GuiIcons.ApplyToButton(closeReplayButton, "");
            heatBox.Image = GuiIcons.Render("", 14, SystemColors.ControlText);
            heatBox.TextImageRelation = TextImageRelation.ImageBeforeText;
            foreach (double speed in Speeds) speedBox.Items.Add(string.Create(CultureInfo.CurrentCulture, $"{speed:0.##}×"));
            speedBox.SelectedIndex = Array.IndexOf(Speeds, 1.0);
            toolTip.SetToolTip(openRecordingButton, "Replay a recording: its calibration at any moment, where the ECU read each table, and how long");
            toolTip.SetToolTip(heatBox, "Shade the shown table's cells by how long the ECU read them in the recording (or the Shift+dragged range)");
            toolTip.SetToolTip(speedBox, "Playback speed");

            var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(2) };
            left.Controls.AddRange([playButton, speedBox]);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(2) };
            right.Controls.AddRange([replayTimeLabel, heatBox, activityLabel, closeReplayButton]);
            timeline.Padding = new Padding(6, 2, 6, 2);
            replayBar.Controls.Add(timeline);
            replayBar.Controls.Add(left);
            replayBar.Controls.Add(right);
            speedBox.Margin = new Padding(3, 5, 3, 3);

            openRecordingButton.Click += (_, _) => BrowseRecording();
            closeReplayButton.Click += (_, _) => CloseRecording();
            playButton.Click += (_, _) => TogglePlay();
            playTimer.Tick += (_, _) => OnPlayTick();
            speedBox.SelectedIndexChanged += (_, _) =>
            {
                replaySpeed = Speeds[Math.Max(0, speedBox.SelectedIndex)];
                RestartPlayClock();
            };
            timeline.PositionChanged += (_, _) =>
            {
                SeekReplay(timeline.Position);
                RestartPlayClock();
            };
            timeline.RangeChanged += async (_, _) => await UpdateActivityAsync();
            heatBox.CheckedChanged += async (_, _) => await UpdateActivityAsync();
        }

        private void BrowseRecording()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Recordings and high-speed logs (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(RecordingDirectory) ? RecordingDirectory : LoggerPaths.OutputDirectory,
                FileName = lastRecordingPath is null ? "" : Path.GetFileName(lastRecordingPath),
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) _ = OpenRecordingAsync(dialog.FileName);
        }

        /// <summary>
        /// Opens a recording for replay: its start calibration (or, for a plain high-speed log, the open calibration of the
        /// same build) with the build's A2L, at time 0.
        /// </summary>
        internal async Task OpenRecordingAsync(string path)
        {
            if (recorder is not null || session is not null)
            {
                context.Log("Stop recording and live sync before opening a recording.");
                return;
            }
            if (recording is null && image is { HasUnsavedChanges: true } &&
                !Confirm("The open calibration has unsaved changes. Replaying a recording sets it aside (Close Recording returns to it). Continue?"))
                return;
            TuningRecording loaded;
            try { loaded = await Task.Run(() => TuningRecording.Load(path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                context.Log($"Could not open {Path.GetFileName(path)}: {ex.Message}");
                return;
            }
            var openImage = recording is null ? image : beforeReplay.Image;
            string? buildName = loaded.Build ?? openImage?.Build.Build;
            if (CcpBuildProfile.Known.FirstOrDefault(b => b.Build == buildName) is not { } build)
            {
                context.Log($"{Path.GetFileName(path)} does not say which ECU it was recorded on. Open a calibration of that build first.");
                return;
            }
            byte[]? start = loaded.CalibrationStart;
            string? startSource = loaded.Files.CalibrationStart;
            if (start is null && openImage?.Build == build)
            {
                start = openImage.Bytes(openImage.BaseAddress, openImage.Size).ToArray();
                startSource = openImage.SourcePath;
                context.Log($"{Path.GetFileName(path)} has no calibration saved with it; its tables are shown with the open file's values and axes.");
            }
            if (start is null)
            {
                context.Log($"{Path.GetFileName(path)} has no calibration saved with it. Open the {build.Build} calibration it was recorded with, then open it again.");
                return;
            }
            CalibrationImage replayImage;
            try { replayImage = CalibrationImage.FromBytes(start, build, startSource); }
            catch (InvalidDataException ex)
            {
                context.Log($"Could not use the recording's calibration: {ex.Message}");
                return;
            }
            if (FindA2l(build.Build) is not { } found || ReadA2l(found, replayImage) is not { } resolved)
            {
                context.Log($"Replay needs {build.Build}'s A2L in config/a2l.");
                return;
            }
            if (loaded.Unreadable.Count > 0)
            {
                // A log from before the channel: preamble lines: take each channel's scaling from the A2L at its address.
                loaded = await Task.Run(() => TuningRecording.Load(path, c => TuningRecorder.Complete(resolved.Module, c)));
                if (loaded.Unreadable.Count > 0)
                    context.Log($"{loaded.Unreadable.Count} channel(s) cannot be replayed: the log does not say how they were scaled " +
                        $"(e.g. {string.Join(", ", loaded.Unreadable.Take(3).Select(c => c.Name))}).");
            }

            Pause();
            if (recording is null) beforeReplay = (image, a2l, a2lPath);
            if (image is not null) image.Changed -= OnImageChanged;
            (recording, replayCalibration, replayStart, image, a2l, a2lPath) =
                (loaded, new ReplayCalibration(replayImage, loaded.Edits), start, replayImage, resolved, found);
            image.Changed += OnImageChanged;
            timeline.Duration = loaded.Duration;
            timeline.Marks = loaded.Edits.Select(e => e.Time).ToList();
            timeline.Range = null;
            replayTime = -1;
            SeekReplay(0);
            context.Log($"Replaying {Path.GetFileName(path)}: {ReplayTimeline.Format(loaded.Duration)}, {loaded.Series().Count()} channel(s), " +
                $"{loaded.Edits.Count} calibration write(s), {build.Build}.");
            LoadTree();
            QueueRefresh();
        }

        /// <summary>Leaves replay and returns to the calibration that was open before it.</summary>
        internal void CloseRecording()
        {
            if (recording is null) return;
            Pause();
            if (image is not null) image.Changed -= OnImageChanged;
            (image, a2l, a2lPath) = beforeReplay;
            if (image is not null) image.Changed += OnImageChanged;
            (recording, replayCalibration, replayStart, beforeReplay) = (null, null, null, default);
            context.Log("Closed the recording.");
            LoadTree();
            QueueRefresh();
        }

        /// <summary>Moves the replay to <paramref name="time"/> seconds: the calibration as it then stood, and the cursor.</summary>
        internal void SeekReplay(double time)
        {
            if (recording is null || replayCalibration is null) return;
            double t = Math.Clamp(time, 0, recording.Duration);
            if (t == replayTime) return;
            replayTime = t;
            replayCalibration.SeekTo(t);
            timeline.Position = t;
            replayTimeLabel.Text = $"{ReplayClock(t)} / {ReplayClock(recording.Duration)}";
            SampleCursor();
        }

        /// <summary>A moment of the replay as its labels show it: m:ss.f, or seconds and milliseconds in a recording under ten seconds.</summary>
        private string ReplayClock(double time) =>
            recording is { Duration: < 10 } ? ReplayTimeline.Seconds(time) : ReplayTimeline.Format(time);

        private void TogglePlay()
        {
            if (playTimer.Enabled) Pause();
            else Play();
        }

        internal void Play()
        {
            if (recording is null) return;
            if (replayTime >= recording.Duration) SeekReplay(0);
            playTimer.Start();
            RestartPlayClock();
            playButton.Text = "Pause";
            GuiIcons.ApplyToButton(playButton, "");
        }

        internal void Pause()
        {
            playTimer.Stop();
            playClock.Reset();
            playButton.Text = "Play";
            GuiIcons.ApplyToButton(playButton, GuiIcons.Play);
        }

        // Playback runs from where the clock was last restarted, so a seek or speed change mid-play takes effect at once.
        private void RestartPlayClock()
        {
            if (!playTimer.Enabled) return;
            playFrom = replayTime;
            playClock.Restart();
        }

        private void OnPlayTick()
        {
            if (recording is null)
            {
                Pause();
                return;
            }
            double t = playFrom + playClock.Elapsed.TotalSeconds * replaySpeed;
            SeekReplay(t);
            if (t >= recording.Duration) Pause();
        }

        /// <summary>Shades the shown table by the time the recording spent at each cell (in the selected range), when asked.</summary>
        private async Task UpdateActivityAsync()
        {
            int generation = ++activityGeneration;
            grid?.SetActivity(null);
            activityLabel.Text = "";
            if (!heatBox.Checked || recording is not { } rec || replayStart is not { } start || image is null || a2l is null ||
                current is not { Kind: A2lItemKind.Map or A2lItemKind.Curve } item)
                return;
            var inputs = TableInputs.For(a2l.Module, item);
            var (from, to) = timeline.Range ?? (0, rec.Duration);
            var build = image.Build;
            activityLabel.Text = "Counting…";
            (CellActivity? Activity, string? Problem) result;
            try
            {
                result = await Task.Run(() => (CellActivity.Compute(rec, inputs, start, build, from, to, out var problem), problem));
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
            {
                result = (null, ex.Message);
            }
            if (IsGone || generation != activityGeneration || !ReferenceEquals(current, item)) return;
            if (result.Activity is not { } activity)
            {
                activityLabel.Text = $"No cells to shade: {result.Problem}.";
                return;
            }
            grid?.SetActivity(activity);
            activityLabel.Text = $"{ReplayTimeline.Seconds(activity.Covered)} with inputs of {ReplayTimeline.Seconds(to - from)}" +
                (timeline.Range is null ? "" : " selected");
            toolTip.SetToolTip(activityLabel, "Hover a cell for its time. Shift+drag on the timeline to count part of the recording.");
        }
    }
}
