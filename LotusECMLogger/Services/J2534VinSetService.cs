using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534VinSetService : IVinSetService
    {
        public (bool success, string errorMessage) SetVin(string vin)
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();

                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                return iso.SetVin(vin);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
