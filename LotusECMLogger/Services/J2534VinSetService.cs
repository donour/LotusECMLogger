using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
    public sealed class J2534VinSetService : IVinSetService
    {
        private readonly ILinkProvider _links;

        public J2534VinSetService() : this(J2534LinkProvider.Instance) { }

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
