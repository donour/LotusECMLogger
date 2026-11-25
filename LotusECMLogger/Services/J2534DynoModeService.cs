using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
	public sealed class J2534DynoModeService : IDynoModeService
	{
		// Mode 0x2F (output control) PID 0x0170: enables the ECU's diagnostic override
		// ("dyno mode"). The request takes no arguments; a positive response echoes 6F 01 70.
		private static readonly byte[] DynoModeRequest = [0x2F, 0x01, 0x70];
		private const int SendCount = 5;
		private const int ReplyTimeoutMs = 250;

		private readonly ILinkProvider _links;

		public J2534DynoModeService() : this(J2534Broker.Shared) { }

		/// <summary>On <paramref name="broker"/>, as the app runs it.</summary>
		public J2534DynoModeService(J2534Broker broker) : this(broker.EcuExclusiveLinks(ECUDefinition.ECM, "dyno mode")) { }

		public J2534DynoModeService(ILinkProvider links) => _links = links ?? throw new ArgumentNullException(nameof(links));

		public (bool success, string errorMessage) EnableDynoMode()
		{
			try
			{
				using var link = _links.OpenDiagnostic(ECUDefinition.ECM);

				bool acknowledged = false;
				for (int i = 0; i < SendCount; i++)
				{
					if (link.Exchange(DynoModeRequest, echoLength: 2, timeoutMs: ReplyTimeoutMs).IsPositive)
						acknowledged = true;
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
