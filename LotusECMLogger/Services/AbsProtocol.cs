namespace LotusECMLogger.Services
{
    /// <summary>
    /// KWP2000 (ISO 14230) constants and the ESP8-specific tables used by the ABS diagnostic
    /// client. Everything here is transcribed from
    /// <c>DIAGNOSTICS_PROGRAMMING_GUIDE.md</c> (Bosch ESP8.1 BB68638 V0201 / A132J0314A),
    /// with the deviations found on a real car noted at each site.
    /// </summary>
    internal static class AbsProtocol
    {
        // ── Service ids ──────────────────────────────────────────────────────────────
        public const byte SidStartDiagnosticSession = 0x10;
        public const byte SidReadStatusOfDtc = 0x17;
        public const byte SidReadDtcByStatus = 0x18;
        public const byte SidReadEcuIdentification = 0x1A;
        public const byte SidReadDataByLocalId = 0x21;
        public const byte SidReadMemoryByAddress = 0x23;
        public const byte SidSecurityAccess = 0x27;
        public const byte SidStartRoutineByLocalId = 0x31;
        public const byte SidStopRoutineByLocalId = 0x32;
        public const byte SidRequestRoutineResults = 0x33;
        public const byte SidTesterPresent = 0x3E;

        /// <summary>A positive KWP response echoes the request SID with bit 6 set.</summary>
        public const byte PositiveResponseFlag = 0x40;
        public const byte NegativeResponseSid = 0x7F;

        // ── Sessions ─────────────────────────────────────────────────────────────────
        public const byte SessionDefault = 0x01;
        public const byte SessionProgramming = 0x02;
        public const byte SessionExtended = 0x03;

        /// <summary>
        /// Session byte the reference tester was observed using on the car (0x89), which the
        /// module accepts where the guide's 0x02 is refused. Tried first for read services.
        /// </summary>
        public const byte SessionTester = 0x89;

        // ── SecurityAccess ───────────────────────────────────────────────────────────
        public const byte SecurityRequestSeed = 0x01;
        public const byte SecuritySendKey = 0x02;
        public const int SeedLength = 4;

        /// <summary>TesterPresent sub-function that suppresses the positive response (less bus load).</summary>
        public const byte TesterPresentSuppressResponse = 0x80;

        /// <summary>Routine sub-function used by every 0x31/0x32/0x33 request in the guide.</summary>
        public const byte RoutineSubFunction = 0x01;

        // ── Negative response codes ──────────────────────────────────────────────────
        public const byte NrcServiceNotSupported = 0x11;
        public const byte NrcSubFunctionNotSupported = 0x12;
        public const byte NrcIncorrectMessageLength = 0x13;
        public const byte NrcConditionsNotCorrect = 0x22;
        public const byte NrcRequestOutOfRange = 0x31;
        public const byte NrcSecurityAccessDenied = 0x33;
        public const byte NrcResponsePending = 0x78;

        public static string NrcName(byte nrc) => nrc switch
        {
            0x10 => "generalReject",
            0x11 => "serviceNotSupported",
            0x12 => "subFunctionNotSupported",
            0x13 => "incorrectMessageLength",
            0x22 => "conditionsNotCorrect",
            0x24 => "requestSequenceError",
            0x31 => "requestOutOfRange",
            0x33 => "securityAccessDenied",
            0x34 => "requiredTimeDelayNotExpired",
            0x35 => "invalidKey",
            0x36 => "exceedNumberOfAttempts",
            0x78 => "responsePending",
            _ => "unknown",
        };

        /// <summary>
        /// Plain-language hint for the NRCs a user is most likely to hit, so the UI can explain a
        /// refusal instead of just printing a code.
        /// </summary>
        public static string NrcHint(byte nrc) => nrc switch
        {
            NrcConditionsNotCorrect =>
                "Preconditions not met — vehicle must be stationary, ignition ON with the engine OFF, "
                + "brake pedal released, and no ABS/ESP intervention active.",
            NrcSecurityAccessDenied => "The module wants SecurityAccess level 1 for this service.",
            NrcServiceNotSupported => "This firmware does not implement the service at all.",
            NrcSubFunctionNotSupported => "The service exists but not with this sub-function / record number.",
            NrcRequestOutOfRange => "Address, length, or identifier is outside the range the module allows.",
            0x36 => "Too many bad keys — power-cycle or restart the session before retrying.",
            0x34 => "Security lockout delay is still running; wait before retrying.",
            _ => "",
        };

        // ── SecurityAccess key derivation ────────────────────────────────────────────

        /// <summary>
        /// SecurityAccess level 1 substitution table (firmware flash 0xB8530): key[i] = SBOX[seed[i]].
        /// Verified properties: SBOX[0] = 0, full permutation, XOR-linear.
        /// Reference seed 11 22 33 44 → key D0 BD 6D 67.
        /// </summary>
        private static readonly byte[] SBox =
        [
            0x00, 0x1D, 0x3A, 0x27, 0x74, 0x69, 0x4E, 0x53, 0xE8, 0xF5, 0xD2, 0xCF, 0x9C, 0x81, 0xA6, 0xBB,
            0xCD, 0xD0, 0xF7, 0xEA, 0xB9, 0xA4, 0x83, 0x9E, 0x25, 0x38, 0x1F, 0x02, 0x51, 0x4C, 0x6B, 0x76,
            0x87, 0x9A, 0xBD, 0xA0, 0xF3, 0xEE, 0xC9, 0xD4, 0x6F, 0x72, 0x55, 0x48, 0x1B, 0x06, 0x21, 0x3C,
            0x4A, 0x57, 0x70, 0x6D, 0x3E, 0x23, 0x04, 0x19, 0xA2, 0xBF, 0x98, 0x85, 0xD6, 0xCB, 0xEC, 0xF1,
            0x13, 0x0E, 0x29, 0x34, 0x67, 0x7A, 0x5D, 0x40, 0xFB, 0xE6, 0xC1, 0xDC, 0x8F, 0x92, 0xB5, 0xA8,
            0xDE, 0xC3, 0xE4, 0xF9, 0xAA, 0xB7, 0x90, 0x8D, 0x36, 0x2B, 0x0C, 0x11, 0x42, 0x5F, 0x78, 0x65,
            0x94, 0x89, 0xAE, 0xB3, 0xE0, 0xFD, 0xDA, 0xC7, 0x7C, 0x61, 0x46, 0x5B, 0x08, 0x15, 0x32, 0x2F,
            0x59, 0x44, 0x63, 0x7E, 0x2D, 0x30, 0x17, 0x0A, 0xB1, 0xAC, 0x8B, 0x96, 0xC5, 0xD8, 0xFF, 0xE2,
            0x26, 0x3B, 0x1C, 0x01, 0x52, 0x4F, 0x68, 0x75, 0xCE, 0xD3, 0xF4, 0xE9, 0xBA, 0xA7, 0x80, 0x9D,
            0xEB, 0xF6, 0xD1, 0xCC, 0x9F, 0x82, 0xA5, 0xB8, 0x03, 0x1E, 0x39, 0x24, 0x77, 0x6A, 0x4D, 0x50,
            0xA1, 0xBC, 0x9B, 0x86, 0xD5, 0xC8, 0xEF, 0xF2, 0x49, 0x54, 0x73, 0x6E, 0x3D, 0x20, 0x07, 0x1A,
            0x6C, 0x71, 0x56, 0x4B, 0x18, 0x05, 0x22, 0x3F, 0x84, 0x99, 0xBE, 0xA3, 0xF0, 0xED, 0xCA, 0xD7,
            0x35, 0x28, 0x0F, 0x12, 0x41, 0x5C, 0x7B, 0x66, 0xDD, 0xC0, 0xE7, 0xFA, 0xA9, 0xB4, 0x93, 0x8E,
            0xF8, 0xE5, 0xC2, 0xDF, 0x8C, 0x91, 0xB6, 0xAB, 0x10, 0x0D, 0x2A, 0x37, 0x64, 0x79, 0x5E, 0x43,
            0xB2, 0xAF, 0x88, 0x95, 0xC6, 0xDB, 0xFC, 0xE1, 0x5A, 0x47, 0x60, 0x7D, 0x2E, 0x33, 0x14, 0x09,
            0x7F, 0x62, 0x45, 0x58, 0x0B, 0x16, 0x31, 0x2C, 0x97, 0x8A, 0xAD, 0xB0, 0xE3, 0xFE, 0xD9, 0xC4,
        ];

        /// <summary>Derives the SecurityAccess key from a seed: key[i] = SBOX[seed[i]].</summary>
        public static byte[] ComputeKey(byte[] seed)
        {
            byte[] key = new byte[seed.Length];
            for (int i = 0; i < seed.Length; i++)
                key[i] = SBox[seed[i]];
            return key;
        }

        // ── ReadMemoryByAddress (0x23) address-and-length format byte ────────────────

        /// <summary>
        /// Candidate addressAndLength format bytes for SID 0x23, tried in order. The guide documents
        /// 0x34 but flags it ⚠ VERIFY, because KWP2000 implementations disagree on how the nibbles
        /// encode the address/length byte counts (the UDS convention would make 0x14 correct for a
        /// 4-byte address + 1-byte length). <see cref="AbsKwpSession.ReadMemory"/> tries each in turn
        /// on NRC 0x13/0x31 and remembers the one the module accepts.
        /// </summary>
        public static readonly byte[] AddressAndLengthCandidates = [0x34, 0x14, 0x41];

        // ── Live internal state map (guide §5) ───────────────────────────────────────

        public enum AbsValueFormat
        {
            /// <summary>Unsigned byte, shown as decimal + hex.</summary>
            Byte,
            /// <summary>Signed 16-bit big-endian.</summary>
            Int16,
            /// <summary>Signed 16-bit in Q9 fixed point: value / 512.</summary>
            MuQ9,
            /// <summary>Solenoid valve position byte (3 = release, 0x11 = apply, 0x13 = apply-init, 0x17 = hold).</summary>
            ValvePosition,
            /// <summary>Signed 16-bit brake pressure; below ~99 counts as unpressurized.</summary>
            Pressure,
            /// <summary>Vehicle variant/coding byte, additionally decoded bit-by-bit.</summary>
            VariantCoding,
        }

        /// <summary>One entry of the documented live-state address map.</summary>
        public sealed record AbsLiveStateEntry(
            string Name, uint Address, byte Length, AbsValueFormat Format, string Note = "");

        /// <summary>
        /// The RAM locations documented in guide §5. Addresses are used exactly as published (note
        /// that the guide mixes the 0x0040_xxxx and 0x4000_xxxx forms); the module NRCs anything it
        /// will not read, and the failing rows are reported rather than hidden.
        /// </summary>
        public static readonly AbsLiveStateEntry[] LiveStateMap =
        [
            new("Variant coding byte", 0x400061C2, 1, AbsValueFormat.VariantCoding, "Vehicle variant/coding"),
            new("Road-surface mu (μ)", 0x00404320, 2, AbsValueFormat.MuQ9, "Q9 fixed point (÷512)"),
            new("EDC accumulator, left", 0x00404E8E, 2, AbsValueFormat.Int16, "Brake torque-vectoring pressure"),
            new("EDC accumulator, right", 0x00404E90, 2, AbsValueFormat.Int16, "Brake torque-vectoring pressure"),
            new("EDC secondary (left)", 0x00404E92, 2, AbsValueFormat.Int16, "Secondary accumulator"),
            new("Front valve position", 0x40000C44, 1, AbsValueFormat.ValvePosition),
            new("Rear valve position", 0x40000C50, 1, AbsValueFormat.ValvePosition),
            new("Brake pressure ch1", 0x40000C5A, 2, AbsValueFormat.Pressure, "Channel→wheel mapping unverified"),
            new("Brake pressure ch2", 0x40000C5C, 2, AbsValueFormat.Pressure, "Channel→wheel mapping unverified"),
            new("Brake pressure ch3", 0x40000C60, 2, AbsValueFormat.Pressure, "Channel→wheel mapping unverified"),
            new("Brake pressure ch4", 0x40000CE8, 2, AbsValueFormat.Pressure, "Channel→wheel mapping unverified"),
        ];

        /// <summary>Addresses polled while an actuation routine runs (guide §9, "Monitoring During Actuation").</summary>
        public static readonly AbsLiveStateEntry[] ActuationMonitorMap =
        [
            new("Front valve", 0x40000C44, 1, AbsValueFormat.ValvePosition),
            new("Rear valve", 0x40000C50, 1, AbsValueFormat.ValvePosition),
            new("Pressure ch1", 0x40000C5A, 2, AbsValueFormat.Pressure),
            new("Pressure ch2", 0x40000C5C, 2, AbsValueFormat.Pressure),
        ];

        /// <summary>Reads a big-endian signed 16-bit value from the front of <paramref name="data"/>.</summary>
        public static int ToInt16(byte[] data)
        {
            int v = (data[0] << 8) | data[1];
            return (v & 0x8000) != 0 ? v - 0x10000 : v;
        }

        public static string ValvePositionName(byte value) => value switch
        {
            0x03 => "release (open)",
            0x11 => "apply",
            0x13 => "apply-init",
            0x17 => "hold (closed)",
            _ => "unknown",
        };

        /// <summary>Renders a raw read from the live-state map according to its documented format.</summary>
        public static string FormatLiveValue(AbsLiveStateEntry entry, byte[] data)
        {
            if (data.Length < entry.Length)
                return $"short response ({data.Length} bytes)";

            switch (entry.Format)
            {
                case AbsValueFormat.Byte:
                    return $"{data[0]} (0x{data[0]:X2})";

                case AbsValueFormat.VariantCoding:
                    return $"0x{data[0]:X2} — {DescribeVariantCoding(data[0])}";

                case AbsValueFormat.ValvePosition:
                    return $"0x{data[0]:X2} — {ValvePositionName(data[0])}";

                case AbsValueFormat.MuQ9:
                    return $"{ToInt16(data) / 512.0:F3} μ (raw {ToInt16(data)})";

                case AbsValueFormat.Pressure:
                    int p = ToInt16(data);
                    return $"{p}{(p < 99 ? " (≈unpressurized)" : "")}";

                default:
                    return ToInt16(data).ToString();
            }
        }

        /// <summary>
        /// Bit-by-bit reading of the variant coding byte per <c>VARIANT_CODING.md</c>. Those
        /// assignments are INFERRED from Bosch conventions and Evora variant differences, not
        /// confirmed from firmware, so the raw byte is always shown alongside.
        /// </summary>
        public static string DescribeVariantCoding(byte coding)
        {
            string engine = (coding & 0x04) != 0 ? "supercharged" : "naturally aspirated";
            string market = (coding & 0x20) != 0 ? "US" : "ROW";
            return $"axle/brake {coding & 0x03}, {engine}, tyre/wheel {(coding >> 3) & 0x03}, "
                 + $"market {market}, ESP cal {(coding >> 6) & 0x03} (inferred)";
        }

        // ── DTC formatting (guide §8) ────────────────────────────────────────────────

        /// <summary>
        /// Renders a DTC as the raw 16-bit value the module reports.
        ///
        /// No P/C/B/U letter is derived: the usual SAE J2012 rule (top two bits select the letter)
        /// makes the observed code 0xC150 a "U" network code, which contradicts this being a chassis
        /// module — so the encoding this firmware uses is not the standard one and any letter shown
        /// would be a guess. Raw values match how the Bosch/Lotus material quotes these codes.
        /// </summary>
        public static string FormatDtcCode(int code) => $"DTC 0x{code:X4}";

        /// <summary>Expands the KWP2000 DTC status byte into its named bits (guide §8).</summary>
        public static string DescribeDtcStatus(byte status)
        {
            string[] names =
            [
                "testFailed", "testFailedThisCycle", "pendingDTC", "confirmedDTC",
                "testNotCompletedSinceClear", "testFailedSinceClear", "testNotCompletedThisCycle",
                "warningIndicatorRequested",
            ];

            var set = new List<string>();
            for (int bit = 0; bit < names.Length; bit++)
                if ((status & (1 << bit)) != 0)
                    set.Add(names[bit]);

            return set.Count == 0 ? "no status bits set" : string.Join(", ", set);
        }

        // ── Actuation routines (guide §9) ────────────────────────────────────────────

        /// <summary>One pump/valve actuation routine from the firmware's actuator dispatcher.</summary>
        public sealed record AbsRoutine(byte Type, string Name, string Description, int DefaultSeconds);

        /// <summary>
        /// The routines mapped from <c>actuator_routine_dispatcher</c> @ 0xCC94. All drive the pump
        /// motor and solenoid valves, so all require a stationary vehicle with the engine off.
        /// </summary>
        public static readonly AbsRoutine[] Routines =
        [
            new(0x01, "Quick valve cycle", "Valves cycling, pump on — dislodges air, verifies valve clicks.", 5),
            new(0x02, "Pressure hold test", "Valves closed (0x17), pump on — leak test / pedal-feel check.", 10),
            new(0x03, "Bleed circulation", "Valves open (0x03), pump on — the main fluid-bleeding routine.", 30),
            new(0x05, "Per-wheel cycle", "Per-wheel valve cycling — isolates a single sticking wheel.", 10),
            new(0x10, "Full system test", "All wheels in sequence — comprehensive hydraulic test.", 30),
        ];

        public static AbsRoutine? FindRoutine(byte type) =>
            Array.Find(Routines, r => r.Type == type);

        /// <summary>Wheel order of the four status bytes returned by RequestRoutineResults (0x33).</summary>
        public static readonly string[] RoutineWheelNames = ["LF", "RF", "LR", "RR"];

        /// <summary>Decodes one per-wheel status byte from a routine poll: 0xFF = inactive, 0x00 = active/OK.</summary>
        public static string RoutineWheelStatus(byte status) => status switch
        {
            0x00 => "active/OK",
            0xFF => "inactive",
            _ => $"0x{status:X2}",
        };

        /// <summary>The 3-phase bleeding sequence from guide §9, with its published durations.</summary>
        public static readonly (byte Type, int Seconds)[] BleedSequence =
        [
            (0x03, 30), // circulate — valves open, pump on
            (0x02, 10), // pressure hold — valves closed
            (0x01, 5),  // quick cycle — dislodge remaining bubbles
        ];
    }
}
