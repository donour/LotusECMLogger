using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>Outcome of one KWP2000 request/response exchange with the ABS module.</summary>
    internal readonly record struct KwpResponse(bool Ok, string Error, byte[] Payload, byte Nrc)
    {
        public static KwpResponse Positive(byte[] payload) => new(true, "", payload, 0);

        public static KwpResponse Negative(byte nrc) =>
            new(false, $"NRC 0x{nrc:X2} ({AbsProtocol.NrcName(nrc)})", [], nrc);

        public static KwpResponse Failure(string error) => new(false, error, [], 0);

        /// <summary>Error text with the plain-language hint appended when one is known for the NRC.</summary>
        public string DetailedError
        {
            get
            {
                string hint = Nrc == 0 ? "" : AbsProtocol.NrcHint(Nrc);
                return hint.Length == 0 ? Error : $"{Error} — {hint}";
            }
        }
    }

    /// <summary>
    /// Owns a J2534 ISO-TP connection to the Bosch ESP8 ABS/ESP module and implements the KWP2000
    /// request layer on top of it: session control, SecurityAccess, keep-alive, responsePending
    /// handling, and ReadMemoryByAddress with format-byte discovery.
    ///
    /// The J2534 device performs ISO-TP segmentation and reassembly (the flow-control filter set up
    /// in <see cref="Open"/>), so a request is written as [4-byte CAN id][KWP payload] and a
    /// multi-frame response arrives already joined — the manual FF/CF/FC handling in the guide's
    /// reference client is not needed here.
    /// </summary>
    internal sealed class AbsKwpSession : IDisposable
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        /// <summary>Per-read timeout; the loop below spans the responsePending (P2*) window.</summary>
        private const int ReadTimeoutMs = 250;
        private const int ReadAttempts = 20;

        /// <summary>The module drops the session after ~5 s of silence; refresh well inside that.</summary>
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(2);

        private readonly J2534Session _session;
        private readonly J2534Channel _channel;

        private DateTime _lastActivity = DateTime.UtcNow;
        private byte? _acceptedAal;

        private AbsKwpSession(J2534Session session, J2534Channel channel)
        {
            _session = session;
            _channel = channel;
        }

        /// <summary>Opens the device, an ISO 15765 channel, and the ABS flow-control filter.</summary>
        public static AbsKwpSession Open()
        {
            J2534Session session = J2534Session.Open();
            try
            {
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(Abs.CreateFlowControlFilter()).ThrowIfError();
                return new AbsKwpSession(session, channel);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        /// <summary>The session byte currently accepted by the module, or null if none was entered.</summary>
        public byte? ActiveSession { get; private set; }

        /// <summary>True once <see cref="TryUnlock"/> has completed a SecurityAccess level 1 exchange.</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>
        /// Sends a KWP2000 request and waits for the matching positive response, transparently
        /// waiting out any number of responsePending (NRC 0x78) replies.
        /// </summary>
        public KwpResponse Request(params byte[] kwpPayload)
        {
            byte[] header = Abs.GetRequestHeader();
            byte[] message = new byte[header.Length + kwpPayload.Length];
            Array.Copy(header, message, header.Length);
            Array.Copy(kwpPayload, 0, message, header.Length, kwpPayload.Length);
            _channel.SendMessage(message);
            _lastActivity = DateTime.UtcNow;

            byte requestSid = kwpPayload[0];
            byte expectedResponseSid = (byte)(requestSid | AbsProtocol.PositiveResponseFlag);

            for (int attempt = 0; attempt < ReadAttempts; attempt++)
            {
                var response = _channel.ReadMessages(1, ReadTimeoutMs);
                if (response.Messages.Length == 0)
                    continue;

                byte[] data = response.Messages[0].Data;
                // Accept only frames from the ABS response id; skip our own TX echoes and other traffic.
                if (data.Length < 5 || !Abs.MatchesResponse(data))
                    continue;

                byte sid = data[4];
                if (sid == expectedResponseSid)
                {
                    _lastActivity = DateTime.UtcNow;
                    return KwpResponse.Positive(data[5..]);
                }

                // Negative response: [header] 7F <requestSid> <nrc>
                if (sid == AbsProtocol.NegativeResponseSid && data.Length >= 7 && data[5] == requestSid)
                {
                    byte nrc = data[6];
                    if (nrc == AbsProtocol.NrcResponsePending)
                    {
                        _lastActivity = DateTime.UtcNow;
                        continue; // still working — keep waiting for the final response
                    }

                    return KwpResponse.Negative(nrc);
                }
                // Some other frame from the ABS id — ignore and keep reading.
            }

            return KwpResponse.Failure("No response from ABS module (timeout).");
        }

        /// <summary>
        /// Enters the first session the module accepts from <paramref name="candidates"/>. The guide
        /// specifies 0x01/0x02/0x03, but this module was observed accepting the tester's 0x89 where it
        /// refuses 0x02, so callers pass their preferred order and the accepted byte is reported back.
        /// Entering a session resets the module's security state, so <see cref="IsUnlocked"/> clears.
        /// </summary>
        public (bool ok, string detail, byte session) EnterSession(params byte[] candidates)
        {
            string lastError = "no session candidates";
            foreach (byte candidate in candidates)
            {
                var response = Request(AbsProtocol.SidStartDiagnosticSession, candidate);
                if (response.Ok)
                {
                    ActiveSession = candidate;
                    IsUnlocked = false; // a session change resets security
                    return (true, $"session 0x{candidate:X2} accepted", candidate);
                }

                lastError = $"0x{candidate:X2}: {response.Error}";
            }

            return (false, lastError, 0);
        }

        /// <summary>
        /// Performs SecurityAccess level 1: request seed (27 01), derive key[i] = SBOX[seed[i]], send
        /// key (27 02). Returns a description either way — several services on this module turned out
        /// not to need the unlock, so callers report the outcome and continue rather than abort.
        /// </summary>
        public (bool ok, string detail) TryUnlock()
        {
            var seedResponse = Request(AbsProtocol.SidSecurityAccess, AbsProtocol.SecurityRequestSeed);
            if (!seedResponse.Ok)
                return (false, $"seed request failed: {seedResponse.DetailedError}");

            // Payload is [echoed sub-function 0x01][seed bytes…].
            if (seedResponse.Payload.Length < 1 + AbsProtocol.SeedLength)
                return (false, $"seed response too short ({seedResponse.Payload.Length} bytes)");

            byte[] seed = seedResponse.Payload[1..(1 + AbsProtocol.SeedLength)];

            // An all-zero seed is the conventional "already unlocked" answer — no key to send.
            if (Array.TrueForAll(seed, b => b == 0))
            {
                IsUnlocked = true;
                return (true, "already unlocked (zero seed)");
            }

            byte[] key = AbsProtocol.ComputeKey(seed);
            byte[] request = new byte[2 + key.Length];
            request[0] = AbsProtocol.SidSecurityAccess;
            request[1] = AbsProtocol.SecuritySendKey;
            Array.Copy(key, 0, request, 2, key.Length);

            var keyResponse = Request(request);
            if (!keyResponse.Ok)
                return (false, $"key rejected: {keyResponse.DetailedError} "
                             + $"(seed {BitConverter.ToString(seed)} → key {BitConverter.ToString(key)})");

            IsUnlocked = true;
            return (true, $"unlocked (seed {BitConverter.ToString(seed)} → key {BitConverter.ToString(key)})");
        }

        /// <summary>
        /// Sends TesterPresent with the response suppressed (3E 80). Fire-and-forget: nothing is read
        /// back, which is the point — it keeps the session alive without adding response traffic.
        /// </summary>
        public void TesterPresent()
        {
            byte[] header = Abs.GetRequestHeader();
            byte[] message = new byte[header.Length + 2];
            Array.Copy(header, message, header.Length);
            message[header.Length] = AbsProtocol.SidTesterPresent;
            message[header.Length + 1] = AbsProtocol.TesterPresentSuppressResponse;
            _channel.SendMessage(message);
            _lastActivity = DateTime.UtcNow;
        }

        /// <summary>Sends TesterPresent only if the bus has been quiet for longer than the keep-alive interval.</summary>
        public void KeepAlive()
        {
            if (DateTime.UtcNow - _lastActivity >= KeepAliveInterval)
                TesterPresent();
        }

        /// <summary>
        /// ReadMemoryByAddress (0x23): <c>23 &lt;AAL&gt; &lt;4-byte address&gt; &lt;length&gt;</c>, response
        /// <c>63 &lt;echoed address&gt; &lt;data…&gt;</c>.
        ///
        /// The guide flags the addressAndLength format byte as unverified, so the first call tries
        /// each candidate in <see cref="AbsProtocol.AddressAndLengthCandidates"/> and the one the
        /// module accepts is reused for the rest of the connection. Only the two NRCs that indicate a
        /// malformed request (0x13 incorrectMessageLength, 0x31 requestOutOfRange) advance to the next
        /// candidate — a security or conditions refusal is returned as-is, since retrying the same
        /// request in a different shape would not help.
        /// </summary>
        public KwpResponse ReadMemory(uint address, byte length)
        {
            if (_acceptedAal is byte known)
                return StripAddressEcho(ReadMemory(known, address, length), address);

            KwpResponse last = KwpResponse.Failure("no address format candidates");
            foreach (byte aal in AbsProtocol.AddressAndLengthCandidates)
            {
                last = ReadMemory(aal, address, length);
                if (last.Ok)
                {
                    _acceptedAal = aal;
                    return StripAddressEcho(last, address);
                }

                if (last.Nrc != AbsProtocol.NrcIncorrectMessageLength &&
                    last.Nrc != AbsProtocol.NrcRequestOutOfRange)
                    return last;
            }

            return last;
        }

        /// <summary>The address format byte the module accepted, or null if no memory read has succeeded.</summary>
        public byte? AcceptedAddressFormat => _acceptedAal;

        private KwpResponse ReadMemory(byte aal, uint address, byte length) => Request(
            AbsProtocol.SidReadMemoryByAddress,
            aal,
            (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            length);

        /// <summary>
        /// Removes the echoed address from a 0x63 response so only the data remains. The echo is
        /// verified rather than assumed: if the leading bytes are not the address we asked for, the
        /// payload is returned untouched so a differently-shaped response is still visible.
        /// </summary>
        private static KwpResponse StripAddressEcho(KwpResponse response, uint address)
        {
            if (!response.Ok)
                return response;

            byte[] payload = response.Payload;
            byte[] echo =
            [
                (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            ];

            if (payload.Length >= echo.Length && payload.AsSpan(0, echo.Length).SequenceEqual(echo))
                return KwpResponse.Positive(payload[echo.Length..]);

            return response;
        }

        public void Dispose() => _session.Dispose();
    }
}
