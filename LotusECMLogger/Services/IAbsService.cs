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

    public interface IAbsService
    {
        /// <summary>
        /// Reads the ABS/ESP module's ECU identification (KWP2000 service 0x1A, record 0x87).
        /// This is strictly read-only: it enters the extended diagnostic session and reads —
        /// it performs no SecurityAccess unlock and never uses the programming session, so it
        /// cannot modify the module. If the firmware gates the read behind SecurityAccess, the
        /// returned error says so (the unlock step is deliberately deferred to a later feature).
        /// </summary>
        /// <returns>
        /// A success flag, an error message when unsuccessful (naming the step and NRC that
        /// failed), and the decoded module information on success.
        /// </returns>
        (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo();
    }
}
