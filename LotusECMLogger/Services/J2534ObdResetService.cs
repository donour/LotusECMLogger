using SAE.J2534;

namespace LotusECMLogger.Services
{
	public sealed class J2534ObdResetService : IObdResetService
	{
		public (bool success, string errorMessage) PerformLearningReset()
		{
			try
			{
				string dllFileName = APIFactory.GetAPIinfo().First().Filename;
				API api = APIFactory.GetAPI(dllFileName);
				using Device device = api.GetDevice();
				using Channel channel = device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE);

				var flowControlFilter = new MessageFilter
				{
					FilterType = Filter.FLOW_CONTROL_FILTER,
					Mask = [0xFF, 0xFF, 0xFF, 0xFF],
					Pattern = [0x00, 0x00, 0x07, 0xE8],
					FlowControl = [0x00, 0x00, 0x07, 0xE0]
				};
				channel.StartMsgFilter(flowControlFilter);

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


