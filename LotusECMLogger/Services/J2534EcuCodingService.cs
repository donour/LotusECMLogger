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
			using var session = J2534Session.Open();
			J2534Channel channel = session.OpenIso15765();

			channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

			return ReadCodingInternal(channel);
		}

		public (bool success, string errorMessage) WriteCoding(T6eCodingDecoder coding)
		{
			try
			{
				using var session = J2534Session.Open();
				return WriteRawCanCoding(coding, session.OpenCan());
			}
			catch (Exception ex)
			{
				return (false, $"Failed to write coding: {ex.Message}");
			}
		}

		// Coding is read with Mode 22 PIDs 0x0263 (low word) and 0x0264 (high word).
		private const byte CodingPidLow = 0x63;
		private const byte CodingPidHigh = 0x64;

		// Bounds the read so a silent ECU (ignition off, wrong module) fails in ~3 s instead of
		// hanging. A live ECU answers both PIDs well inside the first window; the slack covers an
		// ECU still busy just after key-on, and the retries cover a dropped request or reply.
		private const int ReadAttempts = 3;
		private const int ReadAttemptWindowMs = 1000;
		private const int ReadPollMs = 100;

		// responsePending (NRC 0x78) means "still working": per ISO 14229 the tester waits up to
		// P2*server (5 s) for the final reply instead of re-sending. The hard cap keeps an ECU that
		// answers 0x78 forever from recreating the hang this bound exists to prevent.
		private const byte NrcResponsePending = 0x78;
		private const int ResponsePendingExtensionMs = 5000;
		private const int MaxReadMs = 10000;

		private static T6eCodingDecoder ReadCodingInternal(J2534Channel channel)
		{
			byte[]? codingLow = null;
			byte[]? codingHigh = null;
			var total = System.Diagnostics.Stopwatch.StartNew();

			for (int attempt = 0; attempt < ReadAttempts && (codingLow is null || codingHigh is null); attempt++)
			{
				// Re-request only what is still missing, so a retry cannot pile up duplicate replies.
				if (codingLow is null)
					channel.SendMessage(BuildCodingRequest(CodingPidLow)).ThrowIfError();
				if (codingHigh is null)
					channel.SendMessage(BuildCodingRequest(CodingPidHigh)).ThrowIfError();

				var window = System.Diagnostics.Stopwatch.StartNew();
				long windowMs = ReadAttemptWindowMs;
				while (window.ElapsedMilliseconds < windowMs && total.ElapsedMilliseconds < MaxReadMs &&
					(codingLow is null || codingHigh is null))
				{
					GetMessagesResult resp = channel.ReadMessages(2, ReadPollMs);

					// J2534 reports TIMEOUT whenever fewer messages than requested arrived, so the
					// messages are consumed before the status is judged.
					foreach (var message in resp.Messages)
					{
						var reply = ParseCodingResponse(message.Data);
						if (reply.Value is not null)
						{
							if (reply.Pid == CodingPidLow)
								codingLow = reply.Value;
							else
								codingHigh = reply.Value;
						}
						else if (reply.Nrc == NrcResponsePending)
						{
							// Keep listening in this window rather than re-sending into a busy ECU.
							windowMs = Math.Max(windowMs, window.ElapsedMilliseconds + ResponsePendingExtensionMs);
						}
						else if (reply.Nrc is byte nrc)
						{
							throw new IOException($"ECU rejected the coding read (NRC 0x{nrc:X2}).");
						}
					}

					// Anything other than "nothing (more) arrived" means the adapter itself failed —
					// e.g. it was unplugged — which an empty read would otherwise disguise as silence.
					if (!resp.IsSuccess && !resp.IsTimeout && !resp.IsBufferEmpty &&
						resp.Status != ResultCode.BUFFER_OVERFLOW)
					{
						throw new J2534Exception(resp.Status, $"J2534 read failed during coding read: {resp.Status}");
					}
				}
			}

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

		private static byte[] BuildCodingRequest(byte pidLow) =>
			[0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, pidLow];

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

		private static (bool success, string errorMessage) WriteRawCanCoding(T6eCodingDecoder codingDecoder, J2534Channel canChannel)
		{
			try
			{
				byte[] highBytes = codingDecoder.GetHighBytes();
				byte[] lowBytes = codingDecoder.GetLowBytes();

				byte[] canMessage = new byte[12];
				canMessage[0] = 0x00;
				canMessage[1] = 0x00;
				canMessage[2] = 0x05;

				Array.Copy(highBytes, 0, canMessage, 4, 4);
				Array.Copy(lowBytes, 0, canMessage, 8, 4);

				// The coding must be persisted to all modules, each addressed by a
				// distinct arbitration ID (0x500 through 0x505).
				for (byte arbIdLow = 0x00; arbIdLow <= 0x05; arbIdLow++)
				{
					canMessage[3] = arbIdLow;
					canChannel.SendMessage(canMessage);
					Thread.Sleep(100);
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

