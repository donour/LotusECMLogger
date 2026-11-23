using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Tests.Fakes
{
    /// <summary>
    /// An <see cref="ILinkProvider"/> backed by scripted ECUs instead of a J2534 adapter. Each
    /// scripted request has one reply the ECU sends every time it is asked; anything unscripted is
    /// met with silence. Replies go through the real <see cref="DiagnosticResponseMatcher"/>, so a
    /// service asking for the wrong echo gets a timeout here just as it would on a car.
    /// </summary>
    internal sealed class ScriptedLinkProvider : ILinkProvider
    {
        private readonly Dictionary<(uint ResponseId, string Request), byte[]> _replies = [];

        /// <summary>Every <see cref="IDiagnosticLink.Exchange"/> made, in order.</summary>
        public List<(ECUDefinition Ecu, byte[] Request, int EchoLength, int TimeoutMs)> Exchanges { get; } = [];

        /// <summary>Every <see cref="IDiagnosticLink.Send"/> made (no reply awaited), in order.</summary>
        public List<(ECUDefinition Ecu, byte[] Request)> Sent { get; } = [];

        /// <summary>Every raw CAN frame sent, in order.</summary>
        public List<(uint CanId, byte[] Data)> CanFrames { get; } = [];

        public int LinksOpened { get; private set; }
        public int LinksOpen { get; private set; }

        /// <summary>When set, opening a link throws this (no adapter, adapter busy).</summary>
        public Exception? OpenError { get; set; }

        /// <summary>When set, raw CAN sends to this ID throw, as when the adapter refuses a frame.</summary>
        public uint? RefuseCanId { get; set; }

        /// <summary>
        /// The ECU answers <paramref name="request"/> with <paramref name="response"/> (service ID
        /// onwards; the CAN ID header is added).
        /// </summary>
        public ScriptedLinkProvider Reply(byte[] request, params byte[] response) =>
            Reply(ECUDefinition.ECM, request, response);

        public ScriptedLinkProvider Reply(ECUDefinition ecu, byte[] request, params byte[] response)
        {
            _replies[(ecu.ResponseId, Convert.ToHexString(request))] = response;
            return this;
        }

        /// <summary>The ECM rejects <paramref name="request"/> with a negative response.</summary>
        public ScriptedLinkProvider Reject(byte[] request, byte nrc) => Reply(request, 0x7F, request[0], nrc);

        /// <summary>The requests exchanged, as hex strings, for compact assertions.</summary>
        public IEnumerable<string> ExchangedHex => Exchanges.Select(e => Convert.ToHexString(e.Request));

        public IDiagnosticLink OpenDiagnostic(ECUDefinition ecu)
        {
            Open();
            return new Link(this, ecu);
        }

        public ICanLink OpenCan()
        {
            Open();
            return new CanLink(this);
        }

        private void Open()
        {
            if (OpenError is not null)
                throw OpenError;
            LinksOpened++;
            LinksOpen++;
        }

        private DiagnosticReply Answer(ECUDefinition ecu, byte[] request, int echoLength)
        {
            if (!_replies.TryGetValue((ecu.ResponseId, Convert.ToHexString(request)), out byte[]? response))
                return DiagnosticReply.Timeout;

            byte[] message = [.. ecu.GetResponseHeader(), .. response];
            return DiagnosticResponseMatcher.Match(ecu.ResponseId, request, echoLength, message, out byte nrc) switch
            {
                DiagnosticMatch.Positive => DiagnosticReply.Positive(message),
                DiagnosticMatch.Negative => DiagnosticReply.Negative(nrc),
                _ => DiagnosticReply.Timeout,
            };
        }

        private sealed class Link(ScriptedLinkProvider owner, ECUDefinition ecu) : IDiagnosticLink
        {
            private bool _disposed;

            public ECUDefinition Ecu { get; } = ecu;

            public DiagnosticReply Exchange(byte[] request, int echoLength = 0,
                int timeoutMs = DiagnosticTiming.DefaultReplyTimeoutMs)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                owner.Exchanges.Add((Ecu, request.ToArray(), echoLength, timeoutMs));
                return owner.Answer(Ecu, request, echoLength);
            }

            public void Send(byte[] request)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                owner.Sent.Add((Ecu, request.ToArray()));
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.LinksOpen--;
            }
        }

        private sealed class CanLink(ScriptedLinkProvider owner) : ICanLink
        {
            private bool _disposed;

            public void Send(uint canId, byte[] data)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (canId == owner.RefuseCanId)
                    throw new InvalidOperationException("adapter refused the frame");
                owner.CanFrames.Add((canId, data.ToArray()));
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.LinksOpen--;
            }
        }
    }
}
