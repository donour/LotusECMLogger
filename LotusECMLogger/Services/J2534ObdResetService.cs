using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
	public sealed class J2534ObdResetService : IObdResetService
	{
		private readonly ILinkProvider _links;

		public J2534ObdResetService() : this(J2534LinkProvider.Instance) { }

		public J2534ObdResetService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

		public (bool success, string errorMessage) PerformLearningReset()
		{
			try
			{
				using var link = _links.OpenDiagnostic(ECUDefinition.ECM);

				var iso = new Iso15765Service(link);
				iso.SendLearningDataClear();

				return (true, "");
			}
			catch (Exception ex)
			{
				return (false, ex.Message);
			}
		}
	}
}
