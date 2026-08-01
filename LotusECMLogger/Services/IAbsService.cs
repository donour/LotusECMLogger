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
    /// Live internal state sampled from the module's RAM via ReadMemoryByAddress (guide §5):
    /// road-surface mu, EDC accumulators, valve positions and brake pressures.
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

    /// <summary>Progress update while a pump/valve actuation routine runs.</summary>
    public sealed record AbsRoutineProgress
    {
        /// <summary>Human-readable phase, e.g. "Bleed circulation (0x03)".</summary>
        public string Phase { get; init; } = "";

        /// <summary>Seconds elapsed / total for the current phase.</summary>
        public double ElapsedSeconds { get; init; }
        public double TotalSeconds { get; init; }

        /// <summary>Latest per-wheel status from RequestRoutineResults (0x33), and the monitored RAM values.</summary>
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];
    }

    /// <summary>Outcome of an actuation routine (or the full bleed sequence).</summary>
    public sealed record AbsRoutineResult
    {
        public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];

        /// <summary>True if every routine started, ran, and was stopped cleanly.</summary>
        public bool Completed { get; init; }

        public static readonly AbsRoutineResult Empty = new();
    }

    /// <summary>
    /// Whether the module's documented actuation preconditions are satisfied, judged from the
    /// passive telemetry broadcasts (guide §9). The module enforces these itself with NRC 0x22, but
    /// the guide is explicit that a client should not rely on that alone.
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

    /// <summary>
    /// Diagnostic client for the Bosch ESP8 ABS/ESP module, implementing the operations in
    /// <c>DIAGNOSTICS_PROGRAMMING_GUIDE.md</c>: passive telemetry (§4), live memory reads (§5),
    /// coding and identification reads (§6/§7), fault codes (§8), and pump/valve actuation (§9).
    /// No persistent-state write (variant recoding 0x3B, memory write 0x3D, DTC clear 0x14) is
    /// implemented — those are deliberately out of scope for this client.
    /// </summary>
    public interface IAbsService
    {
        // ── §7 / §6 — identification and coding ──────────────────────────────────────

        /// <summary>
        /// Reads the module's identification and configuration records: enters the module's
        /// diagnostic session, scans ReadEcuIdentification (1A 80-9F, labelled where known) and the
        /// ReadDataByLocalId coding records (21 00-FF), and returns those that hold data.
        /// Read-only; needs no SecurityAccess on this firmware (confirmed on a real car).
        /// </summary>
        (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress);

        // ── §5 — live internal state ─────────────────────────────────────────────────

        /// <summary>
        /// Reads the documented live-state RAM locations (mu estimate, EDC accumulators, valve
        /// positions, brake pressures, variant coding byte) via ReadMemoryByAddress (0x23), decoding
        /// each to its documented type. Unlocks first with SecurityAccess level 1, since the guide
        /// lists this service as requiring it; rows the module refuses are reported individually
        /// rather than failing the whole read. Read-only.
        /// </summary>
        (bool success, string errorMessage, AbsLiveStateResult result) ReadLiveState(IProgress<string>? progress);

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

        /// <summary>Fired when the telemetry monitor fails; monitoring is stopped first.</summary>
        event EventHandler<string>? TelemetryError;

        bool IsMonitoringTelemetry { get; }

        /// <summary>
        /// Starts passively decoding the module's 100 Hz broadcasts (0xA2/0xA4/0xA8) — wheel speeds,
        /// vehicle speed, brake switch and ESP/ABS status. Transmits nothing and needs no session, so
        /// it is safe while driving. Samples are logged to <paramref name="csvFilePath"/> when given.
        /// </summary>
        void StartTelemetryMonitor(string? csvFilePath);

        void StopTelemetryMonitor();

        /// <summary>
        /// Listens for <paramref name="durationMs"/> and returns one merged telemetry sample. Used for
        /// a one-shot reading and for the actuation precondition check.
        /// </summary>
        (bool success, string errorMessage, AbsTelemetrySample result) ReadTelemetrySnapshot(int durationMs);

        // ── §9 — pump / valve actuation ──────────────────────────────────────────────

        /// <summary>
        /// Checks the documented actuation preconditions (stationary, brake released, no active
        /// intervention) from passive telemetry. Read-only.
        /// </summary>
        (bool success, string errorMessage, AbsPreconditionCheck result) CheckActuationPreconditions();

        /// <summary>
        /// Runs one hydraulic actuation routine (StartRoutineByLocalId 0x31) for
        /// <paramref name="seconds"/>, polling per-wheel status (0x33) and the valve/pressure RAM
        /// locations while it runs, then stopping it (0x32) and returning to the default session.
        /// The stop and session restore always run, including on cancellation or error.
        ///
        /// This drives the pump motor and solenoid valves and moves brake fluid — stationary,
        /// engine-off use only. The caller is responsible for obtaining the operator's confirmation.
        /// </summary>
        (bool success, string errorMessage, AbsRoutineResult result) RunRoutine(
            byte routineType, int seconds, IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken);

        /// <summary>
        /// Runs the guide's three-phase brake-bleeding sequence: circulate (0x03), pressure hold
        /// (0x02), then quick valve cycle (0x01). Same safety envelope as <see cref="RunRoutine"/>.
        /// </summary>
        (bool success, string errorMessage, AbsRoutineResult result) RunBleedSequence(
            IProgress<AbsRoutineProgress>? progress, CancellationToken cancellationToken);

        // ── Addressing discovery tools ───────────────────────────────────────────────

        /// <summary>
        /// Sends harmless requests to candidate diagnostic ids and reports which responders answer.
        /// Used to discover the module's CAN addressing when a read times out. Changes no session or
        /// module state, so this is a pure reachability check.
        /// </summary>
        (bool success, string errorMessage, AbsProbeResult result) ProbeConnection();

        /// <summary>
        /// Passively monitors the CAN bus (transmits nothing) to discover the ABS's diagnostic
        /// addressing by watching an external reference tester talk to it: learns the periodic
        /// broadcast ids during a short idle baseline, then logs every frame on a NEW id for
        /// <paramref name="captureSeconds"/>. Captures both 11-bit and 29-bit ids.
        /// </summary>
        (bool success, string errorMessage, AbsSniffResult result) SniffBus(int captureSeconds, IProgress<string>? progress);
    }
}
