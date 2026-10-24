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
    /// <summary>The Tuning page's live sync: start, stop, send the file, take the ECU's values.</summary>
    internal sealed partial class CcpTuningPanel
    {
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

        private CalibrationSyncState? SyncState(uint address, int length) => session?.StateOf(address, length);
    }
}
