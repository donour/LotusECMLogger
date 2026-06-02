using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534DtcService : IDtcService
    {
        public (bool success, string errorMessage, IReadOnlyList<DiagnosticTroubleCode> codes) ReadStoredCodes()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                var codes = iso.ReadDtcs(OBDIIMode.ShowStoredDiagnosticTroubleCodes);
                return (true, "", codes);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, []);
            }
        }
    }
}
