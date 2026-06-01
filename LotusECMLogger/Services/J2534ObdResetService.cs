using SAE.J2534;

namespace LotusECMLogger.Services
{
	public sealed class J2534ObdResetService : IObdResetService
	{
		public (bool success, string errorMessage) PerformLearningReset()
		{
			try
			{
				string dllFileName = J2534APIFactory.DiscoverAPIs().First().FileName;
				J2534API api = J2534APIFactory.LoadAPI(dllFileName).Unwrap();
				using J2534Device device = api.OpenDevice("").Unwrap();
				using J2534Channel channel = device.OpenChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE).Unwrap();

				var flowControlFilter = new MessageFilter
				{
					FilterType = Filter.FLOW_CONTROL_FILTER,
					Mask = [0xFF, 0xFF, 0xFF, 0xFF],
					Pattern = [0x00, 0x00, 0x07, 0xE8],
					FlowControl = [0x00, 0x00, 0x07, 0xE0]
				};
				channel.StartMessageFilter(flowControlFilter).ThrowIfError();

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


