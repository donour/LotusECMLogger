namespace LotusECMLogger.Services
{
    /// <summary>
    /// One row of an ABS diagnostic report: a label, the decoded value, and supporting detail
    /// (address, raw bytes, or the reason a read failed).
    /// </summary>
    public sealed record AbsReportRow(string Field, string Value, string Detail = "");

    /// <summary>
    /// Identification and configuration records read from the Bosch ESP8 ABS/ESP module
    /// (KWP2000 ReadEcuIdentification 0x1A and ReadDataByLocalId 0x21).
    /// </summary>
    public sealed record AbsModuleInfo
    {
        public IReadOnlyList<AbsReportRow> Fields { get; init; } = [];

        public static readonly AbsModuleInfo Empty = new();
    }

    /// <summary>
    /// Verified diagnostic live-data record04, including raw replies and bounded OEM conversions.
    /// This is not an arbitrary RAM read or a view of the later learned controller speed.
    /// </summary>
    public sealed record AbsLiveStateResult
    {
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];

        public static readonly AbsLiveStateResult Empty = new();
    }

    /// <summary>
    /// Result of a connection probe: one report row per addressing attempt / responder,
    /// used to discover which CAN ids the ABS actually answers on.
    /// </summary>
    public sealed record AbsProbeResult
    {
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];

        public static readonly AbsProbeResult Empty = new();
    }

    /// <summary>
    /// Result of a passive bus sniff used to discover the ABS's diagnostic addressing by watching
    /// an external reference tester.
    /// </summary>
    public sealed record AbsSniffResult
    {
        /// <summary>Number of periodic broadcast ids learned during the idle baseline phase.</summary>
        public int BaselineIdCount { get; init; }

        /// <summary>Distinct ids seen during capture that were NOT in the baseline, with frame counts.</summary>
        public IReadOnlyList<string> NewIds { get; init; } = [];

        /// <summary>Chronological log of the non-baseline frames (elapsed ms, id, data bytes).</summary>
        public IReadOnlyList<string> Frames { get; init; } = [];

        public static readonly AbsSniffResult Empty = new();
    }

    /// <summary>
    /// Diagnostic trouble codes read from the ABS via KWP2000 ReadDtcByStatus (0x18), each with its
    /// status byte expanded into the named KWP2000 status bits.
    /// </summary>
    public sealed record AbsDtcResult
    {
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];

        /// <summary>The stored codes, for callers that want the values rather than the display rows.</summary>
        public IReadOnlyList<(int Code, byte Status)> Codes { get; init; } = [];

        /// <summary>Raw KWP payload after the 0x58 response SID, for reference.</summary>
        public byte[] RawResponse { get; init; } = [];

        public static readonly AbsDtcResult Empty = new();

        /// <summary>
        /// Parses a ReadDtcByStatus (0x58) response payload: a one-byte DTC count followed by one
        /// entry per code. Entries are 3 bytes (16-bit code + status) on this module — confirmed
        /// against a reference-tester trace (<c>01 C1 50 A0</c> = one DTC 0xC150, status 0xA0;
        /// <c>00</c> = none) — where the guide describes the KWP2000-standard 4-byte entry. Both
        /// layouts are accepted, picked by whichever matches the payload length.
        /// </summary>
        public static AbsDtcResult FromResponse(byte[] payload)
        {
            var rows = new List<AbsReportRow>();
            int reported = payload.Length > 0 ? payload[0] : 0;

            // Entry size is chosen by which layout exactly accounts for the payload; the observed
            // 3-byte form wins ties, since that is what this firmware was seen to send.
            int entrySize = reported > 0 && 1 + (reported * 4) == payload.Length ? 4 : 3;

            var codes = new List<(int, byte)>();
            for (int i = 1; i + entrySize <= payload.Length; i += entrySize)
            {
                // A 4-byte entry carries a 3-byte code; only its low 16 bits are the DTC number,
                // matching how the 3-byte form encodes it.
                int code = entrySize == 3
                    ? (payload[i] << 8) | payload[i + 1]
                    : (payload[i + 1] << 8) | payload[i + 2];
                byte status = payload[i + entrySize - 1];
                codes.Add((code, status));
            }

            if (codes.Count == 0)
            {
                rows.Add(new AbsReportRow("DTCs", reported == 0 ? "none stored" : $"{reported} reported",
                    reported == 0 ? "" : "count did not match any known entry layout"));
            }
            else
            {
                rows.Add(new AbsReportRow("DTC count", reported.ToString(),
                    $"{entrySize}-byte entries; codes shown raw (this firmware's letter convention is unverified)"));
                foreach (var (code, status) in codes)
                    rows.Add(new AbsReportRow(AbsProtocol.FormatDtcCode(code),
                        $"status 0x{status:X2}", AbsProtocol.DescribeDtcStatus(status)));
            }

            rows.Add(new AbsReportRow("Raw response", BitConverter.ToString(payload)));

            return new AbsDtcResult { Rows = rows, Codes = codes, RawResponse = payload };
        }
    }

    /// <summary>Progress update while a pump test runs.</summary>
    public sealed record AbsRoutineProgress
    {
        /// <summary>Human-readable phase, e.g. "Sending pump OFF".</summary>
        public string Phase { get; init; } = "";

        /// <summary>Seconds elapsed / total for the current phase.</summary>
        public double ElapsedSeconds { get; init; }
        public double TotalSeconds { get; init; }

        /// <summary>Optional report rows for the current phase.</summary>
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];
    }

    /// <summary>Outcome of an actuation request, including independent cleanup acknowledgements.</summary>
    public sealed record AbsRoutineResult
    {
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];
        public IReadOnlyList<AbsDiagnosticExchange> Exchanges { get; init; } = [];
        public bool Cancelled { get; init; }
        public bool ActivationAttempted { get; init; }
        /// <summary>ON may have been accepted, requiring cleanup. False after a correlated refusal.</summary>
        public bool CleanupRequired { get; init; }
        /// <summary>The OFF command's executor reported completion; this is not physical motor feedback.</summary>
        public bool OffCommandCompleted { get; init; }
        /// <summary>Stop was acknowledged with matching replies; deferred cleanup may still be pending.</summary>
        public bool StopConfirmed { get; init; }
        public bool SessionRestored { get; init; }

        /// <summary>The requested test and all cleanup commands completed without reported errors;
        /// this is not confirmation of physical motor state or deferred stop processing.</summary>
        public bool Completed { get; init; }

        public static readonly AbsRoutineResult Empty = new();
    }

    /// <summary>
    /// Legacy actuation result shape. Current broadcast interpretation cannot establish these
    /// preconditions, so the implementation always returns an unavailable result.
    /// </summary>
    public sealed record AbsPreconditionCheck
    {
        public bool Stationary { get; init; }
        public bool BrakeReleased { get; init; }
        public bool NoIntervention { get; init; }

        /// <summary>False when no telemetry arrived at all — the bus is asleep or disconnected.</summary>
        public bool TelemetrySeen { get; init; }

        public bool AllSatisfied => TelemetrySeen && Stationary && BrakeReleased && NoIntervention;

        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];

        /// <summary>One-line summary of what is blocking actuation, or an empty string when clear.</summary>
        public string BlockingReason
        {
            get
            {
                if (!TelemetrySeen)
                    return "No ABS telemetry on the bus — is the ignition on?";

                var problems = new List<string>();
                if (!Stationary) problems.Add("vehicle is moving");
                if (!BrakeReleased) problems.Add("brake pedal is pressed");
                if (!NoIntervention) problems.Add("ABS/ESP intervention is active");
                return string.Join("; ", problems);
            }
        }
    }

    /// <summary>Primary ABS diagnostic reads, raw captures and provisional passive broadcasts.
    /// Includes a bounded OEM pump test and the separately guarded ABS firmware programming flow.
    /// </summary>
    public interface IAbsService
    {
        // ── §7 / §6 — identification and coding ──────────────────────────────────────

        /// <summary>Legacy display adapter for the bounded baseline read.</summary>
        (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress);

        /// <summary>Requests session 89, five known 1A records, coding 2101, Process 21BF and live 2104.
        /// Preserves complete replies and individual failures; never scans or unlocks.</summary>
        (bool success, string errorMessage, AbsDiagnosticBaseline result) ReadBaseline(IProgress<string>? progress);

        /// <summary>Captures a baseline and displays its diagnostic04 sample with firmware-reference gating.</summary>
        (bool success, string errorMessage, AbsLiveStateResult result) ReadLiveState(IProgress<string>? progress);

        /// <summary>Raised on the worker thread after every poll has been appended to the capture.</summary>
        event EventHandler<AbsDiagnosticSample>? DiagnosticSampleReceived;
        /// <summary>Raised after the device closes. Empty text means normal completion; otherwise describes failure.</summary>
        event EventHandler<string>? DiagnosticMonitorError;
        bool IsMonitoringDiagnostics { get; }
        /// <summary>Captures a fresh baseline, then polls 2104 and flushes each response/failure to a new JSONL file.</summary>
        void StartDiagnosticMonitor(string captureFilePath, int intervalMs, string notes);
        void StopDiagnosticMonitor();

        // ── §8 — fault codes ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads diagnostic trouble codes (ReadDtcByStatus, <c>18 00 FF 00</c>) and follows up on each
        /// stored code with ReadStatusOfDtc (0x17). No SecurityAccess or session change is needed.
        /// Read-only — codes are never cleared.
        /// </summary>
        (bool success, string errorMessage, AbsDtcResult result) ReadDtcs();

        // ── §4 — passive telemetry ───────────────────────────────────────────────────

        /// <summary>
        /// Fired (off the UI thread) for each decoded set of ABS broadcast frames while the telemetry
        /// monitor is running.
        /// </summary>
        event EventHandler<AbsTelemetrySample>? TelemetryReceived;

        /// <summary>Fired after the monitor closes. Empty text means normal completion; otherwise describes failure.</summary>
        event EventHandler<string>? TelemetryError;

        bool IsMonitoringTelemetry { get; }

        /// <summary>
        /// Captures broadcasts 0xA2/0xA4/0xA8 with provisional raw-count/status decoding.
        /// Physical scales and packing are not validated. Transmits nothing. Samples are logged to <paramref name="csvFilePath"/> when given.
        /// </summary>
        void StartTelemetryMonitor(string? csvFilePath);

        void StopTelemetryMonitor();

        /// <summary>
        /// Listens for <paramref name="durationMs"/> and returns one merged provisional telemetry sample.
        /// </summary>
        (bool success, string errorMessage, AbsTelemetrySample result) ReadTelemetrySnapshot(int durationMs);

        /// <summary>Runs the verified OEM MRA pump command for a requested 1–5 seconds, then sends
        /// explicit OFF/stop commands. Operator confirmation is not a measured precondition.
        /// Raw exchanges are journaled to a new file, including cleanup after cancellation.</summary>
        (bool success, string errorMessage, AbsRoutineResult result) RunPumpCycle(int seconds,
            bool operatorConfirmed, string captureFilePath, IProgress<AbsRoutineProgress>? progress,
            CancellationToken cancellationToken);

        // Legacy generic actuation APIs remain unavailable; use the narrow pump test above.
        (bool success, string errorMessage, AbsPreconditionCheck result) CheckActuationPreconditions();
        (bool success, string errorMessage, AbsRoutineResult result) RunRoutine(
            byte routineType, int seconds, IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken);
        (bool success, string errorMessage, AbsRoutineResult result) RunBleedSequence(
            IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken);

        // ── Connection and passive observation tools ─────────────────────────────────

        /// <summary>
        /// Sends TesterPresent (3E) and identification read (1A85) on the known 6F4/6F5 address pair.
        /// Reports raw replies and failures. Does not scan addresses, unlock or change sessions.
        /// </summary>
        (bool success, string errorMessage, AbsProbeResult result) ProbeConnection();

        /// <summary>
        /// Passively monitors the CAN bus (transmits nothing) to discover the ABS's diagnostic
        /// addressing by watching an external reference tester talk to it: learns the periodic
        /// broadcast ids during a short idle baseline, then logs every frame on a NEW id for
        /// <paramref name="captureSeconds"/>. Captures both 11-bit and 29-bit ids.
        /// </summary>
        (bool success, string errorMessage, AbsSniffResult result) SniffBus(int captureSeconds, IProgress<string>? progress);

        /// <summary>Flashes a strict Intel HEX image through the recovered ABS bootloader flow.</summary>
        (bool success, string errorMessage, AbsFlashResult result) FlashFirmware(
            string firmwarePath, AbsFlashOptions options, IProgress<AbsFlashProgress>? progress,
            CancellationToken cancellationToken);
    }
}
