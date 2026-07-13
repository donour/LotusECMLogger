namespace LotusECMLogger.Services
{
    /// <summary>
    /// The four DTC categories encoded in the top two bits of a diagnostic trouble code.
    /// </summary>
    public enum DtcCategory
    {
        Powertrain = 0,
        Chassis = 1,
        Body = 2,
        Network = 3,
    }

    /// <summary>
    /// A single OBD-II diagnostic trouble code (e.g. "P0301").
    /// </summary>
    public record DiagnosticTroubleCode
    {
        /// <summary>Human-readable code, e.g. "P0301".</summary>
        public required string Code { get; init; }

        /// <summary>Category implied by the code prefix (P/C/B/U).</summary>
        public required DtcCategory Category { get; init; }

        /// <summary>The raw 16-bit value as transmitted by the ECU.</summary>
        public required ushort Raw { get; init; }

        /// <summary>
        /// Decodes a two-byte DTC (SAE J2012 / ISO 15031-6). The top two bits select the
        /// category (P/C/B/U), the next two bits are the first digit (0-3), and the remaining
        /// twelve bits are three hexadecimal digits.
        /// </summary>
        public static DiagnosticTroubleCode FromBytes(byte high, byte low)
        {
            const string prefixes = "PCBU";
            int categoryIndex = (high & 0xC0) >> 6;
            int firstDigit = (high & 0x30) >> 4;
            int secondDigit = high & 0x0F;
            int thirdDigit = (low & 0xF0) >> 4;
            int fourthDigit = low & 0x0F;

            return new DiagnosticTroubleCode
            {
                Code = $"{prefixes[categoryIndex]}{firstDigit}{secondDigit:X}{thirdDigit:X}{fourthDigit:X}",
                Category = (DtcCategory)categoryIndex,
                Raw = (ushort)((high << 8) | low),
            };
        }
    }

    /// <summary>
    /// The codes returned by one <see cref="IDtcService.ReadCodes"/> pass: stored (confirmed)
    /// codes from service 0x03 and permanent codes from service 0x0A.
    /// </summary>
    public sealed record DtcReadResult
    {
        public IReadOnlyList<DiagnosticTroubleCode> Stored { get; init; } = [];

        /// <summary>Permanent codes survive a Mode 04 clear and cannot be erased on request.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Permanent { get; init; } = [];

        /// <summary>
        /// Non-null when the permanent-code read failed (e.g. the firmware does not answer
        /// service 0x0A); <see cref="Permanent"/> is empty in that case.
        /// </summary>
        public string? PermanentError { get; init; }
    }

    public interface IDtcService
    {
        /// <summary>
        /// Reads stored (service 0x03) and permanent (service 0x0A) diagnostic trouble codes.
        /// </summary>
        /// <returns>
        /// Success flag, an error message when unsuccessful, and the codes read (empty lists
        /// when the ECU reports no codes).
        /// </returns>
        (bool success, string errorMessage, DtcReadResult result) ReadCodes();

        /// <summary>
        /// Clears diagnostic information via OBD-II service 0x04: stored and pending DTCs,
        /// freeze frame data, readiness monitor results, and related stored values.
        /// </summary>
        /// <returns>Success flag and an error message when unsuccessful.</returns>
        (bool success, string errorMessage) ClearCodes();
    }
}
