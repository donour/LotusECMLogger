using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// KWP2000 (ISO 14230) diagnostic client for the Bosch ESP8 ABS/ESP module over ISO-TP.
    /// The ABS uses CAN IDs 0x7E2 (request) / 0x7EA (response) — distinct from the engine ECU
    /// (0x7E0/0x7E8) and with a different SID table — so it needs its own flow-control filter
    /// and request header.
    ///
    /// This service is intentionally read-only for now. <see cref="ReadModuleInfo"/> reads the
    /// ECU identification record; it never performs SecurityAccess, never enters the programming
    /// session, and issues no write/clear/routine services, so it cannot alter the module.
    /// </summary>
    public sealed class J2534AbsService : IAbsService
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        // KWP2000 service IDs used here (all read-only).
        private const byte SidStartDiagnosticSession = 0x10;
        private const byte SidReadEcuIdentification = 0x1A;

        // A positive KWP response echoes the request SID with bit 6 set (SID | 0x40).
        private const byte PositiveResponseFlag = 0x40;

        private const byte ExtendedSession = 0x03;
        private const byte IdentificationRecordAll = 0x87;

        private const byte NrcResponsePending = 0x78;
        private const byte NrcSecurityAccessDenied = 0x33;
        private const byte NegativeResponseSid = 0x7F;

        public (bool success, string errorMessage, AbsModuleInfo result) ReadModuleInfo()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(Abs.CreateFlowControlFilter()).ThrowIfError();

                // Enter the extended diagnostic session. This is read-only session management —
                // no SecurityAccess, no programming session — so nothing here can modify the
                // module. Treat failure as non-fatal: some firmware answers 0x1A in the default
                // session anyway, so still attempt the read and fold any session error into the
                // final message.
                var sessionResult = Request(channel, [SidStartDiagnosticSession, ExtendedSession]);

                // Read ECU identification (record 0x87).
                var idResult = Request(channel, [SidReadEcuIdentification, IdentificationRecordAll]);
                if (!idResult.ok)
                {
                    if (idResult.nrc == NrcSecurityAccessDenied)
                        return (false,
                            "The ABS module requires SecurityAccess (unlock) to read its identification. " +
                            "The unlock step is deferred, so this read-only feature cannot go further yet.",
                            AbsModuleInfo.Empty);

                    string message = $"Failed to read ECU identification: {idResult.error}";
                    if (!sessionResult.ok)
                        message += $" (extended session was not entered: {sessionResult.error})";
                    return (false, message, AbsModuleInfo.Empty);
                }

                // idResult.payload = [0x87 (echoed record id), <identification bytes...>].
                byte[] identification = idResult.payload.Length > 0 ? idResult.payload[1..] : [];
                return (true, "", AbsModuleInfo.FromIdentification(identification));
            }
            catch (Exception ex)
            {
                return (false, ex.Message, AbsModuleInfo.Empty);
            }
        }

        /// <summary>
        /// Sends a single-frame KWP2000 request to the ABS and waits for its response. The
        /// J2534 ISO15765 channel handles ISO-TP framing/reassembly, so this works for
        /// multi-frame responses too. NRC 0x78 (responsePending) is transparently awaited.
        /// </summary>
        /// <returns>
        /// <c>ok</c> with the response payload after the positive-response SID byte, or an
        /// error string plus the NRC (0 when none) on a negative response or timeout.
        /// </returns>
        private static (bool ok, string error, byte[] payload, byte nrc) Request(
            J2534Channel channel, byte[] kwpPayload)
        {
            byte[] header = Abs.GetRequestHeader();
            byte[] message = new byte[header.Length + kwpPayload.Length];
            Array.Copy(header, message, header.Length);
            Array.Copy(kwpPayload, 0, message, header.Length, kwpPayload.Length);
            channel.SendMessage(message);

            byte requestSid = kwpPayload[0];
            byte expectedResponseSid = (byte)(requestSid | PositiveResponseFlag);

            // The budget spans the responsePending window (P2*): repeated 250 ms reads.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var response = channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                byte[] data = response.Messages[0].Data;
                // Accept only frames addressed from the ABS response id; skip our own TX
                // echoes/confirmations and unrelated bus traffic.
                if (data.Length < 5 || !Abs.MatchesResponse(data))
                    continue;

                byte sid = data[4];
                if (sid == expectedResponseSid)
                    return (true, "", data[5..], 0);

                // Negative response: [header] 7F <requestSid> <nrc>
                if (sid == NegativeResponseSid && data.Length >= 7 && data[5] == requestSid)
                {
                    byte nrc = data[6];
                    if (nrc == NrcResponsePending)
                        continue; // module still working — keep waiting for the final response
                    return (false, $"NRC 0x{nrc:X2} ({NrcName(nrc)})", [], nrc);
                }
                // Some other frame from the ABS id — ignore and keep reading.
            }

            return (false, "No response from ABS module (timeout).", [], 0);
        }

        private static string NrcName(byte nrc) => nrc switch
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
    }
}
