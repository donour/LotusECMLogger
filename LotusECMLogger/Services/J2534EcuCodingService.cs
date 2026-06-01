using SAE.J2534;

namespace LotusECMLogger.Services
{
	// TODO: Program mismatch repair function.
	// When the ECU is unlocked, read the state of the program mismatch flag and,
	// if set, optionally issue the reset command via the coding handler to clear it.
	// The coding-handler command register accepts values 1-7 (see service_coding_333ms
	// in the firmware); the reset path is one of those commands.
	public sealed class J2534EcuCodingService : IEcuCodingService
	{
		public T6eCodingDecoder ReadCoding()
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

			return ReadCodingInternal(channel);
		}

		public (bool success, string errorMessage) WriteCoding(T6eCodingDecoder coding)
		{
			try
			{
				string dllFileName = J2534APIFactory.DiscoverAPIs().First().FileName;
				J2534API api = J2534APIFactory.LoadAPI(dllFileName).Unwrap();
				using J2534Device device = api.OpenDevice("").Unwrap();
				return WriteRawCanCoding(coding, device);
			}
			catch (Exception ex)
			{
				return (false, $"Failed to write coding: {ex.Message}");
			}
		}

		private static T6eCodingDecoder ReadCodingInternal(J2534Channel channel)
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
				channel.SendMessages(Array.ConvertAll(codingRequest, b => new SAE.J2534.Message(b, channel.DefaultTxFlags)));
				GetMessagesResult resp = channel.ReadMessages(1, 100);
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

			try
			{
				return new T6eCodingDecoder(result_cod1, result_cod0);
			}
			catch (ArgumentException ex)
			{
				throw new InvalidEcuCodingDataException(result_cod1, result_cod0, ex);
			}
		}

		private static (bool success, string errorMessage) WriteRawCanCoding(T6eCodingDecoder codingDecoder, J2534Device device)
		{
			try
			{
				using J2534Channel canChannel = device.OpenChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE).Unwrap();

				byte[] highBytes = codingDecoder.GetHighBytes();
				byte[] lowBytes = codingDecoder.GetLowBytes();

				byte[] canMessage = new byte[12];
				canMessage[0] = 0x00;
				canMessage[1] = 0x00;
				canMessage[2] = 0x05;
				canMessage[3] = 0x02;

				Array.Copy(highBytes, 0, canMessage, 4, 4);
				Array.Copy(lowBytes, 0, canMessage, 8, 4);

				canChannel.SendMessage(canMessage);
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

