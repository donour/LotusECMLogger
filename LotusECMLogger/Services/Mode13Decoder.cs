namespace LotusECMLogger.Services
{
    /// <summary>
    /// Pure decode helpers for service 0x13 (the Lotus "read all DTCs" extension). A positive
    /// response is the SID 0x53 followed by a flat, contiguous list of big-endian two-byte
    /// codes: the current, confirmed and TPMS sets concatenated with no per-group count byte.
    /// The groups therefore cannot be told apart on the wire, and the same fault can appear
    /// more than once. See <c>T6-mode13-programming.md</c> §5.
    /// </summary>
    public static class Mode13Decoder
    {
        /// <summary>Positive response SID (0x13 | 0x40).</summary>
        public const byte PositiveSid = 0x53;

        // [00 00 07 E8] 0x53 — a response carrying no codes is exactly this long.
        private const int MinResponseLength = 5;

        /// <summary>
        /// Decodes a positive Mode 0x13 response buffer ([hdr4] 0x53 &lt;code pairs...&gt;).
        /// Repeated codes are collapsed, keeping first-seen order.
        /// </summary>
        /// <exception cref="ArgumentException">The buffer is too short, or is not a positive
        /// Mode 0x13 response.</exception>
        public static Mode13ReadResult Decode(byte[] response)
        {
            if (response.Length < MinResponseLength)
                throw new ArgumentException(
                    $"Mode 0x13 response too short ({response.Length} bytes).", nameof(response));
            if (response[4] != PositiveSid)
                throw new ArgumentException(
                    $"Not a positive Mode 0x13 response (SID 0x{response[4]:X2}).", nameof(response));

            var codes = new List<DiagnosticTroubleCode>();
            var seen = new HashSet<ushort>();
            int reported = 0;

            // Codes start immediately after the SID: unlike service 0x03 there is no count byte.
            // A trailing odd byte (a truncated response) has no pair and is ignored.
            for (int i = MinResponseLength; i + 1 < response.Length; i += 2)
            {
                // 0x0000 is "no code" — padding on firmwares that round the payload up.
                if (response[i] == 0x00 && response[i + 1] == 0x00)
                    continue;

                reported++;
                var dtc = DiagnosticTroubleCode.FromBytes(response[i], response[i + 1]);
                if (seen.Add(dtc.Raw))
                    codes.Add(dtc);
            }

            return new Mode13ReadResult
            {
                Codes = codes,
                ReportedCodeCount = reported,
                // From the SID on: the header is J2534 addressing, not part of the service payload.
                RawHex = string.Join(" ", response[4..].Select(b => b.ToString("X2"))),
            };
        }
    }
}
