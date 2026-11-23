using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
    public sealed class J2534DtcService : IDtcService
    {
        private readonly ILinkProvider _links;

        public J2534DtcService() : this(J2534LinkProvider.Instance) { }

        public J2534DtcService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

        public (bool success, string errorMessage, DtcReadResult result) ReadCodes()
        {
            try
            {
                using var link = _links.OpenDiagnostic(ECUDefinition.ECM);
                var iso = new Iso15765Service(link);
                var stored = iso.ReadDtcs(OBDIIMode.ShowStoredDiagnosticTroubleCodes);

                // Pending and permanent codes each degrade to a note instead of an error:
                // not every firmware answers services 0x07 and 0x0A, and the stored codes
                // are still worth showing.
                IReadOnlyList<DiagnosticTroubleCode> pending = [];
                string? pendingError = null;
                try
                {
                    pending = iso.ReadDtcs(OBDIIMode.ShowPendingDiagnosticTroubleCodes);
                }
                catch (IOException ex)
                {
                    pendingError = ex.Message;
                }

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
                    Pending = pending,
                    Permanent = permanent,
                    PendingError = pendingError,
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
                using var link = _links.OpenDiagnostic(ECUDefinition.ECM);
                var iso = new Iso15765Service(link);
                return iso.ClearDiagnosticInformation();
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
