using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
    public sealed class J2534VinSetService : IVinSetService
    {
        private readonly ILinkProvider _links;

        public J2534VinSetService() : this(J2534Broker.Shared) { }

        /// <summary>On <paramref name="broker"/>, as the app runs it.</summary>
        public J2534VinSetService(J2534Broker broker) : this(broker.EcuExclusiveLinks(ECUDefinition.ECM, "the VIN write")) { }

        public J2534VinSetService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

        public (bool success, string errorMessage) SetVin(string vin)
        {
            try
            {
                using var link = _links.OpenDiagnostic(ECUDefinition.ECM);

                var iso = new Iso15765Service(link);
                return iso.SetVin(vin);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
