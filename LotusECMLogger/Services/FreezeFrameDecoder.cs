using System.Globalization;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Pure decode helpers for OBD-II service 0x02 (freeze frame) responses. A Mode 02
    /// response is a Mode 01 response with one extra frame-number byte after the PID
    /// ([hdr4] 0x42 &lt;PID&gt; &lt;frame&gt; &lt;data...&gt;), so per-PID decoding rewrites the
    /// response into a synthetic Mode 01 buffer and reuses the Mode 01 parser.
    /// </summary>
    public static class FreezeFrameDecoder
    {
        // [00 00 07 E8] SID PID frame — anything shorter cannot carry data.
        private const int MinResponseLength = 7;
        private const byte PositiveSid = 0x42;

        /// <summary>
        /// Rewrites a positive Mode 02 response into the equivalent Mode 01 buffer:
        /// SID 0x42 becomes 0x41 and the frame-number byte is removed.
        /// </summary>
        /// <exception cref="ArgumentException">The buffer is too short or not a positive
        /// Mode 02 response.</exception>
        public static byte[] NormalizeToMode01(byte[] mode02Response)
        {
            if (mode02Response.Length < MinResponseLength)
                throw new ArgumentException(
                    $"Freeze frame response too short ({mode02Response.Length} bytes).", nameof(mode02Response));
            if (mode02Response[4] != PositiveSid)
                throw new ArgumentException(
                    $"Not a positive freeze frame response (SID 0x{mode02Response[4]:X2}).", nameof(mode02Response));

            var normalized = new byte[mode02Response.Length - 1];
            Array.Copy(mode02Response, normalized, 6); // header, SID, PID
            normalized[4] = 0x41;
            Array.Copy(mode02Response, 7, normalized, 6, mode02Response.Length - 7);
            return normalized;
        }

        /// <summary>
        /// Decodes one single-PID Mode 02 response through the Mode 01 parser. A PID the
        /// parser does not know yields a single undecoded entry carrying the raw bytes.
        /// </summary>
        public static IReadOnlyList<FreezeFrameEntry> DecodePidResponse(byte[] mode02Response)
        {
            byte[] normalized = NormalizeToMode01(mode02Response);
            byte pid = mode02Response[5];
            string rawHex = ToHex(mode02Response, 7);

            var readings = LiveDataReading.ParseCanResponse(normalized);
            if (readings.Count == 0)
            {
                return
                [
                    new FreezeFrameEntry
                    {
                        Name = $"PID 0x{pid:X2}",
                        Value = null,
                        RawHex = rawHex,
                        IsDecoded = false,
                    }
                ];
            }

            return readings
                .Select(r => new FreezeFrameEntry
                {
                    Name = r.name,
                    Value = r.value_f.ToString("0.##", CultureInfo.InvariantCulture),
                    RawHex = rawHex,
                    IsDecoded = true,
                })
                .ToList();
        }

        /// <summary>
        /// Parses a supported-PID bitmask response (PID 0x00, 0x20, ...) into absolute PID
        /// numbers. The bitmask sits one byte later than in a Mode 01 response because of
        /// the frame-number byte. Returns an empty list for a response that does not match
        /// <paramref name="basePid"/>.
        /// </summary>
        public static IReadOnlyList<int> ParseSupportedPids(byte[] mode02Response, int basePid)
        {
            var supported = new List<int>();
            if (mode02Response.Length <= MinResponseLength ||
                mode02Response[4] != PositiveSid || mode02Response[5] != basePid)
            {
                return supported;
            }

            for (int i = 7; i < mode02Response.Length; i++)
            {
                byte bitmask = mode02Response[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmask & (1 << (7 - bit))) != 0)
                        supported.Add(basePid + (i - 7) * 8 + bit + 1);
                }
            }

            return supported;
        }

        /// <summary>
        /// Extracts the triggering DTC from a Mode 02 PID 0x02 response. A zeroed code means
        /// the ECU has no freeze frame stored, reported as null (as is a malformed response —
        /// the caller cannot act on either).
        /// </summary>
        public static DiagnosticTroubleCode? ParseTriggeringDtc(byte[] mode02Response)
        {
            if (mode02Response.Length < 9 ||
                mode02Response[4] != PositiveSid || mode02Response[5] != 0x02)
            {
                return null;
            }

            byte high = mode02Response[7];
            byte low = mode02Response[8];
            if (high == 0x00 && low == 0x00)
                return null;

            return DiagnosticTroubleCode.FromBytes(high, low);
        }

        private static string ToHex(byte[] data, int start) =>
            string.Join(" ", data.Skip(start).Select(b => b.ToString("X2")));
    }
}
