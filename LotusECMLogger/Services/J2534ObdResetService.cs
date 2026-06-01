using SAE.J2534;

namespace LotusECMLogger.Services
{
	public sealed class J2534ObdResetService : IObdResetService
	{
		public (bool success, string errorMessage) PerformLearningReset()
		{
			try
			{
				using var session = J2534Session.Open();
				J2534Channel channel = session.OpenIso15765();

				channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

				var iso = new Iso15765Service(channel);
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


