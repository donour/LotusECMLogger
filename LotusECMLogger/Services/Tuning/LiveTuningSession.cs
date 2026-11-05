using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Tuning
{
    public enum LiveTuningMode
    {
        /// <summary>Each edit, undo and redo is written to the ECU as soon as it is made.</summary>
        Live,
        /// <summary>Edits are staged on the PC until <see cref="LiveTuningSession.ApplyAsync"/>.</summary>
        Hold,
    }

    public enum LiveTuningStatus
    {
        /// <summary>The file and the ECU differ: send the file, take the ECU's values, or close.</summary>
        Comparing,
        /// <summary>The ECU holds the file's values except for pending edits.</summary>
        Syncing,
        /// <summary>A write failed or the ECU changed outside this session: nothing is written until a refresh.</summary>
        Stopped,
        Closed,
    }

    /// <summary>How a byte range stands against the ECU; the worst byte decides.</summary>
    public enum CalibrationSyncState { InSync, Pending, Writing, Failed }

    /// <summary>
    /// A change to the ECU's calibration RAM: a run this session wrote and read back, or (<see cref="Found"/>) a run a
    /// refresh found different from what the session last knew, e.g. after a reboot restored the flash calibration.
    /// </summary>
    public sealed class CalibrationWrittenEventArgs(uint address, byte[] before, byte[] after, DateTime at, bool found) : EventArgs
    {
        public uint Address { get; } = address;
        public byte[] Before { get; } = before;
        public byte[] After { get; } = after;
        public DateTime At { get; } = at;
        public bool Found { get; } = found;
    }

    public sealed class LiveTuningOptions
    {
        /// <summary>Where the ECU's calibration is saved when the session starts; null saves nothing.</summary>
        public string? BackupDirectory { get; init; }
        /// <summary>The build's engine speed, so sending a whole file can be refused while the engine turns.</summary>
        public CcpEngineSpeedSymbol? EngineSpeed { get; init; }
        public LiveTuningMode Mode { get; init; } = LiveTuningMode.Live;
    }

    /// <summary>
    /// Keeps ECU calibration RAM in step with a <see cref="CalibrationImage"/> over the shared <see cref="CcpMaster"/>
    /// (Phase 6c). It starts by reading the whole CAL region and comparing it with the file; the user then sends the
    /// file, takes the ECU's values, or closes. While syncing, every change to the image (edit, undo, redo) is written
    /// as runs of changed bytes with read-back, at once in <see cref="LiveTuningMode.Live"/> or on Apply in
    /// <see cref="LiveTuningMode.Hold"/>. Each batch first checks the ECU still holds what this session last wrote
    /// (one checksum), so a reboot or another tool's write stops sync instead of being overwritten blindly.
    /// A failed write stops sync and marks its bytes failed until <see cref="RefreshAsync"/> reads the ECU again.
    /// The CCP enable byte is never written: the ECU's value always wins there.
    /// <para>
    /// Single edits are written while the engine runs, which is the point of live tuning. Writes are not atomic: a
    /// lookup can see a table part-way through a multi-run write, so sending a whole file needs the engine off.
    /// <see cref="SyncChanged"/> may be raised on any thread. The image is only read on the thread that edits it.
    /// </para>
    /// </summary>
    public sealed class LiveTuningSession : IAsyncDisposable
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _io = new(1, 1);
        private readonly CcpMemoryRange _range;
        private readonly int? _enableOffset;
        private readonly CcpEngineSpeedSymbol? _engineSpeed;
        private readonly byte[] _ecu;       // what the ECU holds, as last read or written by this session
        private readonly byte[] _target;    // the image's bytes, copied on its editing thread
        private readonly bool[] _failed;
        private (int Offset, int Length)? _writing;
        private LiveTuningStatus _status;
        private string? _stopReason;
        private LiveTuningMode _mode;
        private Task _flush = Task.CompletedTask;

        public CcpMaster Master { get; }
        public CalibrationImage Image { get; }
        public string? BackupPath { get; }

        /// <summary>Raised when the status, mode or any byte's sync state changes. May be raised on any thread.</summary>
        public event EventHandler? SyncChanged;

        /// <summary>
        /// Raised after each verified write to the ECU, and for each run a refresh finds changed, so a recording can say
        /// which calibration was in the ECU when. May be raised on any thread.
        /// </summary>
        public event EventHandler<CalibrationWrittenEventArgs>? Written;

        private LiveTuningSession(CcpMaster master, CalibrationImage image, byte[] ecu, string? backupPath, LiveTuningOptions options)
        {
            (Master, Image, _ecu, BackupPath, _engineSpeed, _mode) = (master, image, ecu, backupPath, options.EngineSpeed, options.Mode);
            _range = CcpMemoryRange.Calibration(image.Build);
            if (image.Build.EnableByteAddress is uint enable && _range.Contains(enable, 1)) _enableOffset = (int)(enable - image.BaseAddress);
            _target = image.Bytes(image.BaseAddress, image.Size).ToArray();
            _failed = new bool[image.Size];
            _status = FirstDifference() < 0 ? LiveTuningStatus.Syncing : LiveTuningStatus.Comparing;
            image.Changed += OnImageChanged;
            master.StateChanged += OnMasterStateChanged;
        }

        /// <summary>Reads the ECU's calibration (saving it as a backup when asked) and compares it with the image.</summary>
        public static async Task<LiveTuningSession> StartAsync(CcpMaster master, CalibrationImage image, LiveTuningOptions? options = null,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(image);
            var settings = options ?? new LiveTuningOptions();
            if (image.Build.Target != master.Target)
                throw new ArgumentException($"{image.Build.Build} is a {image.Build.Target.Name} build; the session is {master.Target.Name}.", nameof(image));
            var ecu = await master.ReadAsync(image.BaseAddress, image.Size, progress, cancellationToken).ConfigureAwait(false);
            string? id = CcpCalibrationWriter.CalibrationId(ecu);
            if (id != image.Build.Build)
                throw new InvalidDataException($"The ECU's calibration is {id ?? "unreadable"}; the file is {image.Build.Build}.");
            string? backup = settings.BackupDirectory is { } directory
                ? CcpCalibrationWriter.SaveBackup(directory, image.Build, image.BaseAddress, ecu) : null;
            return new LiveTuningSession(master, image, ecu, backup, settings);
        }

        public LiveTuningStatus Status { get { lock (_lock) return _status; } }
        public string? StopReason { get { lock (_lock) return _stopReason; } }

        /// <summary>
        /// Live or Hold. Switching to Live does not send edits held so far; <see cref="ApplyAsync"/> does, and the next
        /// change sends them along with itself.
        /// </summary>
        public LiveTuningMode Mode
        {
            get { lock (_lock) return _mode; }
            set
            {
                lock (_lock)
                {
                    if (_mode == value) return;
                    _mode = value;
                }
                RaiseSyncChanged();
            }
        }

        /// <summary>Runs where the image and the ECU differ (the enable byte excluded): what Apply or Send would write.</summary>
        public IReadOnlyList<CalibrationSpan> Differences
        {
            get { lock (_lock) return DifferingRuns(0, _target.Length).Select(r => Span(r.Offset, r.Length)).ToList(); }
        }

        public int PendingBytes { get { lock (_lock) return DifferingRuns(0, _target.Length).Sum(r => r.Length); } }

        /// <summary>Whether the ECU's enable byte differs from the file's; the ECU's value is kept.</summary>
        public bool EnableByteDiffers
        {
            get { lock (_lock) return _enableOffset is int e && _ecu[e] != _target[e]; }
        }

        public CalibrationSyncState StateOf(uint address, int length)
        {
            if (!_range.Contains(address, length)) throw new ArgumentOutOfRangeException(nameof(address));
            int offset = (int)(address - Image.BaseAddress);
            lock (_lock)
            {
                var state = CalibrationSyncState.InSync;
                for (int i = offset; i < offset + length; i++)
                {
                    if (_failed[i]) return CalibrationSyncState.Failed;
                    if (_writing is { } w && i >= w.Offset && i < w.Offset + w.Length) state = CalibrationSyncState.Writing;
                    else if (state == CalibrationSyncState.InSync && Differs(i)) state = CalibrationSyncState.Pending;
                }
                return state;
            }
        }

        public CalibrationSyncState StateOf(A2lCalibrationItem item, int x, int y = 0) =>
            StateOf(item.CellAddress(x, y), item.ElementSize);

        /// <summary>
        /// A copy of the ECU's calibration as this session knows it (what it last read or wrote), or null while that is
        /// uncertain: sync stopped or closed, a write in flight, or a byte whose write failed.
        /// </summary>
        public byte[]? EcuBytes()
        {
            lock (_lock)
            {
                if (_status is not (LiveTuningStatus.Comparing or LiveTuningStatus.Syncing) || _writing is not null || _failed.Contains(true))
                    return null;
                return (byte[])_ecu.Clone();
            }
        }

        // ── Resolving the first comparison ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes every difference to the ECU (the whole file, in effect). Refused while the engine turns, because a
        /// multi-run write is not atomic. Afterwards the session is syncing.
        /// </summary>
        public async Task SendImageAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            RequireStatus(LiveTuningStatus.Comparing, LiveTuningStatus.Syncing);
            if (_engineSpeed is { } symbol &&
                await CcpCalibrationWriter.ReadEngineSpeedAsync(Master, symbol, cancellationToken).ConfigureAwait(false) is var rpm and > 0)
                throw new InvalidOperationException($"The engine is turning ({rpm:0} rpm). Sending the whole file needs the engine off.");
            await WriteDifferencesAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Copies the ECU's values into the image as one undo step (the enable byte excluded); then syncing.</summary>
        public void TakeEcuValues()
        {
            RequireStatus(LiveTuningStatus.Comparing, LiveTuningStatus.Syncing);
            List<(uint, byte[])> writes;
            lock (_lock)
            {
                writes = DifferingRuns(0, _target.Length).Select(r => (Address(r.Offset), _ecu.AsSpan(r.Offset, r.Length).ToArray())).ToList();
                _status = LiveTuningStatus.Syncing; // so the edit below is not mistaken for one to send
            }
            if (writes.Count > 0) Image.WriteBytes(writes, "Take the ECU's values");
            RaiseSyncChanged();
        }

        // ── Syncing ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Writes the pending edits. In Live mode this happens by itself after each change.</summary>
        public async Task ApplyAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            RequireStatus(LiveTuningStatus.Syncing);
            await WriteDifferencesAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Waits for any write started by a Live-mode edit.</summary>
        public Task WhenIdleAsync()
        {
            lock (_lock) return _flush;
        }

        /// <summary>Reads the whole region again, clears failed bytes and compares afresh (status Comparing or Syncing).</summary>
        public async Task RefreshAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            RequireStatus(LiveTuningStatus.Comparing, LiveTuningStatus.Syncing, LiveTuningStatus.Stopped);
            await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ecu = await Master.ReadAsync(Image.BaseAddress, Image.Size, progress, cancellationToken).ConfigureAwait(false);
                if (CcpCalibrationWriter.CalibrationId(ecu) != Image.Build.Build)
                    throw new InvalidDataException($"The ECU's calibration is now {CcpCalibrationWriter.CalibrationId(ecu) ?? "unreadable"}.");
                var at = DateTime.Now;
                List<CalibrationWrittenEventArgs> found;
                lock (_lock)
                {
                    found = CcpCalibrationWriter.ChangedRuns(Image.BaseAddress, _ecu, ecu)
                        .Select(run => new CalibrationWrittenEventArgs(run.Address, Copy(_ecu, run.Address, run.Length),
                            Copy(ecu, run.Address, run.Length), at, found: true))
                        .ToList();
                    ecu.CopyTo(_ecu, 0);
                    Array.Clear(_failed);
                    _stopReason = null;
                    _status = FirstDifference() < 0 ? LiveTuningStatus.Syncing : LiveTuningStatus.Comparing;
                }
                foreach (var change in found) Written?.Invoke(this, change);
            }
            finally { _io.Release(); }
            RaiseSyncChanged();
        }

        private byte[] Copy(byte[] source, uint address, int length) => source.AsSpan((int)(address - Image.BaseAddress), length).ToArray();

        private async Task WriteDifferencesAsync(IProgress<int>? progress, CancellationToken cancellationToken)
        {
            await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] target, known;
                lock (_lock)
                {
                    if (_status is not (LiveTuningStatus.Comparing or LiveTuningStatus.Syncing))
                        throw new InvalidOperationException($"Live sync is {_status.ToString().ToLowerInvariant()}: {_stopReason}");
                    (target, known) = ((byte[])_target.Clone(), (byte[])_ecu.Clone());
                }
                if (_enableOffset is int e) target[e] = known[e];
                var runs = CcpCalibrationWriter.ChangedRuns(Image.BaseAddress, known, target);
                if (runs.Count > 0)
                {
                    ushort now = await Master.ChecksumAsync(Image.BaseAddress, Image.Size, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (now != CcpFrame.AdditiveChecksum(known))
                    {
                        Stop("The ECU's calibration changed outside this session (another write, or a reboot restoring it from " +
                            "flash). Refresh to compare again.");
                        throw new InvalidOperationException(StopReason);
                    }
                }
                int written = 0;
                foreach (var run in runs)
                {
                    int offset = (int)(run.Address - Image.BaseAddress);
                    var bytes = target.AsMemory(offset, run.Length);
                    lock (_lock) _writing = (offset, run.Length);
                    RaiseSyncChanged();
                    try
                    {
                        await Master.WriteVerifiedAsync(run.Address, bytes, _range, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lock (_lock)
                        {
                            _writing = null;
                            Array.Fill(_failed, true, offset, run.Length); // the ECU's bytes there are now unknown
                        }
                        Stop($"Writing 0x{run.Address:X8}+{run.Length} failed: {ex.Message} Refresh to read the ECU again.");
                        throw;
                    }
                    byte[] before;
                    lock (_lock)
                    {
                        before = _ecu.AsSpan(offset, run.Length).ToArray();
                        bytes.Span.CopyTo(_ecu.AsSpan(offset));
                        _writing = null;
                    }
                    Written?.Invoke(this, new CalibrationWrittenEventArgs(run.Address, before, bytes.ToArray(), DateTime.Now, found: false));
                    written += run.Length;
                    progress?.Report(written);
                    RaiseSyncChanged();
                }
                lock (_lock)
                {
                    if (_status == LiveTuningStatus.Comparing) _status = LiveTuningStatus.Syncing;
                }
            }
            finally { _io.Release(); }
            RaiseSyncChanged();
        }

        private void OnImageChanged(object? sender, CalibrationChangedEventArgs e)
        {
            bool flush;
            lock (_lock)
            {
                foreach (var span in e.Spans)
                    Image.Bytes(span.Address, span.Length).CopyTo(_target.AsSpan((int)(span.Address - Image.BaseAddress)));
                flush = _status == LiveTuningStatus.Syncing && _mode == LiveTuningMode.Live;
                if (flush) _flush = FlushAsync(_flush);
            }
            RaiseSyncChanged();
        }

        // Live-mode writes run one after another; a failure is recorded as the stop reason, never thrown unobserved.
        private async Task FlushAsync(Task previous)
        {
            await previous.ConfigureAwait(false);
            await Task.Yield();
            try
            {
                if (Status == LiveTuningStatus.Syncing) await WriteDifferencesAsync(null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) // nobody awaits this task except WhenIdleAsync, so a failure becomes the stop reason
            {
                if (Status != LiveTuningStatus.Stopped && Status != LiveTuningStatus.Closed) Stop(ex.Message);
            }
        }

        private void OnMasterStateChanged(object? sender, CcpSessionState state)
        {
            if (state != CcpSessionState.Connected && Status is LiveTuningStatus.Comparing or LiveTuningStatus.Syncing)
                Stop($"The CCP session is {state.ToString().ToLowerInvariant()}{(Master.StateReason is { } r ? $": {r}" : "")}. " +
                    "Reconnect, then refresh.");
        }

        private void Stop(string reason)
        {
            lock (_lock)
            {
                if (_status == LiveTuningStatus.Closed) return;
                (_status, _stopReason) = (LiveTuningStatus.Stopped, reason);
            }
            RaiseSyncChanged();
        }

        private void RequireStatus(params LiveTuningStatus[] allowed)
        {
            var status = Status;
            if (!allowed.Contains(status))
                throw new InvalidOperationException(status == LiveTuningStatus.Stopped
                    ? $"Live sync stopped: {StopReason}" : $"Live sync is {status.ToString().ToLowerInvariant()}.");
        }

        private bool Differs(int i) => i != _enableOffset && _target[i] != _ecu[i];

        private int FirstDifference()
        {
            for (int i = 0; i < _target.Length; i++) if (Differs(i)) return i;
            return -1;
        }

        private IEnumerable<(int Offset, int Length)> DifferingRuns(int start, int end)
        {
            for (int i = start; i < end;)
            {
                if (!Differs(i)) { i++; continue; }
                int run = i;
                while (i < end && Differs(i)) i++;
                yield return (run, i - run);
            }
        }

        private uint Address(int offset) => Image.BaseAddress + (uint)offset;

        private CalibrationSpan Span(int offset, int length) => new(Address(offset), length);

        private void RaiseSyncChanged() => SyncChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Stops syncing and waits for an in-flight write; the ECU keeps whatever was written.</summary>
        public async ValueTask DisposeAsync()
        {
            Task flush;
            lock (_lock)
            {
                if (_status == LiveTuningStatus.Closed) return;
                _status = LiveTuningStatus.Closed;
                flush = _flush;
            }
            Image.Changed -= OnImageChanged;
            Master.StateChanged -= OnMasterStateChanged;
            await flush.ConfigureAwait(false);
            await _io.WaitAsync().ConfigureAwait(false);
            _io.Release();
            RaiseSyncChanged();
        }
    }
}
