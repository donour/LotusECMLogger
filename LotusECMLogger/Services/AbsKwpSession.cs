using LotusECMLogger.Services.Links;

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
    /// The KWP2000 request layer for the Bosch ESP8 ABS/ESP module over an <see cref="IDiagnosticLink"/>
    /// to it: session control, SecurityAccess, keep-alive and ReadMemoryByAddress with format-byte
    /// discovery. The link (the J2534 broker's ISO 15765 channel) does the ISO-TP segmentation, the
    /// reply matching and the responsePending (NRC 0x78) waits, so the manual FF/CF/FC handling in the
    /// guide's reference client is not needed here.
    /// </summary>
    internal sealed class AbsKwpSession : IDisposable
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        /// <summary>
        /// The reply budget the old read loop had (20 reads of 250 ms). Each responsePending extends it,
        /// inside the link, up to its 10 s cap.
        /// </summary>
        private const int ReplyTimeoutMs = 5000;

        /// <summary>The module drops the session after ~5 s of silence; refresh well inside that.</summary>
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(2);

        private readonly IDiagnosticLink _link;

        private DateTime _lastActivity = DateTime.UtcNow;
        private byte? _acceptedAal;

        private AbsKwpSession(IDiagnosticLink link) => _link = link;

        /// <summary>Opens a link to the ABS module from <paramref name="links"/> (which decides the lease).</summary>
        public static AbsKwpSession Open(ILinkProvider links) => new(links.OpenDiagnostic(Abs));

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
            DiagnosticReply reply;
            try
            {
                // The link accepts only the ABS response id with SID + 0x40, or 7F <SID> <NRC>.
                reply = _link.Exchange(kwpPayload, echoLength: 0, timeoutMs: ReplyTimeoutMs);
            }
            catch (Exception ex) when (ex is IOException or J2534Exception)
            {
                // A refused send or a failed adapter is one failed request, as a timeout is: callers
                // report it and still run their clean-up (stopping a routine, restoring the session).
                return KwpResponse.Failure($"ABS request failed: {ex.Message}");
            }
            _lastActivity = DateTime.UtcNow;

            if (reply.Message is byte[] data)
                return KwpResponse.Positive(data[5..]);
            if (reply.Nrc is byte nrc)
                return KwpResponse.Negative(nrc);
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
            try
            {
                _link.Send([AbsProtocol.SidTesterPresent, AbsProtocol.TesterPresentSuppressResponse]);
            }
            catch (Exception ex) when (ex is IOException or J2534Exception)
            {
                // Fire-and-forget, as before: a lost keep-alive shows up as the next request failing.
            }
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

        public void Dispose() => _link.Dispose();
    }
}
