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
    /// The codes returned by one <see cref="IDtcService.ReadCodes"/> pass for one module:
    /// stored (confirmed) codes from service 0x03, pending codes from service 0x07, and
    /// permanent codes from service 0x0A.
    /// </summary>
    public sealed record DtcModuleResult
    {
        /// <summary>Stored (confirmed) codes from service 0x03. Empty with a null
        /// <see cref="StoredError"/> means the module answered and reported no stored codes.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Stored { get; init; } = [];

        /// <summary>Pending codes: faults detected this or last drive cycle that have not yet
        /// confirmed (matured) into stored codes.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Pending { get; init; } = [];

        /// <summary>Permanent codes survive a Mode 04 clear and cannot be erased on request.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Permanent { get; init; } = [];

        /// <summary>
        /// Non-null when service 0x03 got no valid answer (silence, negative response, or an
        /// adapter failure). All three code lists are unknown in that case, not empty.
        /// </summary>
        public string? StoredError { get; init; }

        /// <summary>
        /// Non-null when the pending-code read failed (e.g. the firmware does not answer
        /// service 0x07); <see cref="Pending"/> is empty in that case and the list is unknown.
        /// </summary>
        public string? PendingError { get; init; }

        /// <summary>
        /// Non-null when the permanent-code read failed or was not attempted;
        /// <see cref="Permanent"/> is empty in that case and the list is unknown.
        /// </summary>
        public string? PermanentError { get; init; }
    }

    /// <summary>
    /// The codes returned by one <see cref="IDtcService.ReadCodes"/> pass. The top-level
    /// lists are the engine ECU's; <see cref="Tcu"/> carries the transmission control unit's
    /// codes when that module was read.
    /// </summary>
    public sealed record DtcReadResult
    {
        public IReadOnlyList<DiagnosticTroubleCode> Stored { get; init; } = [];

        /// <summary>Pending codes: faults detected this or last drive cycle that have not yet
        /// confirmed (matured) into stored codes.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Pending { get; init; } = [];

        /// <summary>Permanent codes survive a Mode 04 clear and cannot be erased on request.</summary>
        public IReadOnlyList<DiagnosticTroubleCode> Permanent { get; init; } = [];

        /// <summary>
        /// Non-null when the pending-code read failed (e.g. the firmware does not answer
        /// service 0x07); <see cref="Pending"/> is empty in that case.
        /// </summary>
        public string? PendingError { get; init; }

        /// <summary>
        /// Non-null when the permanent-code read failed (e.g. the firmware does not answer
        /// service 0x0A); <see cref="Permanent"/> is empty in that case.
        /// </summary>
        public string? PermanentError { get; init; }

        /// <summary>
        /// The transmission control unit's codes. Null only when the transmission was not read
        /// at all (an engine-only result such as the emissions pre-check); non-null whenever a
        /// read was attempted. <see cref="DtcModuleResult.StoredError"/> distinguishes a module
        /// that could not be read from one that answered with no codes.
        /// </summary>
        public DtcModuleResult? Tcu { get; init; }
    }

    public interface IDtcService
    {
        /// <summary>
        /// Reads stored (service 0x03), pending (service 0x07), and permanent (service 0x0A)
        /// diagnostic trouble codes from the engine ECU, and then from the transmission control
        /// unit at the standard second OBD ECU address (0x7E1/0x7E9). A transmission that does
        /// not answer is reported through <see cref="DtcReadResult.Tcu"/>; it does not fail the
        /// engine read.
        /// </summary>
        /// <returns>
        /// Success flag, an error message when unsuccessful, and the codes read. Success means
        /// the engine ECU's stored-code read succeeded; a missing transmission is not an error.
        /// </returns>
        (bool success, string errorMessage, DtcReadResult result) ReadCodes();

        /// <summary>
        /// Clears the engine ECU's diagnostic information via OBD-II service 0x04: stored and
        /// pending DTCs, freeze frame data, readiness monitor results, and related stored
        /// values. This does not clear transmission codes; use <see cref="ClearTcuCodes"/>.
        /// </summary>
        /// <returns>Success flag and an error message when unsuccessful.</returns>
        (bool success, string errorMessage) ClearCodes();

        /// <summary>
        /// Clears the transmission control unit's diagnostic information via OBD-II service
        /// 0x04 at the standard second OBD ECU address (0x7E1/0x7E9). Engine codes and the
        /// engine freeze frame are not affected; permanent codes cannot be cleared.
        /// </summary>
        /// <returns>Success flag and an error message when unsuccessful.</returns>
        (bool success, string errorMessage) ClearTcuCodes();
    }
}
