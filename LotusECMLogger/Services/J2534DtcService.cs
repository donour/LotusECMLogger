using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534DtcService : IDtcService
    {
        public (bool success, string errorMessage, DtcReadResult result) ReadCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                var stored = iso.ReadDtcs(OBDIIMode.ShowStoredDiagnosticTroubleCodes);

                // Permanent codes survive a Mode 04 clear. Not every firmware answers
                // service 0x0A, so a failed read degrades to a note instead of an error.
                IReadOnlyList<DiagnosticTroubleCode> permanent = [];
                string? permanentError = null;
                try
                {
                    permanent = iso.ReadDtcs(OBDIIMode.PermanentDiagnosticTroubleCodes);
                }
                catch (IOException ex)
                {
                    permanentError = ex.Message;
                }

                return (true, "", new DtcReadResult
                {
                    Stored = stored,
                    Permanent = permanent,
                    PermanentError = permanentError,
                });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new DtcReadResult());
            }
        }

        public (bool success, string errorMessage) ClearCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                return iso.ClearDiagnosticInformation();
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
