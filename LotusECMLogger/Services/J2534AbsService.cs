using System.Globalization;
using System.Text;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// KWP2000 (ISO 14230) diagnostic client for the Bosch ESP8 ABS/ESP module over ISO-TP,
    /// implementing <c>DIAGNOSTICS_PROGRAMMING_GUIDE.md</c>. The module answers on CAN ids
    /// 0x6F4 (request) / 0x6F5 (response) — captured from a reference tester, not the ISO 15765-4
    /// slot the guide assumed — so it needs its own flow-control filter and request header.
    ///
    /// Read services (identification, coding, memory, DTCs) and the hydraulic actuation routines
    /// are implemented. Services that alter persistent state — variant recoding (0x3B), memory
    /// write (0x3D), DTC clear (0x14) — are deliberately absent, matching the guide's scope.
    /// </summary>
    public sealed class J2534AbsService : IAbsService, IDisposable
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        /// <summary>
        /// Session bytes tried in order when a service needs an open session. The guide specifies
        /// programming (0x02), but this firmware was observed accepting the reference tester's 0x89
        /// while refusing 0x02, so both are attempted before the extended session.
        /// </summary>
        private static readonly byte[] SessionCandidates =
            [AbsProtocol.SessionProgramming, AbsProtocol.SessionTester, AbsProtocol.SessionExtended];

        /// <summary>Guards the J2534 device: one operation (or the telemetry monitor) at a time.</summary>
        private readonly object _deviceLock = new();

        private Thread? _telemetryThread;
        private CancellationTokenSource? _telemetryCts;
        private volatile bool _monitoring;

        public event EventHandler<AbsTelemetrySample>? TelemetryReceived;
        public event EventHandler<string>? TelemetryError;

        public bool IsMonitoringTelemetry => _monitoring;

        // ═══════════════════════════════════════════════════════════════════════════════
        // §7 / §6 — identification and coding records
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress)
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var abs = AbsKwpSession.Open();
                    var rows = new List<AbsReportRow>();

                    // Open a session. Identification reads work without SecurityAccess on this
                    // firmware, so a refusal here is reported and the scan continues anyway.
                    var (sessionOk, sessionDetail, _) = abs.EnterSession(AbsProtocol.SessionTester,
                        AbsProtocol.SessionExtended, AbsProtocol.SessionProgramming);
                    rows.Add(new AbsReportRow("Diagnostic session", sessionOk ? "open" : "refused", sessionDetail));

                    // ReadEcuIdentification (§7). The guide's "read all" record 0x87 is tried first,
                    // then the whole 0x80-0x9F record space, since 0x87 is not implemented here.
                    progress?.Report("Reading identification records (1A 80-9F)…");
                    int idFound = 0;
                    for (byte record = 0x80; record <= 0x9F; record++)
                    {
                        var r = abs.Request(AbsProtocol.SidReadEcuIdentification, record);
                        if (!r.Ok || r.Payload.Length <= 1)
                            continue;

                        var (value, detail) = FormatData(r.Payload[1..]);
                        rows.Add(new AbsReportRow(IdentificationLabel(record), value, detail));
                        idFound++;
                    }

                    // ReadDataByLocalId (§6). The guide's 2-byte identifiers (0xF190/0xF191) are not
                    // what this module implements — it uses 1-byte local ids — so the full id space is
                    // scanned and whatever answers is reported.
                    progress?.Report("Scanning coding records (21 00-FF)…");
                    int codeFound = 0;
                    for (int lid = 0x00; lid <= 0xFF; lid++)
                    {
                        if ((lid & 0x1F) == 0)
                            progress?.Report($"Scanning coding records… 0x{lid:X2}/0xFF");

                        var r = abs.Request(AbsProtocol.SidReadDataByLocalId, (byte)lid);
                        if (!r.Ok || r.Payload.Length <= 1)
                            continue;

                        byte[] data = r.Payload[1..];
                        var (value, detail) = FormatData(data);

                        // A single-byte coding record is the variant byte the guide describes, so add
                        // its (inferred) bit-field reading alongside the raw value.
                        if (data.Length == 1)
                            detail = AbsProtocol.DescribeVariantCoding(data[0]);

                        rows.Add(new AbsReportRow($"Coding 21 {lid:X2}", value, detail));
                        codeFound++;
                    }

                    rows.Add(new AbsReportRow("Scan summary",
                        $"{idFound} identification + {codeFound} coding record(s)",
                        "read-only; no SecurityAccess required on this firmware"));

                    return (true, "", new AbsModuleInfo { Fields = rows });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsModuleInfo.Empty);
            }
        }

        // Friendly names for the ReadEcuIdentification records confirmed on this module. Traceable
        // via the record number, so easy to correct against a reference tester.
        private static string IdentificationLabel(byte record) => record switch
        {
            0x85 => "Serial number (1A 85)",
            0x86 => "Lotus part number (1A 86)",
            0x93 => "Bosch part number (1A 93)",
            0x9C => "Config byte (1A 9C)",
            _ => $"ECU Id 1A {record:X2}",
        };

        /// <summary>
        /// Splits read data into a display value and a detail column: printable records show their
        /// text with the hex behind them, binary records show hex with the byte count.
        /// </summary>
        private static (string value, string detail) FormatData(byte[] data)
        {
            const int max = 24;
            byte[] shown = data.Length > max ? data[..max] : data;
            string hex = BitConverter.ToString(shown) + (data.Length > max ? $" … ({data.Length} bytes)" : "");
            string ascii = ToPrintable(shown);

            return ascii.Trim('.').Length >= 3 ? (ascii, hex) : (hex, $"{data.Length} byte(s)");
        }

        private static string ToPrintable(byte[] data) =>
            new string(data.Select(b => b >= 0x20 && b < 0x7F ? (char)b : '.').ToArray());

        // ═══════════════════════════════════════════════════════════════════════════════
        // §5 — live internal state (ReadMemoryByAddress)
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsLiveStateResult result) ReadLiveState(IProgress<string>? progress)
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var abs = AbsKwpSession.Open();
                    var rows = new List<AbsReportRow>();

                    // The guide puts memory reads in the default session; this module wants one of its
                    // own session bytes before it will grant security, so a session is opened first and
                    // the outcome recorded either way.
                    var (sessionOk, sessionDetail, _) = abs.EnterSession(SessionCandidates);
                    rows.Add(new AbsReportRow("Diagnostic session", sessionOk ? "open" : "refused", sessionDetail));

                    var (unlockOk, unlockDetail) = abs.TryUnlock();
                    rows.Add(new AbsReportRow("SecurityAccess", unlockOk ? "unlocked" : "not unlocked", unlockDetail));

                    int ok = 0;
                    foreach (var entry in AbsProtocol.LiveStateMap)
                    {
                        progress?.Report($"Reading {entry.Name}…");

                        var response = abs.ReadMemory(entry.Address, entry.Length);
                        string address = $"0x{entry.Address:X8}";

                        if (response.Ok && response.Payload.Length >= entry.Length)
                        {
                            // The raw bytes are always shown: the module's 0x63 response echoes the
                            // address before the data, and if that echo were ever shaped differently
                            // than expected the decoded value would silently be an echo byte. Any
                            // surplus length is called out for the same reason.
                            string note = entry.Note.Length == 0 ? "" : $" — {entry.Note}";
                            if (response.Payload.Length != entry.Length)
                                note = $" — unexpected length ({response.Payload.Length} bytes){note}";

                            rows.Add(new AbsReportRow(entry.Name,
                                AbsProtocol.FormatLiveValue(entry, response.Payload),
                                $"{address} = {BitConverter.ToString(response.Payload)}{note}"));
                            ok++;
                        }
                        else
                        {
                            rows.Add(new AbsReportRow(entry.Name, "unavailable",
                                $"{address} — {response.DetailedError}"));
                        }
                    }

                    rows.Add(new AbsReportRow("Read summary", $"{ok}/{AbsProtocol.LiveStateMap.Length} locations",
                        abs.AcceptedAddressFormat is byte aal
                            ? $"address format byte 0x{aal:X2} accepted"
                            : $"no address format accepted (tried {string.Join(", ",
                                AbsProtocol.AddressAndLengthCandidates.Select(b => $"0x{b:X2}"))})"));

                    return (true, "", new AbsLiveStateResult { Rows = rows });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsLiveStateResult.Empty);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // §8 — fault codes
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsDtcResult result) ReadDtcs()
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var abs = AbsKwpSession.Open();

                    // ReadDtcByStatus: report all DTCs by status mask (18 00 FF 00) — the exact request
                    // the reference tester used, which the ABS answers with no session and no unlock.
                    var response = abs.Request(AbsProtocol.SidReadDtcByStatus, 0x00, 0xFF, 0x00);
                    if (!response.Ok)
                        return (false, $"Failed to read ABS DTCs: {response.DetailedError}", AbsDtcResult.Empty);

                    var result = AbsDtcResult.FromResponse(response.Payload);

                    // ReadStatusOfDtc (0x17) per stored code — the module's own view of each fault's
                    // confirmed/pending state, which can differ from the status in the 0x18 summary.
                    var rows = new List<AbsReportRow>(result.Rows);
                    foreach (var (code, _) in result.Codes)
                    {
                        var status = abs.Request(AbsProtocol.SidReadStatusOfDtc, (byte)(code >> 8), (byte)code);
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

        /// <summary>UI event throttle: the module broadcasts at 100 Hz, far faster than a grid needs.</summary>
        private const int TelemetryEventIntervalMs = 100;

        public void StartTelemetryMonitor(string? csvFilePath)
        {
            lock (_deviceLock)
            {
                if (_monitoring)
                    throw new InvalidOperationException("ABS telemetry monitoring is already running.");

                _telemetryCts = new CancellationTokenSource();
                CancellationToken token = _telemetryCts.Token;
                _monitoring = true;

                _telemetryThread = new Thread(() => TelemetryLoop(csvFilePath, token))
                {
                    IsBackground = true,
                    Name = "ABS Telemetry Monitor",
                };
                _telemetryThread.Start();
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
                _telemetryCts?.Dispose();
                _telemetryCts = null;
                _telemetryThread = null;
                _monitoring = false;
            }
        }

        /// <summary>
        /// Reads the module's broadcasts and decodes them until cancelled. Nothing is transmitted, so
        /// this is safe with the vehicle in motion — the guide's recommended way to log while driving.
        /// </summary>
        private void TelemetryLoop(string? csvFilePath, CancellationToken token)
        {
            StreamWriter? csv = null;
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenCan();
                channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                if (!string.IsNullOrWhiteSpace(csvFilePath))
                {
                    LoggerPaths.EnsureParentDirectory(csvFilePath);
                    csv = new StreamWriter(csvFilePath, append: false);
                    csv.WriteLine("Timestamp,LF,RF,LR,RR,VehicleSpeedRaw,VehicleSpeedKph,BrakeSwitch," +
                                  "EspActive,AbsActive,TorqueRequest,EspWarning");
                }

                var sample = new AbsTelemetrySample();
                DateTime lastEvent = DateTime.MinValue;
                int rowsSinceFlush = 0;

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

                        // One CSV row per front-wheel frame — the 100 Hz anchor of the three.
                        if (csv is not null && id == AbsTelemetryDecoder.FrontWheelsCanId)
                        {
                            WriteTelemetryCsvRow(csv, sample);
                            if (++rowsSinceFlush >= 100)
                            {
                                csv.Flush();
                                rowsSinceFlush = 0;
                            }
                        }
                    }

                    if (updated && (DateTime.UtcNow - lastEvent).TotalMilliseconds >= TelemetryEventIntervalMs)
                    {
                        lastEvent = DateTime.UtcNow;
                        TelemetryReceived?.Invoke(this, sample);
                    }
                }
            }
            catch (Exception ex)
            {
                _monitoring = false;
                TelemetryError?.Invoke(this, ex.Message);
            }
            finally
            {
                try { csv?.Flush(); csv?.Dispose(); } catch { /* closing a log must not mask the exit reason */ }
            }
        }

        private static void WriteTelemetryCsvRow(StreamWriter csv, AbsTelemetrySample s)
        {
            var row = new StringBuilder();
            row.Append(s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',');
            row.Append(Num(s.WheelLf)).Append(',');
            row.Append(Num(s.WheelRf)).Append(',');
            row.Append(Num(s.WheelLr)).Append(',');
            row.Append(Num(s.WheelRr)).Append(',');
            row.Append(Num(s.VehicleSpeedRaw)).Append(',');
            row.Append(s.VehicleSpeedRaw is int raw
                ? AbsTelemetrySample.ToKph(raw).ToString("F2", CultureInfo.InvariantCulture)
                : "").Append(',');
            row.Append(Num(s.BrakeSwitch)).Append(',');
            row.Append(Flag(s.EspActive)).Append(',');
            row.Append(Flag(s.AbsActive)).Append(',');
            row.Append(Flag(s.TorqueRequest)).Append(',');
            row.Append(Flag(s.EspWarning));
            csv.WriteLine(row.ToString());

            static string Num(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
            static string Flag(bool? value) => value is null ? "" : value.Value ? "1" : "0";
        }

        public (bool success, string errorMessage, AbsTelemetrySample result) ReadTelemetrySnapshot(int durationMs)
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var session = J2534Session.Open();
                    J2534Channel channel = session.OpenCan();
                    channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                    var sample = new AbsTelemetrySample();
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(durationMs);
                    while (DateTime.UtcNow < deadline)
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

        /// <summary>How long to listen for telemetry when judging the actuation preconditions.</summary>
        private const int PreconditionSampleMs = 800;

        public (bool success, string errorMessage, AbsPreconditionCheck result) CheckActuationPreconditions()
        {
            var (success, error, sample) = ReadTelemetrySnapshot(PreconditionSampleMs);
            if (!success)
                return (false, error, new AbsPreconditionCheck());

            // Absent signals must not read as "satisfied": each condition requires positive evidence,
            // so a missing broadcast leaves its flag false and TelemetrySeen explains why.
            bool stationary = sample.VehicleSpeedRaw == 0;
            bool brakeReleased = sample.BrakeSwitch == 0;
            bool noIntervention = sample.AbsActive == false && sample.EspActive == false;

            var rows = new List<AbsReportRow>
            {
                new("Vehicle stationary", Verdict(stationary),
                    sample.VehicleSpeedRaw is int v
                        ? $"speed raw {v} ({AbsTelemetrySample.ToKph(v):F1} km/h)"
                        : "no 0xA2 broadcast seen"),
                new("Brake released", Verdict(brakeReleased),
                    sample.BrakeSwitch is int b
                        ? AbsTelemetrySample.BrakeSwitchName(b)
                        : "no 0xA4 broadcast seen"),
                new("No ABS/ESP intervention", Verdict(noIntervention),
                    sample.AbsActive is null
                        ? "no 0xA8 broadcast seen"
                        : $"ABS {OnOff(sample.AbsActive)}, ESP {OnOff(sample.EspActive)}"),
                new("Ignition ON, engine OFF", "check manually",
                    "not observable from ABS telemetry — the module enforces it with NRC 0x22"),
            };

            return (true, "", new AbsPreconditionCheck
            {
                TelemetrySeen = sample.HasData,
                Stationary = stationary,
                BrakeReleased = brakeReleased,
                NoIntervention = noIntervention,
                Rows = rows,
            });

            static string Verdict(bool ok) => ok ? "OK" : "NOT MET";
            static string OnOff(bool? flag) => flag is null ? "unknown" : flag.Value ? "active" : "clear";
        }

        public (bool success, string errorMessage, AbsRoutineResult result) RunRoutine(
            byte routineType, int seconds, IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken)
            => RunRoutines([(routineType, seconds)], progress, cancellationToken);

        public (bool success, string errorMessage, AbsRoutineResult result) RunBleedSequence(
            IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken)
            => RunRoutines(AbsProtocol.BleedSequence, progress, cancellationToken);

        /// <summary>
        /// Runs a sequence of actuation routines on one diagnostic session. Every routine that is
        /// started is stopped again and the module is returned to the default session, whatever the
        /// outcome — the guide is explicit that leaving a routine running can strand the hydraulic
        /// unit in an intermediate valve state until the next power cycle.
        /// </summary>
        private (bool success, string errorMessage, AbsRoutineResult result) RunRoutines(
            IReadOnlyList<(byte Type, int Seconds)> sequence,
            IProgress<AbsRoutineProgress>? progress,
            CancellationToken cancellationToken)
        {
            var rows = new List<AbsReportRow>();

            try
            {
                // Precondition gate before anything is sent. The module also refuses with NRC 0x22,
                // but the guide warns against relying on that alone.
                var (checkOk, checkError, preconditions) = CheckActuationPreconditions();
                if (!checkOk)
                    return (false, $"Could not verify preconditions: {checkError}", AbsRoutineResult.Empty);

                rows.AddRange(preconditions.Rows);
                if (!preconditions.AllSatisfied)
                    return (false, $"Preconditions not met: {preconditions.BlockingReason}",
                        new AbsRoutineResult { Rows = rows });

                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var abs = AbsKwpSession.Open();

                    // Actuation needs the programming session and SecurityAccess level 1.
                    var (sessionOk, sessionDetail, _) = abs.EnterSession(SessionCandidates);
                    rows.Add(new AbsReportRow("Diagnostic session", sessionOk ? "open" : "refused", sessionDetail));
                    if (!sessionOk)
                        return (false, $"Could not open a diagnostic session ({sessionDetail}).",
                            new AbsRoutineResult { Rows = rows });

                    var (unlockOk, unlockDetail) = abs.TryUnlock();
                    rows.Add(new AbsReportRow("SecurityAccess", unlockOk ? "unlocked" : "not unlocked", unlockDetail));

                    bool completed = true;
                    string error = "";

                    try
                    {
                        foreach (var (type, seconds) in sequence)
                        {
                            var (phaseOk, phaseError) =
                                RunOnePhase(abs, type, seconds, rows, progress, cancellationToken);
                            if (phaseOk)
                                continue;

                            completed = false;
                            error = phaseError;
                            break; // a failed phase aborts the sequence; each phase stops itself
                        }
                    }
                    finally
                    {
                        // Return to the default session so the module is released cleanly — the guide
                        // warns that leaving it otherwise can strand the hydraulic unit until the next
                        // power cycle, so this runs even if a phase threw.
                        var restored = abs.Request(AbsProtocol.SidStartDiagnosticSession, AbsProtocol.SessionDefault);
                        rows.Add(new AbsReportRow("Default session restored", restored.Ok ? "yes" : "no",
                            restored.Ok ? "10 01 accepted" : restored.DetailedError));
                    }

                    return (completed, error, new AbsRoutineResult { Rows = rows, Completed = completed });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new AbsRoutineResult { Rows = rows });
            }
        }

        /// <summary>Poll interval for routine status and the monitored valve/pressure locations.</summary>
        private const int RoutinePollIntervalMs = 500;

        /// <summary>
        /// Starts one routine, polls it for <paramref name="seconds"/>, then stops it. The stop is in
        /// a finally block so cancellation, an exception, or a mid-run error still shuts the pump off.
        /// </summary>
        private static (bool ok, string error) RunOnePhase(
            AbsKwpSession abs, byte type, int seconds, List<AbsReportRow> rows,
            IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken)
        {
            var routine = AbsProtocol.FindRoutine(type);
            string phase = $"{routine?.Name ?? "Routine"} (0x{type:X2})";

            var start = abs.Request(AbsProtocol.SidStartRoutineByLocalId, AbsProtocol.RoutineSubFunction, type);
            if (!start.Ok)
            {
                rows.Add(new AbsReportRow(phase, "start refused", start.DetailedError));
                return (false, $"{phase} could not be started: {start.DetailedError}");
            }

            rows.Add(new AbsReportRow(phase, "started", routine?.Description ?? ""));

            try
            {
                DateTime begin = DateTime.UtcNow;
                var lastRows = new List<AbsReportRow>();

                while (true)
                {
                    double elapsed = (DateTime.UtcNow - begin).TotalSeconds;
                    if (elapsed >= seconds || cancellationToken.IsCancellationRequested)
                        break;

                    lastRows = PollRoutine(abs, type);
                    progress?.Report(new AbsRoutineProgress
                    {
                        Phase = phase,
                        ElapsedSeconds = elapsed,
                        TotalSeconds = seconds,
                        Rows = lastRows,
                    });

                    // Requests refresh the session clock themselves; this covers any quiet gap.
                    abs.KeepAlive();
                    cancellationToken.WaitHandle.WaitOne(RoutinePollIntervalMs);
                }

                rows.AddRange(lastRows.Select(r => r with { Field = $"{phase} — {r.Field}" }));

                if (cancellationToken.IsCancellationRequested)
                {
                    rows.Add(new AbsReportRow(phase, "cancelled", "stopped early at the operator's request"));
                    return (false, $"{phase} was cancelled.");
                }

                return (true, "");
            }
            finally
            {
                var stop = abs.Request(AbsProtocol.SidStopRoutineByLocalId, AbsProtocol.RoutineSubFunction, type);
                rows.Add(new AbsReportRow(phase, stop.Ok ? "stopped" : "STOP FAILED",
                    stop.Ok ? "32 01 accepted" : stop.DetailedError));
            }
        }

        /// <summary>
        /// Polls a running routine: per-wheel status from RequestRoutineResults (0x33) plus the valve
        /// positions and brake pressures the guide recommends watching to confirm the hydraulic unit
        /// is actually responding.
        /// </summary>
        private static List<AbsReportRow> PollRoutine(AbsKwpSession abs, byte type)
        {
            var rows = new List<AbsReportRow>();

            var poll = abs.Request(AbsProtocol.SidRequestRoutineResults, AbsProtocol.RoutineSubFunction, type);
            if (poll.Ok)
            {
                // Response payload is [echoed sub-function][echoed type][LF RF LR RR].
                byte[] status = poll.Payload.Length >= 6 ? poll.Payload[2..6] : [];
                rows.Add(status.Length == 4
                    ? new AbsReportRow("Wheel status", string.Join("  ",
                        AbsProtocol.RoutineWheelNames.Select((name, i) =>
                            $"{name}={AbsProtocol.RoutineWheelStatus(status[i])}")),
                        BitConverter.ToString(poll.Payload))
                    : new AbsReportRow("Wheel status", "unexpected length", BitConverter.ToString(poll.Payload)));
            }
            else
            {
                rows.Add(new AbsReportRow("Wheel status", "unavailable", poll.DetailedError));
            }

            foreach (var entry in AbsProtocol.ActuationMonitorMap)
            {
                var response = abs.ReadMemory(entry.Address, entry.Length);
                rows.Add(response.Ok && response.Payload.Length >= entry.Length
                    ? new AbsReportRow(entry.Name, AbsProtocol.FormatLiveValue(entry, response.Payload),
                        $"0x{entry.Address:X8} = {BitConverter.ToString(response.Payload)}")
                    : new AbsReportRow(entry.Name, "unavailable", response.DetailedError));
            }

            return rows;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Addressing discovery tools
        // ═══════════════════════════════════════════════════════════════════════════════

        public (bool success, string errorMessage, AbsProbeResult result) ProbeConnection()
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var session = J2534Session.Open();
                    J2534Channel channel = session.OpenCan();

                    // Pass-all filter so we can learn the broadcast baseline and catch a reply on any id.
                    channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                    var rows = new List<AbsReportRow>();

                    // Baseline — learn the periodic broadcast ids so request-triggered replies stand
                    // out. Also confirms the channel receives at all.
                    var baseline = new HashSet<uint>(Listen(channel, 1200).Keys);
                    rows.Add(new AbsReportRow("Bus",
                        baseline.Count == 0 ? "SILENT" : $"alive — {baseline.Count} broadcast id(s)",
                        baseline.Count == 0 ? "no CAN traffic received at all" : SampleIds(baseline)));

                    // The ABS broadcasts are the quickest confirmation that the module itself is awake.
                    rows.Add(new AbsReportRow("ABS telemetry",
                        baseline.Overlaps([AbsTelemetryDecoder.FrontWheelsCanId,
                            AbsTelemetryDecoder.RearWheelsCanId, AbsTelemetryDecoder.EspStatusCanId])
                            ? "broadcasting" : "not seen",
                        "0x0A2 / 0x0A4 / 0x0A8 at 100 Hz"));

                    void Named(string label, uint reqId, byte[] payload)
                    {
                        var hits = Probe(channel, reqId, payload, baseline, 250);
                        if (hits.Count == 0)
                            rows.Add(new AbsReportRow(label, "no reply"));
                        else
                            rows.AddRange(hits.Select(h => new AbsReportRow(label, "reply", h)));
                    }

                    // The confirmed ABS diagnostic pair first: a DTC read is the request the reference
                    // tester used and needs neither session nor unlock, so a reply here is definitive.
                    Named($"ABS 0x{Abs.RequestId:X3} ReadDtcByStatus (18 00 FF 00)", Abs.RequestId, [0x18, 0x00, 0xFF, 0x00]);
                    Named($"ABS 0x{Abs.RequestId:X3} TesterPresent (3E 00)", Abs.RequestId, [0x3E, 0x00]);

                    // ECM control proves the request/response path and that the bus is awake.
                    Named("ECM control (0x7E0, 01 00)", 0x7E0, [0x01, 0x00]);

                    // Functional broadcast — every module must listen on 0x7DF.
                    Named("Functional (0x7DF, 3E 00)", 0x7DF, [0x3E, 0x00]);

                    // Physical scan — StartDiagnosticSession(default) to every 8th id across the whole
                    // 11-bit diagnostic range, skipping the ECM and any id a node already broadcasts on
                    // (to avoid an arbitration clash). 10 01 selects the default session: nothing changes.
                    int responders = 0;
                    int scanned = 0;
                    for (uint reqId = 0x600; reqId <= 0x7F8; reqId += 0x08)
                    {
                        if (reqId == 0x7E0 || baseline.Contains(reqId))
                            continue;
                        scanned++;
                        foreach (string hit in Probe(channel, reqId, [0x10, 0x01], baseline, 80))
                        {
                            rows.Add(new AbsReportRow($"Scan 0x{reqId:X3}", "reply", hit));
                            responders++;
                        }
                    }
                    rows.Add(new AbsReportRow("Scan", $"{responders} responder(s)",
                        $"swept {scanned} ids (0x600-0x7F8, step 8) with 10 01"));

                    return (true, "", new AbsProbeResult { Rows = rows });
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsProbeResult.Empty);
            }
        }

        /// <summary>
        /// Sends a single-frame request to <paramref name="requestId"/> and returns a description of
        /// every distinct reply (any id) that was NOT already broadcasting in the baseline (empty when
        /// silent). The payloads used here are read-only: OBD Mode 01, TesterPresent, ReadDtcByStatus,
        /// and StartDiagnosticSession (which changes only volatile session state, never module data).
        /// </summary>
        private static List<string> Probe(
            J2534Channel channel, uint requestId, byte[] payload, HashSet<uint> baseline, int listenMs)
        {
            // Raw ISO-TP single frame: [4-byte CAN id][PCI = payload length][payload] padded to 8.
            byte[] frame = new byte[12];
            frame[0] = (byte)((requestId >> 24) & 0xFF);
            frame[1] = (byte)((requestId >> 16) & 0xFF);
            frame[2] = (byte)((requestId >> 8) & 0xFF);
            frame[3] = (byte)(requestId & 0xFF);
            frame[4] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 5, payload.Length);
            channel.SendMessage(frame);

            var results = new List<string>();
            var seen = new HashSet<string>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(listenMs);
            while (DateTime.UtcNow < deadline)
            {
                var read = channel.ReadMessages(64, 25);
                foreach (var msg in read.Messages)
                {
                    byte[] data = msg.Data;
                    if (data is null || data.Length < 5)
                        continue;

                    uint id = FrameId(data);
                    // Capture a reply on ANY new id — the ABS may respond outside 0x7E8-0x7EF.
                    if (id == requestId || baseline.Contains(id))
                        continue;

                    string desc = $"0x{id:X3}{ModuleName(id)} → {BitConverter.ToString(data, 4)}";
                    if (seen.Add(desc))
                        results.Add(desc);
                }
            }

            return results;
        }

        /// <summary>
        /// Listens for <paramref name="durationMs"/> ms and returns the latest payload seen for
        /// every distinct CAN id (id -> hex of the bytes after the 4-byte id).
        /// </summary>
        private static Dictionary<uint, string> Listen(J2534Channel channel, int durationMs)
        {
            var seen = new Dictionary<uint, string>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(durationMs);

            while (DateTime.UtcNow < deadline)
            {
                var result = channel.ReadMessages(64, 25);
                foreach (var msg in result.Messages)
                {
                    byte[] data = msg.Data;
                    if (data is null || data.Length < 4)
                        continue;

                    seen[FrameId(data)] = data.Length > 4 ? BitConverter.ToString(data, 4) : "(no data)";
                }
            }

            return seen;
        }

        private static string SampleIds(HashSet<uint> ids)
        {
            var sample = ids.OrderBy(x => x).Take(8).Select(x => $"0x{x:X3}");
            string text = string.Join(", ", sample);
            return ids.Count > 8 ? text + ", …" : text;
        }

        private static string ModuleName(uint id) => id switch
        {
            0x6F5 => " (ABS diagnostic)",
            0x7E8 => " (ECM)",
            0x7E9 => " (TCM)",
            0x7EA => " (ABS slot, unused here)",
            0x7EB => " (Body)",
            _ => "",
        };

        public (bool success, string errorMessage, AbsSniffResult result) SniffBus(
            int captureSeconds, IProgress<string>? progress)
        {
            try
            {
                EnsureDeviceFree();
                lock (_deviceLock)
                {
                    using var session = J2534Session.Open();
                    // CAN_ID_BOTH so we also capture 29-bit ids, in case the ABS uses extended addressing.
                    J2534Channel channel = session.OpenChannel(Protocol.CAN, Baud.CAN, ConnectFlag.CAN_ID_BOTH);
                    channel.StartMessageFilter(PassAllFilter()).ThrowIfError();

                    // Phase 1 — learn the periodic broadcast ids while the tester is idle.
                    progress?.Report("Learning bus baseline (5s) — keep the reference tester idle…");
                    var baseline = new HashSet<uint>();
                    DateTime b0 = DateTime.UtcNow;
                    while ((DateTime.UtcNow - b0).TotalSeconds < 5)
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
                    while ((DateTime.UtcNow - start).TotalSeconds < captureSeconds && frames.Count < 20000)
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

        /// <summary>
        /// Rejects an operation that would open the J2534 device while the telemetry monitor owns it.
        /// Only one channel set can be open at a time, and a second open would fail deeper down with a
        /// far less obvious error.
        /// </summary>
        private void EnsureDeviceFree()
        {
            if (_monitoring)
                throw new InvalidOperationException(
                    "ABS telemetry monitoring is running — stop it before running another ABS operation.");
        }

        public void Dispose() => StopTelemetryMonitor();
    }
}
