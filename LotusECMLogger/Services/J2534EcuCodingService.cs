using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services
{
	// TODO: Program mismatch repair function.
	// When the ECU is unlocked, read the state of the program mismatch flag and,
	// if set, optionally issue the reset command via the coding handler to clear it.
	// The coding-handler command register accepts values 1-7 (see service_coding_333ms
	// in the firmware); the reset path is one of those commands.
	public sealed class J2534EcuCodingService : IEcuCodingService
	{
		private readonly ILinkProvider _links;
		private readonly ILinkProvider _writeLinks;

		// The write changes the ECM's coding, so it holds the ECM while it runs (plan §3.4); the read shares.
		public J2534EcuCodingService() : this(J2534Broker.Shared) { }

		/// <summary>On <paramref name="broker"/>, as the app runs it.</summary>
		public J2534EcuCodingService(J2534Broker broker) : this(
			broker.Links("the ECU coding read"),
			broker.EcuExclusiveLinks(ECUDefinition.ECM, "the ECU coding write")) { }

		public J2534EcuCodingService(ILinkProvider links) : this(links, links) { }

		/// <param name="links">For the read.</param>
		/// <param name="writeLinks">For the write.</param>
		public J2534EcuCodingService(ILinkProvider links, ILinkProvider writeLinks)
		{
			_links = links ?? throw new ArgumentNullException(nameof(links));
			_writeLinks = writeLinks ?? throw new ArgumentNullException(nameof(writeLinks));
		}

		public T6eCodingDecoder ReadCoding()
		{
			using var link = _links.OpenDiagnostic(ECUDefinition.ECM);
			return ReadCodingInternal(link);
		}

		public (bool success, string errorMessage) WriteCoding(T6eCodingDecoder coding)
		{
			try
			{
				using var link = _writeLinks.OpenCan();
				return WriteRawCanCoding(coding, link);
			}
			catch (Exception ex)
			{
				return (false, $"Failed to write coding: {ex.Message}");
			}
		}

		// Coding is read with Mode 22 PIDs 0x0263 (low word) and 0x0264 (high word).
		private const byte CodingPidLow = 0x63;
		private const byte CodingPidHigh = 0x64;

		// Bounds the read so a silent ECU (ignition off, wrong module) fails in a few seconds instead
		// of hanging. A live ECU answers well inside the first window; the slack covers an ECU still
		// busy just after key-on, and the retries cover a dropped request or reply. A "response
		// pending" (NRC 0x78) extends the wait inside the link (ISO 14229 P2*server), up to its cap.
		private const int ReadAttempts = 3;
		private const int ReadAttemptWindowMs = 1000;

		private static T6eCodingDecoder ReadCodingInternal(IDiagnosticLink link)
		{
			byte[]? codingLow = ReadCodingWord(link, CodingPidLow);
			byte[]? codingHigh = ReadCodingWord(link, CodingPidHigh);

			if (codingLow is null || codingHigh is null)
			{
				string missing = (codingLow, codingHigh) switch
				{
					(null, null) => "PIDs 0x0263 and 0x0264",
					(null, _) => "PID 0x0263",
					_ => "PID 0x0264",
				};
				throw new TimeoutException(
					$"No response from the ECU for coding {missing}. " +
					"Check that the ignition is on and the adapter is connected.");
			}

			try
			{
				return new T6eCodingDecoder(codingLow, codingHigh);
			}
			catch (ArgumentException ex)
			{
				throw new InvalidEcuCodingDataException(codingLow, codingHigh, ex);
			}
		}

		/// <summary>Reads one coding word; null when the ECU stays silent through every attempt.</summary>
		/// <exception cref="IOException">The ECU rejected the read.</exception>
		private static byte[]? ReadCodingWord(IDiagnosticLink link, byte pidLow)
		{
			for (int attempt = 0; attempt < ReadAttempts; attempt++)
			{
				var reply = link.Exchange(BuildCodingRequest(pidLow), echoLength: 2, timeoutMs: ReadAttemptWindowMs);
				if (reply.Message is byte[] data && ParseCodingResponse(data).Value is byte[] value)
					return value;
				if (reply.Nrc is byte nrc)
					throw new IOException($"ECU rejected the coding read (NRC 0x{nrc:X2}).");
			}
			return null;
		}

		private static byte[] BuildCodingRequest(byte pidLow) => [0x22, 0x02, pidLow];

		/// <summary>
		/// One classified frame from the coding read: a coding value (<see cref="Value"/> set), a
		/// negative response (<see cref="Nrc"/> set), or anything else — echoes, other services,
		/// other modules — with both null.
		/// </summary>
		internal readonly record struct CodingResponse(byte Pid, byte[]? Value, byte? Nrc)
		{
			public static readonly CodingResponse Ignored = new(0, null, null);
		}

		/// <summary>
		/// Classifies a frame received on the ISO 15765 channel. Positive replies are
		/// [00 00 07 E8] 62 02 &lt;63|64&gt; &lt;4 coding bytes&gt;; negative replies are
		/// [00 00 07 E8] 7F 22 &lt;NRC&gt;.
		/// </summary>
		internal static CodingResponse ParseCodingResponse(byte[] data)
		{
			// Only the ECM's response ID; this also drops echoes of our own 0x7E0 requests.
			if (data.Length < 7 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x07 || data[3] != 0xE8)
				return CodingResponse.Ignored;

			if (data[4] == 0x62 && data[5] == 0x02 &&
				(data[6] == CodingPidLow || data[6] == CodingPidHigh) && data.Length >= 11)
				return new CodingResponse(data[6], data[7..11], null);

			if (data[4] == 0x7F && data[5] == 0x22)
				return new CodingResponse(0, null, data[6]);

			return CodingResponse.Ignored;
		}

		// The coding must be persisted to all modules, each addressed by a distinct arbitration
		// ID (0x500 through 0x505).
		private const uint FirstCodingCanId = 0x500;
		private const uint LastCodingCanId = 0x505;
		private const int CodingFrameSpacingMs = 100;

		private static (bool success, string errorMessage) WriteRawCanCoding(T6eCodingDecoder codingDecoder, ICanLink canLink)
		{
			try
			{
				// Eight data bytes: the high coding word, then the low one.
				byte[] data = [.. codingDecoder.GetHighBytes(), .. codingDecoder.GetLowBytes()];

				// The link throws when the adapter rejects a frame, so a failed send is reported
				// instead of the write claiming success.
				for (uint canId = FirstCodingCanId; canId <= LastCodingCanId; canId++)
				{
					canLink.Send(canId, data);
					Thread.Sleep(CodingFrameSpacingMs);
				}

				return (true, "");
			}
			catch (Exception ex)
			{
				return (false, $"Raw CAN coding write failed: {ex.Message}");
			}
		}
	}
}

