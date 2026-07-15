namespace LotusECMLogger.Services
{
    /// <summary>
    /// Identification data read from the Bosch ESP8 ABS/ESP module (KWP2000 service 0x1A,
    /// record 0x87).
    /// </summary>
    public sealed record AbsModuleInfo
    {
        /// <summary>
        /// Raw bytes of the identification record, with the echoed record id (0x87) stripped.
        /// </summary>
        public byte[] RawIdentification { get; init; } = [];

        /// <summary>
        /// Field/value rows ready for display. The internal layout of record 0x87 is
        /// firmware-specific and not yet verified against a real module, so these are a
        /// best-effort ASCII extraction plus the raw bytes rather than a fixed-offset parse.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Fields { get; init; } = [];

        public static readonly AbsModuleInfo Empty = new();

        /// <summary>
        /// Builds a display model from the raw identification bytes: one row per readable
        /// ASCII run (likely the part number and software id strings), then the byte count
        /// and a hex dump so nothing is hidden while the exact layout is still being verified.
        /// </summary>
        public static AbsModuleInfo FromIdentification(byte[] identification)
        {
            var fields = new List<KeyValuePair<string, string>>();

            foreach (string token in ExtractAsciiTokens(identification, minLength: 4))
                fields.Add(new KeyValuePair<string, string>("Identification", token));

            fields.Add(new KeyValuePair<string, string>("Length", $"{identification.Length} bytes"));
            fields.Add(new KeyValuePair<string, string>("Raw (hex)", BitConverter.ToString(identification)));

            return new AbsModuleInfo
            {
                RawIdentification = identification,
                Fields = fields,
            };
        }

        // Yields runs of printable ASCII (0x20-0x7E) at least <paramref name="minLength"/> long.
        private static IEnumerable<string> ExtractAsciiTokens(byte[] data, int minLength)
        {
            var current = new System.Text.StringBuilder();
            foreach (byte b in data)
            {
                if (b >= 0x20 && b < 0x7F)
                {
                    current.Append((char)b);
                    continue;
                }

                if (current.Length >= minLength)
                    yield return current.ToString();
                current.Clear();
            }

            if (current.Length >= minLength)
                yield return current.ToString();
        }
    }

    /// <summary>
    /// Result of a connection probe: one report row per addressing attempt / responder,
    /// used to discover which CAN ids the ABS actually answers on.
    /// </summary>
    public sealed record AbsProbeResult
    {
        public IReadOnlyList<KeyValuePair<string, string>> Rows { get; init; } = [];

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
    /// Diagnostic trouble codes read from the ABS via KWP2000 ReadDtcByStatus (0x18). The exact
    /// DTC encoding is Bosch/Lotus-specific and not yet mapped to display strings, so codes are
    /// shown as raw 16-bit values plus status.
    /// </summary>
    public sealed record AbsDtcResult
    {
        /// <summary>Field/value rows ready for display (DTC count, one row per code, raw bytes).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Rows { get; init; } = [];

        /// <summary>Raw KWP payload after the 0x58 response SID, for reference.</summary>
        public byte[] RawResponse { get; init; } = [];

        public static readonly AbsDtcResult Empty = new();

        /// <summary>
        /// Parses a ReadDtcByStatus (0x58) response payload: a one-byte DTC count followed by
        /// count × (16-bit code, 1-byte status). Layout confirmed against a reference-tester trace
        /// (e.g. <c>01 C1 50 A0</c> = one DTC 0xC150 status 0xA0; <c>00</c> = none).
        /// </summary>
        public static AbsDtcResult FromResponse(byte[] payload)
        {
            var rows = new List<KeyValuePair<string, string>>();

            var codes = new List<string>();
            for (int i = 1; i + 3 <= payload.Length; i += 3)
            {
                int code = (payload[i] << 8) | payload[i + 1];
                byte status = payload[i + 2];
                codes.Add($"0x{code:X4}  status 0x{status:X2}");
            }

            int reported = payload.Length > 0 ? payload[0] : 0;
            if (codes.Count == 0)
            {
                rows.Add(new KeyValuePair<string, string>("DTCs", reported == 0 ? "none stored" : $"{reported} reported (unparsed)"));
            }
            else
            {
                rows.Add(new KeyValuePair<string, string>("DTC count", reported.ToString()));
                foreach (string c in codes)
                    rows.Add(new KeyValuePair<string, string>("DTC", c));
            }

            rows.Add(new KeyValuePair<string, string>("Raw (hex)", BitConverter.ToString(payload)));
            return new AbsDtcResult { Rows = rows, RawResponse = payload };
        }
    }

    public interface IAbsService
    {
        /// <summary>
        /// Reads the ABS/ESP module's information: it enters the module's diagnostic session
        /// (10 89) and scans the ReadEcuIdentification records (1A 80-9F, labelled where known)
        /// and the ReadDataByLocalId coding records (21 00-FF), returning the ones that hold data.
        /// Read-only and needs NO SecurityAccess — confirmed on a real car.
        /// </summary>
        /// <returns>
        /// A success flag, an error message when unsuccessful, and the collected records on success.
        /// </returns>
        (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo(IProgress<string>? progress);

        /// <summary>
        /// Reads diagnostic trouble codes from the ABS (KWP2000 ReadDtcByStatus, request
        /// <c>18 00 FF 00</c> to id 0x6F4, response on 0x6F5). No SecurityAccess or session change
        /// is needed — this is the exact read the reference tester performs. Read-only.
        /// </summary>
        (bool success, string errorMessage, AbsDtcResult result) ReadDtcs();

        /// <summary>
        /// Sends a harmless TesterPresent (KWP 0x3E 0x00) to the physical (0x7E2) and functional
        /// (0x7DF) request ids and reports which diagnostic responders (0x7E8-0x7EF) answer. Used
        /// to discover the ABS's real CAN addressing when a read times out. TesterPresent changes
        /// no session or module state, so this is a pure, safe reachability check.
        /// </summary>
        (bool success, string errorMessage, AbsProbeResult result) ProbeConnection();

        /// <summary>
        /// Passively monitors the CAN bus (read-only — transmits nothing) to discover the ABS's
        /// diagnostic addressing by watching an external reference tester talk to it. Learns the
        /// periodic broadcast ids during a short idle baseline, then for <paramref name="captureSeconds"/>
        /// seconds logs every frame on a NEW id — the tester↔ABS diagnostic exchange stands out as
        /// ids that only appear while the tester is active. Captures both 11-bit and 29-bit ids.
        /// </summary>
        (bool success, string errorMessage, AbsSniffResult result) SniffBus(int captureSeconds, IProgress<string>? progress);
    }
}
