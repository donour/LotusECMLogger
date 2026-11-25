namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// One diagnostic conversation (ISO 15765) with one ECU. Services send requests through it
    /// without knowing how the adapter is shared: the <see cref="Broker.J2534Broker"/> hands them out
    /// on its one shared device (J2534-session-broker-plan.md). Disposing the link ends the conversation.
    /// </summary>
    public interface IDiagnosticLink : IDisposable
    {
        /// <summary>The ECU this link talks to.</summary>
        ECUDefinition Ecu { get; }

        /// <summary>
        /// Sends one request and waits for the ECU's reply to it.
        /// </summary>
        /// <param name="request">Service ID and parameters, without the CAN ID header.</param>
        /// <param name="echoLength">
        /// How many request bytes after the service ID a positive reply must echo (a PID, DID or
        /// sub-function), so a late reply to an earlier request is not taken for this one.
        /// </param>
        /// <param name="timeoutMs">
        /// How long to wait for the reply. A "response pending" (NRC 0x78) extends the wait, up to
        /// <see cref="DiagnosticTiming.MaxExchangeMs"/> in all.
        /// </param>
        DiagnosticReply Exchange(byte[] request, int echoLength = 0,
            int timeoutMs = DiagnosticTiming.DefaultReplyTimeoutMs);

        /// <summary>Sends a request without waiting for any reply.</summary>
        void Send(byte[] request);
    }

    /// <summary>Reply timing shared by every <see cref="IDiagnosticLink"/> implementation.</summary>
    public static class DiagnosticTiming
    {
        /// <summary>The wait most services used before the link existed (ten 250 ms reads).</summary>
        public const int DefaultReplyTimeoutMs = 2500;

        /// <summary>Extra wait after a "response pending" (ISO 14229 P2*server).</summary>
        public const int ResponsePendingExtensionMs = 5000;

        /// <summary>Hard cap per exchange, so an ECU answering 0x78 forever cannot hang a caller.</summary>
        public const int MaxExchangeMs = 10000;

        /// <summary>Negative response code: request correctly received, response pending.</summary>
        public const byte NrcResponsePending = 0x78;
    }

    /// <summary>
    /// The outcome of one <see cref="IDiagnosticLink.Exchange"/>: a positive reply, a negative
    /// reply, or no reply in time.
    /// </summary>
    public readonly record struct DiagnosticReply
    {
        /// <summary>
        /// The positive reply as received, including the 4-byte CAN ID header
        /// ([00 00 07 E8] &lt;SID + 0x40&gt; ...), so the existing decoders read it unchanged.
        /// Null unless <see cref="IsPositive"/>.
        /// </summary>
        public byte[]? Message { get; private init; }

        /// <summary>The negative response code. Null unless <see cref="IsNegative"/>.</summary>
        public byte? Nrc { get; private init; }

        public bool IsPositive => Message is not null;
        public bool IsNegative => Nrc is not null;
        public bool IsTimeout => Message is null && Nrc is null;

        public static DiagnosticReply Positive(byte[] message) => new() { Message = message };
        public static DiagnosticReply Negative(byte nrc) => new() { Nrc = nrc };
        public static DiagnosticReply Timeout => default;
    }

    /// <summary>What a received message means for an outstanding request.</summary>
    internal enum DiagnosticMatch
    {
        /// <summary>Not a reply to this request: another ECU, a transmit echo, another service.</summary>
        Unrelated,
        Positive,
        Negative,
        /// <summary>Negative response 0x78: the ECU is still working on it.</summary>
        Pending,
    }

    /// <summary>
    /// Decides whether a received message answers a request. Shared by the J2534 link and the
    /// test fakes, so the services' echo choices are checked by their tests.
    /// </summary>
    internal static class DiagnosticResponseMatcher
    {
        private const int HeaderLength = 4;

        /// <param name="responseId">The ECU's response CAN ID (0x7E8 for the ECM).</param>
        /// <param name="request">Service ID and parameters, without the CAN ID header.</param>
        /// <param name="echoLength">Request bytes after the service ID the positive reply must echo.</param>
        /// <param name="message">A received message, with its 4-byte CAN ID header.</param>
        /// <param name="nrc">The negative response code, for <see cref="DiagnosticMatch.Negative"/> and
        /// <see cref="DiagnosticMatch.Pending"/>.</param>
        public static DiagnosticMatch Match(uint responseId, byte[] request, int echoLength, byte[] message,
            out byte nrc)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.Length, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(echoLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(echoLength, request.Length - 1);

            nrc = 0;
            if (message.Length < HeaderLength + 1)
                return DiagnosticMatch.Unrelated;

            uint id = (uint)((message[0] << 24) | (message[1] << 16) | (message[2] << 8) | message[3]);
            if (id != responseId)
                return DiagnosticMatch.Unrelated;

            byte sid = request[0];
            byte replySid = message[HeaderLength];

            if (replySid == (byte)(sid + 0x40))
            {
                if (message.Length < HeaderLength + 1 + echoLength)
                    return DiagnosticMatch.Unrelated;
                for (int i = 0; i < echoLength; i++)
                {
                    if (message[HeaderLength + 1 + i] != request[1 + i])
                        return DiagnosticMatch.Unrelated;
                }
                return DiagnosticMatch.Positive;
            }

            if (replySid == 0x7F && message.Length >= HeaderLength + 3 && message[HeaderLength + 1] == sid)
            {
                nrc = message[HeaderLength + 2];
                return nrc == DiagnosticTiming.NrcResponsePending ? DiagnosticMatch.Pending : DiagnosticMatch.Negative;
            }

            return DiagnosticMatch.Unrelated;
        }
    }
}
