using SAE.J2534;

namespace LotusECMLogger.Services
{
	public sealed class J2534DynoModeService : IDynoModeService
	{
		// Mode 0x2F (output control) PID 0x0170: enables the ECU's diagnostic override
		// ("dyno mode"). The request takes no arguments; a positive response echoes 6F 01 70.
		private static readonly byte[] DynoModeRequest = [0x00, 0x00, 0x07, 0xE0, 0x2F, 0x01, 0x70];
		private const int SendCount = 5;

		public (bool success, string errorMessage) EnableDynoMode()
		{
			try
			{
				using var session = J2534Session.Open();
				J2534Channel channel = session.OpenIso15765();

				channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

				bool acknowledged = false;
				for (int i = 0; i < SendCount; i++)
				{
					channel.SendMessage(DynoModeRequest);

					var response = channel.ReadMessages(1, 250);
					if (response.Messages.Length > 0)
					{
						var data = response.Messages[0].Data;
						if (data.Length >= 7 && data[4] == 0x6F && data[5] == 0x01 && data[6] == 0x70)
							acknowledged = true;
					}
				}

				return acknowledged
					? (true, "")
					: (false, "No positive response from ECU (sent request 5 times).");
			}
			catch (Exception ex)
			{
				return (false, ex.Message);
			}
		}
	}
}
