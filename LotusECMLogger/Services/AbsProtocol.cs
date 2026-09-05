using System.Numerics;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// KWP2000 (ISO 14230) constants and the ESP8-specific tables used by the ABS diagnostic
    /// client. Application session/security values follow the recovered OEM client and verified
    /// BB68638 V0201 image. Unverified hydraulic routine mappings are not provided.
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
        public const byte SessionDefault = 0x81;
        public const byte SessionProgramming = 0x85;
        public const byte SessionExtended = 0x86;

        /// <summary>
        /// Session byte the reference tester was observed using on the car (0x89), which the
        /// module accepts where the guide's 0x02 is refused. Tried first for read services.
        /// </summary>
        public const byte SessionTester = 0x89;

        // ── SecurityAccess ───────────────────────────────────────────────────────────
        public const byte SecurityRequestSeed = 0x01;
        public const byte SecuritySendKey = 0x02;
        public const int SeedLength = 2;

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
                "The module refused this request in its current state; the response does not identify which condition failed.",
            NrcSecurityAccessDenied => "Security access was refused; ordinary baseline and live-data reads do not attempt an unlock.",
            NrcServiceNotSupported => "This firmware does not implement the service at all.",
            NrcSubFunctionNotSupported => "The service exists but not with this sub-function / record number.",
            NrcRequestOutOfRange => "Address, length, or identifier is outside the range the module allows.",
            0x36 => "The module reports too many attempts; no automatic unlock retry is performed.",
            0x34 => "Security lockout delay is still running; wait before retrying.",
            _ => "",
        };

        /// <summary>Application key only: big-endian two-byte seed XOR 0x5220. This is not bootloader security.</summary>
        public static byte[] ComputeKey(byte[] seed)
        {
            ArgumentNullException.ThrowIfNull(seed);
            if (seed.Length != 2)
                throw new ArgumentException("Application security requires exactly two seed bytes.", nameof(seed));
            return [(byte)(seed[0] ^ 0x52), (byte)(seed[1] ^ 0x20)];
        }

        /// <summary>
        /// Bootloader-only four-byte key transform recovered from the OEM ABS client. This must
        /// never be substituted for <see cref="ComputeKey(byte[])"/>; the two exchanges use
        /// different seed widths and security levels.
        /// </summary>
        public static byte[] ComputeBootloaderKey(byte[] seed)
        {
            ArgumentNullException.ThrowIfNull(seed);
            if (seed.Length != 4)
                throw new ArgumentException("ABS bootloader security requires exactly four seed bytes.", nameof(seed));
            uint value = ((uint)seed[0] << 24) | ((uint)seed[1] << 16) | ((uint)seed[2] << 8) | seed[3];
            if (value == 0) return [0, 0, 0, 0];
            int rotate = (int)(((value & 0x20) >> 2) + ((value & 0x02) << 1)
                + ((value & 0x1000) >> 11) + ((value & 0x40000000) >> 30));
            uint rotated = (value & 0x00200000) != 0
                ? BitOperations.RotateLeft(value, rotate)
                : BitOperations.RotateRight(value, rotate);
            int selector = (int)(((value & 0x4000) >> 13) + ((value & 0x04000000) >> 26));
            uint key = selector switch
            {
                0 => rotated | value,
                1 => rotated & value,
                2 => rotated ^ value,
                3 => rotated,
                _ => throw new InvalidOperationException("Invalid bootloader key selector."),
            };
            return [(byte)(key >> 24), (byte)(key >> 16), (byte)(key >> 8), (byte)key];
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

    }
}
