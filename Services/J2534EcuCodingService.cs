using SAE.J2534;

namespace LotusECMLogger.Services
{
	public sealed class J2534EcuCodingService : IEcuCodingService
	{
		public T6eCodingDecoder ReadCoding()
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

			return ReadCodingInternal(channel);
		}

		public (bool success, string errorMessage) WriteCoding(T6eCodingDecoder coding)
		{
			try
			{
				string dllFileName = APIFactory.GetAPIinfo().First().Filename;
				API api = APIFactory.GetAPI(dllFileName);
				using Device device = api.GetDevice();
				return WriteRawCanCoding(coding, device);
			}
			catch (Exception ex)
			{
				return (false, $"Failed to write coding: {ex.Message}");
			}
		}

		private static T6eCodingDecoder ReadCodingInternal(Channel channel)
		{
			byte[] result_cod0 = [0, 0, 0, 0];
			byte[] result_cod1 = [0, 0, 0, 0];

			byte[][] codingRequest =
			[
				[0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x63],
				[0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x64]
			];
			int done = 0;
			do
			{
				channel.SendMessages(codingRequest);
				GetMessageResults resp = channel.GetMessages(1, 100);
				if (resp.Messages.Length > 0)
				{
					var data = resp.Messages[0].Data;
					if (data.Length >= 11)
					{
						if (data[4] == 0x62 && data[5] == 0x02)
						{
							if (data[6] == 0x63)
							{
								result_cod1 = data[7..11];
								done |= 1;
							}
							if (data[6] == 0x64)
							{
								result_cod0 = data[7..11];
								done |= 2;
							}
						}
					}
				}
			} while (done != 3);

			return new T6eCodingDecoder(result_cod1, result_cod0);
		}

		private static (bool success, string errorMessage) WriteRawCanCoding(T6eCodingDecoder codingDecoder, Device device)
		{
			try
			{
				using Channel canChannel = device.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);

				byte[] highBytes = codingDecoder.GetHighBytes();
				byte[] lowBytes = codingDecoder.GetLowBytes();

				byte[] canMessage = new byte[12];
				canMessage[0] = 0x00;
				canMessage[1] = 0x00;
				canMessage[2] = 0x05;
				canMessage[3] = 0x02;

				Array.Copy(highBytes, 0, canMessage, 4, 4);
				Array.Copy(lowBytes, 0, canMessage, 8, 4);

				canChannel.SendMessages([canMessage]);
				Thread.Sleep(100);

				return (true, "");
			}
			catch (Exception ex)
			{
				return (false, $"Raw CAN coding write failed: {ex.Message}");
			}
		}
	}
}


