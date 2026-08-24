namespace LotusECMLogger.Services
{
    /// <summary>
    /// The two request forms the T6e firmware accepts for service 0x13. The ECU answers both
    /// with the same full response; the explicit sub-function is preferred because it is
    /// unambiguous, and the bare form exists for compatibility.
    /// </summary>
    public enum Mode13RequestForm
    {
        /// <summary>0x13 0xFF 0x00 — the explicit "report all DTCs" sub-function.</summary>
        ReportAll,

        /// <summary>0x13 on its own — the bare service request.</summary>
        BareService,
    }

    /// <summary>
    /// The result of one service 0x13 read: every code the ECU holds, in the single flat list
    /// the service returns.
    /// </summary>
    public sealed record Mode13ReadResult
    {
        /// <summary>
        /// The codes read, de-duplicated in first-seen order. The current and confirmed groups
        /// routinely report the same fault, and the wire format gives no way to tell them apart.
        /// </summary>
        public IReadOnlyList<DiagnosticTroubleCode> Codes { get; init; } = [];

        /// <summary>How many codes the response actually carried, before de-duplication.</summary>
        public int ReportedCodeCount { get; init; }

        /// <summary>The response SID and code bytes as hex, for verifying a service that is
        /// undocumented outside the firmware.</summary>
        public string RawHex { get; init; } = "";
    }

    /// <summary>
    /// Reads diagnostic trouble codes through the Lotus proprietary service 0x13, which returns
    /// the current, confirmed and TPMS code sets in a single round-trip. See
    /// <c>T6-mode13-programming.md</c> for the wire protocol.
    /// </summary>
    public interface IMode13DtcService
    {
        /// <summary>
        /// Sends one Mode 0x13 request and decodes the response.
        /// </summary>
        /// <param name="form">Which of the two accepted request forms to send.</param>
        /// <returns>
        /// Success flag, an error message when unsuccessful, and the codes read (an empty list
        /// when the ECU reports none).
        /// </returns>
        (bool success, string errorMessage, Mode13ReadResult result) ReadAllCodes(
            Mode13RequestForm form = Mode13RequestForm.ReportAll);
    }
}
