using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Reads every trouble code the engine ECU holds in one round-trip via the Lotus
    /// proprietary service 0x13. ISO-TP segmentation — including the flow-control frame the
    /// service depends on for responses longer than seven bytes — is handled by the J2534
    /// device's ISO15765 layer, so the response arrives already reassembled.
    /// </summary>
    public sealed class J2534Mode13DtcService : IMode13DtcService
    {
        private readonly ILinkProvider _links;

        public J2534Mode13DtcService() : this(J2534Broker.Shared) { }

        /// <summary>On <paramref name="broker"/>, as the app runs it.</summary>
        public J2534Mode13DtcService(J2534Broker broker) : this(broker.Links("the Mode 0x13 read")) { }

        public J2534Mode13DtcService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

        public (bool success, string errorMessage, Mode13ReadResult result) ReadAllCodes(
            Mode13RequestForm form = Mode13RequestForm.ReportAll)
        {
            try
            {
                using var link = _links.OpenDiagnostic(ECUDefinition.ECM);
                var iso = new Iso15765Service(link);
                return (true, "", Mode13Decoder.Decode(iso.ReadAllDtcsMode13(form)));
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new Mode13ReadResult());
            }
        }
    }
}
