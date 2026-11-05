using LotusECMLogger.Models;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>How a recording ended: where its files are, and anything that could not be saved.</summary>
    public sealed record TuningRecordingResult(RecordingFiles Files, int Edits, long DroppedFrames, string? CalibrationEndProblem);

    /// <summary>
    /// Records a live tuning session (step 1 of recording and replay; see CCP-client-plan.md): a high-speed log of every
    /// variable an A2L axis reads (so any table can be replayed) plus the channels the user picks, every change to the
    /// ECU's calibration RAM, and the calibration when recording starts and stops. While it runs it is the live cursor's
    /// value source, so switching tables reconfigures no DAQ and costs the log nothing.
    /// <para>
    /// It runs its own <see cref="HighSpeedLogService"/> on the shared session (DAQ owner <see cref="OwnerName"/>), so the
    /// High-Speed Log tab can log at the same time if the ECU has DAQ lists for both.
    /// </para>
    /// </summary>
    public sealed class TuningRecorder : IRawValueSource
    {
        public const string OwnerName = "Tuning recording";
        /// <summary>The axis inputs' rate: the live cursor's, and one DAQ list holds every input of either build.</summary>
        public const int InputRateHz = 20;

        private readonly HighSpeedLogService _logger;
        private readonly CalibrationEditLog _edits;
        private readonly A2lResolvedModule _a2l;
        private readonly object _lock = new();
        private LiveTuningSession? _session;

        private TuningRecorder(RecordingFiles files, CcpBuildProfile build, A2lResolvedModule a2l, HighSpeedLogService logger,
            CalibrationEditLog edits, IReadOnlyList<HighSpeedChannel> channels, int inputs)
        {
            (Files, Build, _a2l, _logger, _edits, Channels, InputCount) = (files, build, a2l, logger, edits, channels, inputs);
            Started = DateTime.Now;
            _logger.ErrorOccurred += (_, message) => Failed?.Invoke(this, message);
        }

        public RecordingFiles Files { get; }
        public CcpBuildProfile Build { get; }
        public IReadOnlyList<HighSpeedChannel> Channels { get; }
        /// <summary>How many of <see cref="Channels"/> are axis inputs (the rest are the user's).</summary>
        public int InputCount { get; }
        public DateTime Started { get; }
        public int EditCount => _edits.Count;
        public bool IsRecording => _logger.IsLogging;
        public long DroppedFrames => _logger.DroppedFrames;

        /// <summary>The log stopped by itself (the session closed or the transport failed). Raised on a worker thread.</summary>
        public event EventHandler<string>? Failed;

        public ulong? Bits(uint address, int size) => _logger.Bits(address, size);

        /// <summary>
        /// Saves the ECU's calibration, then starts the log. <paramref name="session"/>, when syncing, supplies the
        /// calibration without reading it (one checksum confirms the ECU still holds it), and its writes are recorded.
        /// </summary>
        public static async Task<TuningRecorder> StartAsync(CcpSessionHost host, CcpMaster master, A2lResolvedModule a2l,
            CcpBuildProfile build, IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> extraChannels, string logPath,
            LiveTuningSession? session, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(a2l);
            ArgumentNullException.ThrowIfNull(build);
            ArgumentNullException.ThrowIfNull(extraChannels);
            var files = new RecordingFiles(logPath);
            var inputs = InputChannels(a2l);
            if (inputs.Count == 0 && extraChannels.Count == 0)
                throw new InvalidOperationException("The A2L binds no axis inputs and no other channels were chosen, so there is nothing to record.");
            var selection = Merge(extraChannels, inputs);

            var start = await CalibrationSnapshotAsync(master, build, session, progress, cancellationToken).ConfigureAwait(false);
            LoggerPaths.EnsureParentDirectory(files.Log);
            await File.WriteAllBytesAsync(files.CalibrationStart, start, cancellationToken).ConfigureAwait(false);

            var logger = new HighSpeedLogService(host, OwnerName);
            CalibrationEditLog? edits = null;
            try
            {
                logger.StartLogging(selection, files.Log, notes:
                [
                    $"build: {build.Build}",
                    $"calibration at start: {Path.GetFileName(files.CalibrationStart)}; calibration writes: {Path.GetFileName(files.Edits)}",
                ]);
                edits = CalibrationEditLog.Create(files.Edits, Path.GetFileName(files.Log));
            }
            catch
            {
                logger.Dispose();
                edits?.Dispose();
                // Nothing was recorded: leave no calibration file without its log.
                try { File.Delete(files.CalibrationStart); } catch (IOException) { }
                throw;
            }
            var recorder = new TuningRecorder(files, build, a2l, logger, edits, selection.Select(s => s.Channel).ToList(),
                selection.Count - extraChannels.Count);
            recorder.Attach(session);
            return recorder;
        }

        /// <summary>Records <paramref name="session"/>'s writes from now on (null: none), e.g. when live sync starts or stops.</summary>
        public void Attach(LiveTuningSession? session)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_session, session)) return;
                if (_session is not null) _session.Written -= OnWritten;
                _session = session;
                if (session is not null) session.Written += OnWritten;
            }
        }

        private void OnWritten(object? sender, CalibrationWrittenEventArgs e) =>
            _edits.Append(e, ItemsAt(_a2l, e.Address, e.After.Length), _logger.LogTime());

        /// <summary>
        /// Stops the log and saves the calibration the ECU then holds, if <paramref name="master"/> is still connected.
        /// A calibration that cannot be saved is reported in the result, not thrown: the log and writes are kept.
        /// </summary>
        public async Task<TuningRecordingResult> StopAsync(CcpMaster? master, IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LiveTuningSession? session;
            lock (_lock) session = _session;
            Attach(null);
            await Task.Run(_logger.StopLogging, CancellationToken.None).ConfigureAwait(false);
            long dropped = _logger.DroppedFrames;
            _logger.Dispose();
            _edits.Dispose();
            string? problem = null;
            if (master is not { State: CcpSessionState.Connected })
                problem = "The CCP session was closed, so the calibration at the end was not read.";
            else
            {
                try
                {
                    var end = await CalibrationSnapshotAsync(master, Build, session, progress, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(Files.CalibrationEnd, end, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    problem = $"The calibration at the end was not saved: {ex.Message}";
                }
            }
            return new TuningRecordingResult(Files, _edits.Count, dropped, problem);
        }

        /// <summary>
        /// The ECU's calibration RAM: live sync's copy when one checksum shows the ECU still holds it, otherwise read.
        /// A read whose calibration ID is not <paramref name="build"/>'s is refused.
        /// </summary>
        public static async Task<byte[]> CalibrationSnapshotAsync(CcpMaster master, CcpBuildProfile build, LiveTuningSession? session,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(build);
            int size = (int)build.CalibrationSize;
            if (session?.EcuBytes() is { } known && known.Length == size && session.Image.Build == build)
            {
                ushort now = await master.ChecksumAsync(build.CalibrationBase, size, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (now == CcpFrame.AdditiveChecksum(known)) return known;
            }
            var read = await master.ReadAsync(build.CalibrationBase, size, progress, cancellationToken).ConfigureAwait(false);
            if (CcpCalibrationWriter.CalibrationId(read) is var id && id != build.Build)
                throw new InvalidDataException($"The ECU's calibration is {id ?? "unreadable"}, not {build.Build}.");
            return read;
        }

        /// <summary>
        /// The variables the A2L's axes read, as log channels at <see cref="InputRateHz"/>: each scaled as its A2L conversion
        /// says when that is linear (so the CSV reads in physical units), raw otherwise.
        /// </summary>
        public static IReadOnlyList<HighSpeedChannel> InputChannels(A2lResolvedModule a2l)
        {
            ArgumentNullException.ThrowIfNull(a2l);
            return a2l.Items.Values
                .Select(item => TableInputs.For(a2l.Module, item))
                .SelectMany(inputs => inputs.Sources)
                .DistinctBy(m => (m.Address, m.DataType.Size()))
                .OrderBy(m => m.Address)
                .Select(m => ToChannel(a2l.Module, m))
                .ToList();
        }

        private static HighSpeedChannel ToChannel(A2lModule module, A2lMeasurement m)
        {
            var conversion = module.Compu(m.Conversion);
            var (scale, offset, unit) = Linear(conversion) is { } linear ? (linear.Scale, linear.Offset, conversion.Unit) : (1.0, 0.0, "raw");
            return new HighSpeedChannel
            {
                Name = m.Name,
                Address = m.Address!.Value,
                Size = (byte)m.DataType.Size(),
                Signed = m.DataType.IsSigned(),
                Scale = scale,
                Offset = offset,
                Unit = unit,
                DefaultRate = InputRateHz,
                DefaultSelected = true,
                SourceSymbol = m.Name,
            };
        }

        /// <summary>
        /// A channel from a log that does not give its scaling (one from before the <c>channel:</c> preamble lines), with
        /// the scaling of the A2L measurement at the same address and size; null when there is none, or it is not linear.
        /// Such logs were scaled from the same catalog types, but a preset could override them, so this is a best guess.
        /// </summary>
        public static LoggedChannel? Complete(A2lModule module, LoggedChannel channel)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(channel);
            if (channel.IsExact) return channel;
            var measurement = module.Measurements.Values.FirstOrDefault(m =>
                m.Address == channel.Address && m.DataType.Size() == channel.Size && !m.IsVirtual);
            if (measurement is null || Linear(module.Compu(measurement.Conversion)) is not { } linear) return null;
            return channel with { Signed = measurement.DataType.IsSigned(), Scale = linear.Scale, Offset = linear.Offset };
        }

        /// <summary>physical = raw × Scale + Offset, when the conversion is that; else null.</summary>
        private static (double Scale, double Offset)? Linear(A2lCompuMethod conversion)
        {
            if (conversion.Problem is not null || conversion.Kind is A2lConversionKind.VerbalTable or A2lConversionKind.Formula or A2lConversionKind.Unsupported)
                return null;
            double offset = conversion.ToPhysical(0), scale = conversion.ToPhysical(1) - offset;
            double far = conversion.ToPhysical(1000);
            bool linear = double.IsFinite(scale) && scale != 0 && Math.Abs(far - (offset + 1000 * scale)) <= 1e-9 * Math.Max(1, Math.Abs(far));
            return linear ? (scale, offset) : null;
        }

        /// <summary>
        /// The user's channels first, then each input not already logged at the same address and size. An input whose name
        /// a user channel uses for another address is logged as <c>name@0xADDRESS</c>, so the two columns stay apart.
        /// </summary>
        private static List<(HighSpeedChannel Channel, int RateHz)> Merge(IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> extra,
            IReadOnlyList<HighSpeedChannel> inputs)
        {
            var selection = extra.ToList();
            var logged = extra.Select(e => (e.Channel.Address, (int)e.Channel.Size)).ToHashSet();
            var names = extra.Select(e => e.Channel.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var input in inputs)
            {
                if (!logged.Add((input.Address, input.Size))) continue;
                var channel = names.Add(input.Name) ? input : new HighSpeedChannel
                {
                    Name = $"{input.Name}@0x{input.Address:X8}", Address = input.Address, Size = input.Size, Signed = input.Signed,
                    Scale = input.Scale, Offset = input.Offset, Unit = input.Unit, DefaultRate = input.DefaultRate,
                    DefaultSelected = true, SourceSymbol = input.SourceSymbol,
                };
                selection.Add((channel, InputRateHz));
            }
            return selection;
        }

        /// <summary>The items a write touched, for the edits file: cells by position, up to a few per item.</summary>
        public static string ItemsAt(A2lResolvedModule a2l, uint address, int length)
        {
            ArgumentNullException.ThrowIfNull(a2l);
            ulong end = (ulong)address + (uint)length;
            bool Overlaps(uint start, int size) => start < end && (ulong)start + (uint)size > address;
            var parts = new List<string>();
            foreach (var item in a2l.Items.Values.OrderBy(i => i.Address))
            {
                if (!item.Spans().Any(s => Overlaps(s.Address, s.Length))) continue;
                var cells = new List<string>();
                if (item.Kind != A2lItemKind.Ascii)
                {
                    for (int y = 0; y < item.CountY; y++)
                        for (int x = 0; x < item.CountX; x++)
                            if (Overlaps(item.CellAddress(x, y), item.ElementSize))
                                cells.Add(item.Kind == A2lItemKind.Map ? $"[{x},{y}]" : item.ElementCount > 1 ? $"[{x}]" : "");
                }
                parts.Add(cells.Count switch
                {
                    0 => item.Name,
                    <= 4 => item.Name + string.Concat(cells),
                    _ => $"{item.Name} ({cells.Count} cells)",
                });
            }
            return string.Join("; ", parts);
        }
    }
}
