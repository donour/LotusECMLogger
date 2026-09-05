using System.Diagnostics;
using System.Text.Json;
using LotusECMLogger.Services.Logging;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>Primary Bosch ABS diagnostics on verified OEM CAN addresses 6F4/6F5.
    /// Baseline and live captures use bounded 1A/21 reads. Legacy broadcast decoding remains provisional.
    /// </summary>
    public sealed class J2534AbsService : IAbsService, IDisposable
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        /// <summary>Guards the J2534 device: one operation (or the telemetry monitor) at a time.</summary>
        private readonly object _deviceLock = new();

        private Thread? _telemetryThread;
        private CancellationTokenSource? _telemetryCts;
        private volatile bool _monitoring;
        private Thread? _diagnosticThread;
        private CancellationTokenSource? _diagnosticCts;
        private volatile bool _diagnosticMonitoring;
        private volatile bool _disposed;
        private readonly CancellationTokenSource _lifetimeCts = new();

        public event EventHandler<AbsDiagnosticSample>? DiagnosticSampleReceived;
        public event EventHandler<string>? DiagnosticMonitorError;
        public bool IsMonitoringDiagnostics => _diagnosticMonitoring;

        public event EventHandler<AbsTelemetrySample>? TelemetryReceived;
        public event EventHandler<string>? TelemetryError;

        public bool IsMonitoringTelemetry => _monitoring;

        // ═══════════════════════════════════════════════════════════════════════════════
        // §7 / §6 — identification and coding records
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress)
        {
            var (success, error, baseline) = ReadBaseline(progress);
            return (success, error, new AbsModuleInfo { Fields = baseline.Rows });
        }

        public (bool success, string errorMessage, AbsDiagnosticBaseline result) ReadBaseline(IProgress<string>? progress)
        {
            try
            {
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var abs = AbsKwpSession.Open();
                    var client = new AbsDiagnosticOperations(abs.Request, _lifetimeCts.Token);
                    var baseline = client.ReadBaseline(progress);
                    bool anyRead = baseline.Exchanges.Any(e => e.Success && !e.RequestHex.StartsWith("10"));
                    return (anyRead, anyRead ? "" : "No baseline read succeeded; raw failures are retained.", baseline);
                }
            }
            catch (Exception error)
            {
                return (false, error.Message, AbsDiagnosticCapture.BuildBaseline(DateTimeOffset.UtcNow, []));
            }
        }

        public (bool success, string errorMessage, AbsLiveStateResult result) ReadLiveState(IProgress<string>? progress)
        {
            var (ok, error, baseline) = ReadBaseline(progress);
            var exchange = baseline.Exchanges.LastOrDefault(e => e.RequestHex == "2104");
            if (exchange is null)
                return (false, error, new AbsLiveStateResult { Rows = baseline.Rows });
            var sample = AbsDiagnosticCapture.BuildSample(exchange, baseline);
            return (ok && exchange.Success, exchange.Error, new AbsLiveStateResult { Rows = sample.Rows });
        }

        public void StartDiagnosticMonitor(string captureFilePath, int intervalMs, string notes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(captureFilePath);
            if (intervalMs is < 100 or > 5000)
                throw new ArgumentOutOfRangeException(nameof(intervalMs), "Choose 100–5000 milliseconds.");
            string captureNotes = $"Requested poll interval: {intervalMs} ms. Host receipt timestamps; not ECU sample times.\n{notes}";
            AbsDiagnosticCapture.ValidateText(captureNotes, nameof(notes));
            lock (_deviceLock)
            {
                EnsureDeviceFree();
                if (File.Exists(captureFilePath) || Directory.Exists(captureFilePath))
                    throw new IOException("Choose a new capture filename; existing data is never overwritten.");
                _diagnosticCts?.Dispose();
                _diagnosticCts = new CancellationTokenSource();
                var token = _diagnosticCts.Token;
                _diagnosticMonitoring = true;
                _diagnosticThread = new Thread(() => DiagnosticLoop(captureFilePath, intervalMs, captureNotes, token))
                { IsBackground = true, Name = "ABS Diagnostic Capture" };
                try { _diagnosticThread.Start(); }
                catch
                {
                    _diagnosticMonitoring = false;
                    _diagnosticCts.Dispose(); _diagnosticCts = null; _diagnosticThread = null;
                    throw;
                }
            }
        }

        public void StopDiagnosticMonitor()
        {
            Thread? worker;
            lock (_deviceLock)
            {
                _diagnosticCts?.Cancel();
                worker = _diagnosticThread;
            }
            if (worker is not null && worker != Thread.CurrentThread)
                worker.Join(TimeSpan.FromSeconds(3));
            lock (_deviceLock)
            {
                // Never advertise a free device while a slow driver call is still unwinding.
                if (worker?.IsAlive == true || !ReferenceEquals(worker, _diagnosticThread)) return;
                _diagnosticCts?.Dispose(); _diagnosticCts = null; _diagnosticThread = null;
            }
        }

        private void DiagnosticLoop(string path, int intervalMs, string notes, CancellationToken token)
        {
            string? failure = null;
            try
            {
                using var abs = AbsKwpSession.Open();
                var client = new AbsDiagnosticOperations(abs.Request, token);
                var baseline = client.ReadBaseline();
                using var writer = new AbsDiagnosticCaptureWriter(path, baseline, notes);
                while (!token.IsCancellationRequested)
                {
                    long started = Stopwatch.GetTimestamp();
                    var sample = client.ReadSample(baseline);
                    writer.Append(sample); // Raw replies and failures are durable before UI notification.
                    DiagnosticSampleReceived?.Invoke(this, sample);
                    double remaining;
                    while ((remaining = intervalMs - Stopwatch.GetElapsedTime(started).TotalMilliseconds) > 0)
                    {
                        if (token.WaitHandle.WaitOne((int)Math.Min(remaining + 1, 250)))
                            token.ThrowIfCancellationRequested();
                        abs.KeepAlive(token);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { failure = error.Message; }
            finally
            {
                lock (_deviceLock) _diagnosticMonitoring = false;
                // An empty completion message also lets the UI recover after a slow stop.
                DiagnosticMonitorError?.Invoke(this, failure ?? "");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // §8 — fault codes
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsDtcResult result) ReadDtcs()
        {
            try
            {
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var abs = AbsKwpSession.Open();

                    // ReadDtcByStatus: report all DTCs by status mask (18 00 FF 00) — the exact request
                    // the reference tester used, which the ABS answers with no session and no unlock.
                    var response = abs.Request([AbsProtocol.SidReadDtcByStatus, 0x00, 0xFF, 0x00], _lifetimeCts.Token);
                    if (!response.Ok)
                        return (false, $"Failed to read ABS DTCs: {response.DetailedError}", AbsDtcResult.Empty);

                    var result = AbsDtcResult.FromResponse(response.Payload);

                    // ReadStatusOfDtc (0x17) per stored code — the module's own view of each fault's
                    // confirmed/pending state, which can differ from the status in the 0x18 summary.
                    var rows = new List<AbsReportRow>(result.Rows);
                    foreach (var (code, _) in result.Codes)
                    {
                        if (_lifetimeCts.IsCancellationRequested) break;
                        var status = abs.Request([AbsProtocol.SidReadStatusOfDtc, (byte)(code >> 8), (byte)code], _lifetimeCts.Token);
                        rows.Add(status.Ok
                            ? new AbsReportRow($"Status of {AbsProtocol.FormatDtcCode(code)}",
                                DescribeDtcStatusResponse(status.Payload), BitConverter.ToString(status.Payload))
                            : new AbsReportRow($"Status of {AbsProtocol.FormatDtcCode(code)}",
                                "unavailable", status.DetailedError));
                    }

                    return (true, "", result with { Rows = rows });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsDtcResult.Empty);
            }
        }

        /// <summary>
        /// Renders a ReadStatusOfDtc (0x57) payload. The response echoes the requested code and ends
        /// with the status byte, so the last byte is decoded as status and the echo left in the raw
        /// column rather than assuming a fixed offset.
        /// </summary>
        private static string DescribeDtcStatusResponse(byte[] payload) =>
            payload.Length == 0
                ? "empty response"
                : $"0x{payload[^1]:X2} — {AbsProtocol.DescribeDtcStatus(payload[^1])}";

        // ═══════════════════════════════════════════════════════════════════════════════
        // §4 — passive telemetry
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>UI event throttle, independent of the unverified broadcast rate.</summary>
        private const int TelemetryEventIntervalMs = 100;

        public void StartTelemetryMonitor(string? csvFilePath)
        {
            lock (_deviceLock)
            {
                EnsureDeviceFree();
                _telemetryCts?.Dispose();

                _telemetryCts = new CancellationTokenSource();
                CancellationToken token = _telemetryCts.Token;
                _monitoring = true;

                _telemetryThread = new Thread(() => TelemetryLoop(csvFilePath, token))
                {
                    IsBackground = true,
                    Name = "ABS Telemetry Monitor",
                };
                try { _telemetryThread.Start(); }
                catch
                {
                    _monitoring = false;
                    _telemetryCts.Dispose(); _telemetryCts = null; _telemetryThread = null;
                    throw;
                }
            }
        }

        public void StopTelemetryMonitor()
        {
            Thread? thread;
            lock (_deviceLock)
            {
                // Keyed on the thread, not on _monitoring: a monitor that stopped itself after an
                // error has already cleared _monitoring but still needs its state cleaned up here.
                if (_telemetryThread is null)
                    return;

                _telemetryCts?.Cancel();
                thread = _telemetryThread;
            }

            // Joined outside the lock: the loop takes the device lock on its way out.
            thread?.Join(TimeSpan.FromSeconds(3));

            lock (_deviceLock)
            {
                if (thread?.IsAlive == true || !ReferenceEquals(thread, _telemetryThread)) return;
                _telemetryCts?.Dispose();
                _telemetryCts = null;
                _telemetryThread = null;
                _monitoring = false;
            }
        }

        /// <summary>
        /// Reads the module's broadcasts and decodes them until cancelled. Nothing is transmitted, so
        /// the raw capture does not change module state. Broadcast interpretation remains provisional.
        /// </summary>
        private void TelemetryLoop(string? csvFilePath, CancellationToken token)
        {
            ISampleSink? sink = null;
            string? failure = null;
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenCan();
                channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                if (!string.IsNullOrWhiteSpace(csvFilePath))
                {
                    var header = new SampleLogHeader("Lotus ABS/ESP Telemetry",
                    [
                        "Passive capture of the module's 0xA2/0xA4/0xA8 broadcasts; nothing is transmitted.",
                        "One row per recognized frame. Other fields carry earlier values; they are not simultaneous samples.",
                        "RawA2/RawA4/RawA8 retain payload bytes. Count/status interpretation and broadcast rates are provisional; no physical speed scale is applied.",
                    ]);
                    sink = new CsvSampleSink(csvFilePath, header, TelemetryCsvColumns);
                }

                var sample = new AbsTelemetrySample();
                DateTime lastEvent = DateTime.MinValue;

                while (!token.IsCancellationRequested)
                {
                    var read = channel.ReadMessages(64, 50);
                    bool updated = false;

                    foreach (var message in read.Messages)
                    {
                        byte[] data = message.Data;
                        if (data is null || data.Length < 5)
                            continue;

                        uint id = FrameId(data);
                        if (id is not (AbsTelemetryDecoder.FrontWheelsCanId
                            or AbsTelemetryDecoder.RearWheelsCanId
                            or AbsTelemetryDecoder.EspStatusCanId))
                            continue;

                        AbsTelemetrySample previous = sample;
                        sample = AbsTelemetryDecoder.Apply(sample, id, data[4..]);
                        if (ReferenceEquals(previous, sample))
                            continue;

                        updated = true;

                        // Preserve every recognized raw frame; carried fields are not simultaneous samples.
                        if (sink is not null)
                            WriteTelemetrySample(sink, sample);
                    }

                    if (updated && (DateTime.UtcNow - lastEvent).TotalMilliseconds >= TelemetryEventIntervalMs)
                    {
                        lastEvent = DateTime.UtcNow;
                        TelemetryReceived?.Invoke(this, sample);
                    }
                }
            }
            catch (Exception ex) { failure = ex.Message; }
            finally
            {
                try { sink?.Dispose(); } catch (Exception error) { failure ??= error.Message; }
                lock (_deviceLock) _monitoring = false;
                TelemetryError?.Invoke(this, failure ?? "");
            }
        }

        /// <summary>Columns of a telemetry log; the sink supplies Timestamp and RelativeTime_ms.</summary>
        private static readonly SampleColumn[] TelemetryCsvColumns =
        [
            "LF", "RF", "LR", "RR", "VehicleSpeedRaw", "BrakeSwitch",
            "EspActive", "AbsActive", "TorqueRequest", "EspWarning",
            SampleColumn.Text("RawA2"), SampleColumn.Text("RawA4"), SampleColumn.Text("RawA8"),
        ];

        /// <summary>
        /// Writes one decoded sample. A field the module reported as unavailable is cleared rather
        /// than set, so it lands as an empty cell instead of being read back as a genuine zero.
        /// </summary>
        private static void WriteTelemetrySample(ISampleSink sink, AbsTelemetrySample s)
        {
            SetOrClear("LF", s.WheelLf);
            SetOrClear("RF", s.WheelRf);
            SetOrClear("LR", s.WheelLr);
            SetOrClear("RR", s.WheelRr);
            SetOrClear("VehicleSpeedRaw", s.VehicleSpeedRaw);
            sink.SetText("RawA2", s.RawA2 ?? "");
            sink.SetText("RawA4", s.RawA4 ?? "");
            sink.SetText("RawA8", s.RawA8 ?? "");
            SetOrClear("BrakeSwitch", s.BrakeSwitch);
            SetOrClear("EspActive", Flag(s.EspActive));
            SetOrClear("AbsActive", Flag(s.AbsActive));
            SetOrClear("TorqueRequest", Flag(s.TorqueRequest));
            SetOrClear("EspWarning", Flag(s.EspWarning));

            sink.WriteRow(s.Timestamp);

            void SetOrClear(string column, double? value)
            {
                if (value is double present)
                    sink.Set(column, present);
                else
                    sink.Clear(column);
            }

            static double? Flag(bool? value) => value is null ? null : value.Value ? 1 : 0;
        }

        public (bool success, string errorMessage, AbsTelemetrySample result) ReadTelemetrySnapshot(int durationMs)
        {
            try
            {
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var session = J2534Session.Open();
                    J2534Channel channel = session.OpenCan();
                    channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                    var sample = new AbsTelemetrySample();
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(durationMs);
                    while (DateTime.UtcNow < deadline && !_lifetimeCts.IsCancellationRequested)
                    {
                        foreach (var message in channel.ReadMessages(64, 25).Messages)
                        {
                            byte[] data = message.Data;
                            if (data is { Length: >= 5 })
                                sample = AbsTelemetryDecoder.Apply(sample, FrameId(data), data[4..]);
                        }
                    }

                    return (true, "", sample);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new AbsTelemetrySample());
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // §9 — pump / valve actuation
        // ═══════════════════════════════════════════════════════════════════════════════

        private const string ActuationUnavailable =
            "Generic valve and bleed routines remain unavailable. Use the separately traced Pump Test for the MRA relay. No actuation request was sent.";

        public (bool success, string errorMessage, AbsPreconditionCheck result) CheckActuationPreconditions() =>
            (false, ActuationUnavailable, new AbsPreconditionCheck
            { Rows = [new AbsReportRow("Actuation", "unavailable", ActuationUnavailable)] });

        public (bool success, string errorMessage, AbsRoutineResult result) RunRoutine(
            byte routineType, int seconds, IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken) =>
            (false, ActuationUnavailable, new AbsRoutineResult
            { Rows = [new AbsReportRow("Actuation", "unavailable", ActuationUnavailable)] });

        public (bool success, string errorMessage, AbsRoutineResult result) RunPumpCycle(int seconds,
            bool operatorConfirmed, string captureFilePath, IProgress<AbsRoutineProgress>? progress,
            CancellationToken cancellationToken)
        {
            AbsRoutineResult? operationResult = null;
            try
            {
                if (seconds is < 1 or > AbsPumpOperations.MaximumSeconds || !operatorConfirmed)
                    throw new ArgumentException("Choose 1–5 seconds and confirm stationary vehicle, engine off and ignition on.");
                ArgumentException.ThrowIfNullOrWhiteSpace(captureFilePath);
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    // This journal is raw throughout: it is not a diagnostic live-data capture.
                    var header = AbsDiagnosticCapture.BuildBaseline(DateTimeOffset.UtcNow, []);
                    using var writer = new AbsDiagnosticCaptureWriter(captureFilePath, header,
                        $"OEM MRA pump test. Requested {seconds} seconds. Operator affirmed stationary, engine off, ignition on. Raw command journal; no physical motor feedback.");
                    using var abs = AbsKwpSession.Open();
                    var operation = new AbsPumpOperations((payload, token) => abs.Request(payload, token, 800),
                        exchange => writer.Append(AbsDiagnosticCapture.BuildSample(exchange, header)));
                    operationResult = operation.Run(seconds, operatorConfirmed, progress, linked.Token);
                    string error = string.Join("; ", operationResult.Rows.Where(r => r.Field == "Error").Select(r => r.Value));
                    return (operationResult.Completed, error, operationResult);
                }
            }
            catch (OperationCanceledException) when (operationResult is null)
            {
                return (false, "Cancelled before pump activation.", new AbsRoutineResult { Cancelled = true });
            }
            catch (Exception error)
            {
                if (operationResult is not null)
                    return (false, error.Message, operationResult with
                    {
                        Completed = false,
                        Rows = operationResult.Rows.Concat(new[] { new AbsReportRow("Error", $"Closing pump test: {error.Message}") }).ToArray(),
                    });
                return (false, error.Message, new AbsRoutineResult { Rows = [new("Error", error.Message)] });
            }
        }

        public (bool success, string errorMessage, AbsRoutineResult result) RunBleedSequence(
            IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken) =>
            (false, ActuationUnavailable, new AbsRoutineResult
            { Rows = [new AbsReportRow("Actuation", "unavailable", ActuationUnavailable)] });

        public (bool success, string errorMessage, AbsProbeResult result) ProbeConnection()
        {
            var rows = new List<AbsReportRow>();
            bool replied = false;
            try
            {
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var abs = AbsKwpSession.Open();
                    foreach (byte[] payload in new byte[][] { [0x3e], [0x1a, 0x85] })
                    {
                        var response = abs.Request(payload, _lifetimeCts.Token);
                        replied |= response.Ok;
                        rows.Add(new AbsReportRow($"6F4→6F5 {Convert.ToHexString(payload)}",
                            Convert.ToHexString(response.RawResponse), response.Ok ? "Matching reply" : response.DetailedError));
                    }
                }
                return (replied, replied ? "" : "No matching reply on the OEM ABS addresses.", new AbsProbeResult { Rows = rows });
            }
            catch (Exception error) { return (false, error.Message, new AbsProbeResult { Rows = rows }); }
        }

        public (bool success, string errorMessage, AbsSniffResult result) SniffBus(
            int captureSeconds, IProgress<string>? progress)
        {
            try
            {
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var session = J2534Session.Open();
                    // CAN_ID_BOTH so we also capture 29-bit ids, in case the ABS uses extended addressing.
                    J2534Channel channel = session.OpenChannel(Protocol.CAN, Baud.CAN, ConnectFlag.CAN_ID_BOTH);
                    channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                    // Phase 1 — learn the periodic broadcast ids while the tester is idle.
                    progress?.Report("Learning bus baseline (5s) — keep the reference tester idle…");
                    var baseline = new HashSet<uint>();
                    DateTime b0 = DateTime.UtcNow;
                    while ((DateTime.UtcNow - b0).TotalSeconds < 5 && !_lifetimeCts.IsCancellationRequested)
                        foreach (var m in channel.ReadMessages(64, 50).Messages)
                            if (m.Data is { Length: >= 4 })
                                baseline.Add(FrameId(m.Data));

                    // Phase 2 — log every frame on an id that was NOT broadcasting in the baseline. The
                    // tester↔ABS diagnostic exchange appears here because those ids are only active
                    // while the tester is talking.
                    progress?.Report($"Capturing {captureSeconds}s — run the reference tester's ABS read NOW…");
                    var frames = new List<string>();
                    var counts = new SortedDictionary<uint, int>();
                    DateTime start = DateTime.UtcNow;
                    while ((DateTime.UtcNow - start).TotalSeconds < captureSeconds && frames.Count < 20000 && !_lifetimeCts.IsCancellationRequested)
                    {
                        foreach (var m in channel.ReadMessages(64, 50).Messages)
                        {
                            byte[] data = m.Data;
                            if (data is null || data.Length < 4)
                                continue;

                            uint id = FrameId(data);
                            if (baseline.Contains(id))
                                continue;

                            double ms = (DateTime.UtcNow - start).TotalMilliseconds;
                            string payload = data.Length > 4 ? BitConverter.ToString(data, 4) : "";
                            frames.Add($"{ms,8:F0} ms  0x{id:X3}  {payload}");
                            counts[id] = counts.GetValueOrDefault(id) + 1;
                        }
                    }

                    progress?.Report("Sniff complete");
                    return (true, "", new AbsSniffResult
                    {
                        BaselineIdCount = baseline.Count,
                        NewIds = counts.Select(kv => $"0x{kv.Key:X3}{ModuleName(kv.Key)} — {kv.Value} frame(s)").ToList(),
                        Frames = frames,
                    });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsSniffResult.Empty);
            }
        }

        public (bool success, string errorMessage, AbsFlashResult result) FlashFirmware(
            string firmwarePath, AbsFlashOptions options, IProgress<AbsFlashProgress>? progress,
            CancellationToken cancellationToken)
        {
            AbsFirmwareImage? image = null;
            try
            {
                image = AbsFirmwareImage.Load(firmwarePath);
                if (string.IsNullOrWhiteSpace(options.DriverFileName))
                    throw new InvalidOperationException("Select the J2534 driver explicitly before ABS programming.");
                lock (_deviceLock)
                {
                    EnsureDeviceFree();
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    string auditPath = BeginFlashAudit(image, options);
                    using var abs = AbsKwpSession.Open(options.DriverFileName);
                    var flasher = new AbsFirmwareFlasher(
                        (payload, token, timeout) => abs.RequestProgramming(payload, token, timeout),
                        abs.MeasureBatteryVoltage, exchangeAudit: exchange => AppendFlashExchange(auditPath, exchange),
                        enforceProductionGeometry: true);
                    AbsFlashResult result = flasher.Flash(image, options, progress, linked.Token);
                    result = result with { AuditLogPath = auditPath };
                    try { WriteFlashAudit(auditPath, image, options, result); }
                    catch (Exception auditError)
                    {
                        result = result with { Rows = result.Rows.Append(new AbsReportRow("Audit", $"Final audit append failed: {auditError.Message}; exchange audit remains available.")).ToArray() };
                    }
                    string error = result.Rows.FirstOrDefault(row => row.Field == "Error")?.Value ?? "";
                    return (result.Completed, error, result);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Flashing cancelled before programming.", new AbsFlashResult { Cancelled = true, ImageSha256 = image?.Sha256 ?? "" });
            }
            catch (Exception error)
            {
                return (false, error.Message, new AbsFlashResult
                {
                    ImageSha256 = image?.Sha256 ?? "",
                    Rows = [new AbsReportRow("Error", error.Message), new AbsReportRow("Integrity", AbsFirmwareFlasher.IntegrityWarning)],
                });
            }
        }

        private static string BeginFlashAudit(AbsFirmwareImage image, AbsFlashOptions options)
        {
            string path = Path.Combine(LoggerPaths.OutputDirectory, "abs-flash-audit.jsonl");
            LoggerPaths.EnsureParentDirectory(path);
            var header = new { TimestampUtc = DateTimeOffset.UtcNow, Event = "flash-started", image.SourcePath, image.Sha256, image.StartAddress, image.EndAddressExclusive, image.Manifest, options.DriverFileName, options.MinimumBatteryVoltage };
            File.AppendAllText(path, JsonSerializer.Serialize(header) + Environment.NewLine);
            return path;
        }

        private static void AppendFlashExchange(string path, AbsFlashExchange exchange)
        {
            File.AppendAllText(path, JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow, Event = "exchange", exchange }) + Environment.NewLine);
        }

        private static void WriteFlashAudit(string path, AbsFirmwareImage image, AbsFlashOptions options, AbsFlashResult result)
        {
            var record = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                image.SourcePath,
                image.Sha256,
                image.StartAddress,
                image.EndAddressExclusive,
                image.Manifest,
                options.MinimumBatteryVoltage,
                result.Completed,
                result.Cancelled,
                result.BlocksSent,
                result.BytesSent,
                result.BatteryVoltage,
                result.IntegrityWarning,
                Exchanges = result.Exchanges,
            };
            File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Shared helpers
        // ═══════════════════════════════════════════════════════════════════════════════

        private static MessageFilter PassAllFilter() => new()
        {
            FilterType = Filter.PASS_FILTER,
            Mask = [0x00, 0x00, 0x00, 0x00],
            Pattern = [0x00, 0x00, 0x00, 0x00],
        };

        private static uint FrameId(byte[] data) =>
            (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);

        private static string ModuleName(uint id) => id switch
        {
            0x6f5 => " (ABS diagnostic)",
            0x7e8 => " (ECM)",
            0x7e9 => " (TCM)",
            _ => "",
        };

        /// <summary>
        /// Rejects an operation that would open the J2534 device while the telemetry monitor owns it.
        /// Only one channel set can be open at a time, and a second open would fail deeper down with a
        /// far less obvious error.
        /// </summary>
        private void EnsureDeviceFree()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_monitoring || _diagnosticMonitoring)
                throw new InvalidOperationException("Stop the ABS capture or telemetry monitor before another ABS operation.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetimeCts.Cancel();
            StopDiagnosticMonitor();
            StopTelemetryMonitor();
        }
    }
}
