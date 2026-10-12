using System.Diagnostics;
using System.Threading.Channels;
using LotusECMLogger.Models;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// The T6 high-speed CAN channel logger: a CCP DAQ consumer on <see cref="CcpMaster"/>.
    ///
    /// Each session opens a transport, CONNECTs, programs the planned groups as DAQ lists (group = list,
    /// frame = ODT, slot = event channel, divider = prescaler, label = PID) and streams them to a CSV file
    /// and to <see cref="DataReceived"/>. DAQ configuration is RAM-only on the ECU and wiped on every boot,
    /// so the full sequence runs on each start. The optional AEM wideband is a separate
    /// <see cref="AemWidebandSource"/> on the same session; its values are merged as extra columns.
    ///
    /// Each session (and each Identify) holds a lease on the shared <see cref="CcpSessionHost"/>, so the logger
    /// shares the adapter and CCP session with the CCP tab. Stopping stops only the logger's DAQ lists; the host
    /// ends the CCP session when its last lease is released.
    /// </summary>
    public sealed partial class HighSpeedLogService : IHighSpeedLogService
    {
        /// <summary>Log column carrying the frame's PID, ahead of the channel columns.</summary>
        private const string LabelColumn = "Label";

        // Hand-off buffer between the session's streams and the CSV writer. Bounded so a disk stall can't
        // grow memory without limit; beyond this frames are dropped (counted) rather than stalling the
        // CCP receive loop, which would only push the overflow back onto the adapter.
        private const int MaxQueuedFrames = 1024;
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        private readonly CcpSessionHost _host;
        private readonly bool _ownsHost;
        private readonly object _lock = new();
        private Session? _session;
        private bool _isLogging;
        private long _droppedFrames;

        /// <param name="host">The shared CCP session; not disposed by this service.</param>
        public HighSpeedLogService(CcpSessionHost host) : this(host, ownsHost: false) { }

        private HighSpeedLogService(CcpSessionHost host, bool ownsHost)
        {
            ArgumentNullException.ThrowIfNull(host);
            (_host, _ownsHost) = (host, ownsHost);
        }

        public event EventHandler<HighSpeedSampleEventArgs>? DataReceived;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsLogging
        {
            get { lock (_lock) { return _isLogging; } }
        }

        /// <summary>
        /// Frames dropped because a consumer fell behind (CSV writer or a full session stream), e.g. a
        /// sustained disk stall. 0 in normal operation.
        /// </summary>
        public long DroppedFrames
        {
            get
            {
                Session? session;
                lock (_lock) session = _session;
                return Interlocked.Read(ref _droppedFrames) + (session?.Daq?.Stream.Dropped ?? 0) + (session?.Aem?.Dropped ?? 0);
            }
        }

        /// <summary>Everything one logging session owns.</summary>
        private sealed class Session(LoggingPlan plan, ISampleSink sink, bool pollAem)
        {
            public LoggingPlan Plan { get; } = plan;
            public ISampleSink Sink { get; } = sink;
            public bool PollAem { get; } = pollAem;
            public CancellationTokenSource Cancel { get; } = new();
            public Channel<WorkItem> Queue { get; } = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(MaxQueuedFrames)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails (counted) instead of evicting silently
            });
            public volatile CcpSessionLease? Lease;
            public volatile CcpDaqSubscription? Daq;
            public volatile AemWidebandSource? Aem;
            public Task Worker { get; set; } = Task.CompletedTask;
        }

        /// <summary>A DAQ sample or an AEM reading, in arrival order, for the single writer.</summary>
        private readonly record struct WorkItem(CcpDaqSample? Daq, AemWidebandSample? Aem);

        public void StartLogging(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> channels, string csvFilePath,
            bool pollAemWideband = false)
        {
            lock (_lock)
            {
                if (_isLogging || _session is not null)
                    throw new InvalidOperationException("High-speed logging is already active. Stop the current session first.");

                if (string.IsNullOrWhiteSpace(csvFilePath))
                    throw new ArgumentException("CSV file path cannot be empty", nameof(csvFilePath));

                // Build (and validate) the ECU channel program before touching hardware.
                var plan = HighSpeedLogPlanner.Plan(channels, CcpTarget.T6);

                // Log columns = the frame label, then the selected channels in selection order
                // (de-duplicated by name).
                List<string> columnNames = [LabelColumn, .. channels.Select(c => c.channel.Name).Distinct()];
                if (pollAemWideband)
                {
                    columnNames.Add(IHighSpeedLogService.AemLambdaChannelName);
                    columnNames.Add(IHighSpeedLogService.AemAfrChannelName);
                }

                ISampleSink? sink = null;
                try
                {
                    sink = CreateSink(csvFilePath, channels, columnNames, pollAemWideband);
                    var session = new Session(plan, sink, pollAemWideband);
                    Interlocked.Exchange(ref _droppedFrames, 0);
                    _session = session;
                    _isLogging = true;
                    session.Worker = Task.Run(() => RunAsync(session));
                    Debug.WriteLine($"HighSpeedLog: started, {plan.Groups.Count} group(s), {columnNames.Count} column(s)");
                }
                catch (Exception ex)
                {
                    _isLogging = false;
                    _session = null;
                    sink?.Dispose();
                    throw new InvalidOperationException($"Failed to start high-speed logging: {ex.Message}", ex);
                }
            }
        }

        public void StopLogging()
        {
            Session? session;
            lock (_lock)
            {
                session = _session;
                _isLogging = false;
                _session = null;
            }
            if (session is null) return;
            // The master never resumes on a captured context, so blocking here (often the UI thread) is safe.
            Task.Run(() => StopAsync(session)).GetAwaiter().GetResult();
            Debug.WriteLine("HighSpeedLog: stopped");
        }

        public void Dispose()
        {
            StopLogging();
            if (_ownsHost) _host.Dispose();
        }

        /// <summary>
        /// Stops only this session's DAQ lists, lets the writer drain the backlog, then releases the lease (the
        /// host ends the CCP session if no one else holds it). Best-effort: a silent ECU must not prevent the CSV
        /// from being closed.
        /// </summary>
        private async Task StopAsync(Session session)
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            session.Aem?.Dispose();
            try
            {
                if (session.Daq is { IsActive: true } daq) await daq.StopAsync(timeout.Token).ConfigureAwait(false);
                else session.Cancel.Cancel(); // still configuring: abandon it (the session becomes uncertain)
            }
            catch (Exception ex) { Debug.WriteLine($"HighSpeedLog: DAQ stop failed: {ex.Message}"); }

            // The streams have ended; the writer drains what is queued and exits.
            try { await session.Worker.WaitAsync(StopTimeout).ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"HighSpeedLog: writer did not finish: {ex.Message}"); }
            session.Queue.Writer.TryComplete();
            session.Cancel.Cancel();
            if (session.Lease is { } lease) await lease.DisposeAsync().ConfigureAwait(false);
            // Keep the session's drop count visible after it ends, as the old logger did.
            Interlocked.Add(ref _droppedFrames, (session.Daq?.Stream.Dropped ?? 0) + (session.Aem?.Dropped ?? 0));
            try { session.Sink.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"HighSpeedLog: closing the log failed: {ex.Message}"); }
        }

        /// <summary>Configures the ECU, then pumps DAQ and AEM into the queue while one loop writes the CSV.</summary>
        private async Task RunAsync(Session session)
        {
            var ct = session.Cancel.Token;
            try
            {
                session.Lease = await _host.AcquireAsync(ct).ConfigureAwait(false);
                var master = session.Lease.Master;
                session.Daq = await master.StartDaqAsync(ToDaqLists(session.Plan), ct).ConfigureAwait(false);
                if (session.PollAem) session.Aem = new AemWidebandSource(master);

                var pumps = new List<Task> { PumpAsync(session.Daq.Stream.Reader, s => new WorkItem(s, null), session.Queue.Writer, ct) };
                if (session.Aem is { } aem) pumps.Add(PumpAsync(aem.Samples, s => new WorkItem(null, s), session.Queue.Writer, ct));
                var writer = WriteAsync(session);
                // The streams end when logging stops (or the transport faults, which surfaces here).
                try { await Task.WhenAll(pumps).ConfigureAwait(false); }
                finally { session.Queue.Writer.TryComplete(); }
                await writer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                session.Queue.Writer.TryComplete();
                Debug.WriteLine($"HighSpeedLog: session error: {ex}");
                bool current;
                lock (_lock) { current = _session == session; if (current) _isLogging = false; }
                if (current) ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        private async Task PumpAsync<T>(ChannelReader<T> source, Func<T, WorkItem> wrap, ChannelWriter<WorkItem> queue,
            CancellationToken ct)
        {
            await foreach (var item in source.ReadAllAsync(ct).ConfigureAwait(false))
                if (!queue.TryWrite(wrap(item)))
                    Interlocked.Increment(ref _droppedFrames);
        }

        /// <summary>Planner groups become DAQ lists, in order, so a fresh session allocates list n to group n.</summary>
        internal static IReadOnlyList<CcpDaqListRequest> ToDaqLists(LoggingPlan plan) => plan.Groups
            .Select(g => new CcpDaqListRequest(g.Slot, g.Divider, g.Frames
                .Select(f => (IReadOnlyList<CcpDaqElement>)f.Channels
                    .Select(c => new CcpDaqElement(c.Channel.Address, c.Channel.Size)).ToList())
                .ToList()))
            .ToList();

        // The single writer: decodes, writes the CSV and raises DataReceived, off the CCP receive loop.
        // Times come from the adapter's hardware timestamps (µs, shared by DAQ and AEM frames), anchored
        // to wall-clock at the first frame, so frames read together in one batch keep distinct, accurate times.
        private async Task WriteAsync(Session session)
        {
            var clock = new HardwareClock();
            await foreach (var item in session.Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (item.Daq is { } sample) WriteDaq(session, sample, clock);
                else if (item.Aem is { } aem) WriteAem(session, aem, clock);
            }
        }

        private void WriteDaq(Session session, in CcpDaqSample sample, HardwareClock clock)
        {
            var (timestamp, elapsedUs) = clock.At(sample.TimestampMicroseconds);
            var frame = session.Plan.Groups[sample.List].Frames[sample.Odt];
            byte label = (byte)(session.Daq!.FirstPids[sample.List] + sample.Odt);
            ReadOnlySpan<byte> payload = sample.Data.Span;

            var readings = new List<HighSpeedReading>(frame.Channels.Count);
            foreach (var planned in frame.Channels)
            {
                var ch = planned.Channel;
                if (planned.ByteOffset >= payload.Length) break;
                int len = Math.Min(ch.Size, payload.Length - planned.ByteOffset);
                double value = ch.Decode(payload.Slice(planned.ByteOffset, len));
                readings.Add(new HighSpeedReading { Name = ch.Name, Value = value, Unit = ch.Unit });
                session.Sink.Set(ch.Name, value);
            }

            // A frame carries only its own group's channels; the rest of the row is carried forward by the sink.
            session.Sink.Set(LabelColumn, label);
            session.Sink.WriteRow(timestamp, elapsedUs / 1000.0);

            DataReceived?.Invoke(this, new HighSpeedSampleEventArgs
            {
                Timestamp = timestamp,
                Label = label,
                Overflow = sample.Overrun,
                Readings = readings,
            });
        }

        // AEM values fold into the last-value-hold columns; the next stream row carries them. No CSV row of
        // its own: at ~20 Hz against a stream running hundreds of Hz, per-poll rows would fragment the log.
        private void WriteAem(Session session, in AemWidebandSample aem, HardwareClock clock)
        {
            var (timestamp, _) = clock.At(aem.TimestampMicroseconds);
            session.Sink.Set(IHighSpeedLogService.AemLambdaChannelName, aem.Lambda);
            session.Sink.Set(IHighSpeedLogService.AemAfrChannelName, aem.Afr);

            DataReceived?.Invoke(this, new HighSpeedSampleEventArgs
            {
                Timestamp = timestamp,
                Label = 0,
                Overflow = false,
                Readings =
                [
                    new HighSpeedReading { Name = IHighSpeedLogService.AemLambdaChannelName, Value = aem.Lambda, Unit = "λ" },
                    new HighSpeedReading { Name = IHighSpeedLogService.AemAfrChannelName, Value = aem.Afr, Unit = "AFR" },
                ],
            });
        }

        /// <summary>
        /// Unwraps the adapter's 32-bit µs counter (wraps every ~71.6 min) by signed difference, so a frame
        /// from the other stream that arrives slightly out of order is not mistaken for a rollover.
        /// </summary>
        internal sealed class HardwareClock
        {
            private DateTime _anchor;
            private bool _started;
            private uint _lastRaw;
            private long _lastElapsed;

            public (DateTime Timestamp, ulong ElapsedUs) At(uint raw)
            {
                if (!_started)
                {
                    (_anchor, _lastRaw, _started) = (DateTime.Now, raw, true);
                    return (_anchor, 0);
                }
                long elapsed = Math.Max(0, _lastElapsed + unchecked((int)(raw - _lastRaw)));
                if (elapsed > _lastElapsed) (_lastRaw, _lastElapsed) = (raw, elapsed);
                return (_anchor.AddTicks(elapsed * TimeSpan.TicksPerMicrosecond), (ulong)elapsed);
            }
        }

        private static ISampleSink CreateSink(string csvFilePath,
            IReadOnlyList<(HighSpeedChannel channel, int rateHz)> channels, IReadOnlyList<string> columns, bool pollAem)
        {
            List<string> notes = [];
            foreach (var (channel, rateHz) in channels)
                notes.Add($"{channel.Name}: 0x{channel.Address:X8} size={channel.Size} {rateHz}Hz unit={channel.Unit}");
            if (pollAem)
                notes.Add($"AEM UEGO: OBD Mode 01 PID 0x24 @ 0x{AemWidebandSource.RequestId:X3}, polled every " +
                    $"{AemWidebandSource.DefaultPollInterval.TotalMilliseconds:0}ms (last-value-hold columns)");

            // Every channel is a decoded measurement, so they are all plain decimal columns.
            return new CsvSampleSink(
                csvFilePath,
                new SampleLogHeader("Lotus High-Speed CAN Channel Log", notes),
                columns.Select(SampleColumn.Number));
        }

        /// <summary>
        /// Probes the ECU through a short lease on the shared session: CONNECT (with the CCP 2.1 check) unless
        /// already connected, EXCHANGE_ID with the ID string, and build identification from the calibration ID.
        /// </summary>
        public HighSpeedIdentifyResult Identify()
        {
            lock (_lock)
            {
                if (_isLogging || _session is not null)
                    throw new InvalidOperationException("Stop high-speed logging before testing the connection.");
            }
            return Task.Run(IdentifyAsync).GetAwaiter().GetResult();
        }

        private async Task<HighSpeedIdentifyResult> IdentifyAsync()
        {
            CcpSessionLease lease;
            try { lease = await _host.AcquireAsync().ConfigureAwait(false); }
            catch (CcpTimeoutException) { return new HighSpeedIdentifyResult { SessionOpened = false }; }
            catch (CcpProtocolException) { return new HighSpeedIdentifyResult { SessionOpened = true, Identified = false }; }

            await using (lease.ConfigureAwait(false))
            {
                CcpBuildIdentification identification;
                try { identification = await lease.Master.IdentifyAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is CcpTimeoutException or CcpException or CcpProtocolException)
                {
                    return new HighSpeedIdentifyResult { SessionOpened = true, Identified = false };
                }
                var slave = identification.Slave;
                var fields = slave.Fields;
                return new HighSpeedIdentifyResult
                {
                    SessionOpened = true,
                    Identified = true,
                    FirmwareIdLength = fields.IdLength,
                    Magic = (uint)(fields.DataType << 24 | fields.AvailableResources << 16 |
                        fields.ProtectedResources << 8 | fields.DriverVersion),
                    SlaveId = slave.SlaveId,
                    CcpVersion = $"{slave.VersionMajor}.{slave.VersionMinor}",
                    CalibrationId = identification.CalibrationId,
                    CalibrationDescription = identification.CalibrationDescription,
                };
            }
        }
    }
}
