using SAE.J2534;

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
        public (bool success, string errorMessage, Mode13ReadResult result) ReadAllCodes(
            Mode13RequestForm form = Mode13RequestForm.ReportAll)
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                return (true, "", Mode13Decoder.Decode(iso.ReadAllDtcsMode13(form)));
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new Mode13ReadResult());
            }
        }
    }
}
