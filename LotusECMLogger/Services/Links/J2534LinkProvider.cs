using SAE.J2534;
using System.Diagnostics;

namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// Opens a J2534 session per link, exactly as the services did before the link interfaces
    /// existed: one device, one channel, closed when the link is disposed. Phase 0 of
    /// J2534-session-broker-plan.md; the broker replaces it later.
    /// </summary>
    public sealed class J2534LinkProvider : ILinkProvider
    {
        public static readonly J2534LinkProvider Instance = new();

        private J2534LinkProvider() { }

        public IDiagnosticLink OpenDiagnostic(ECUDefinition ecu)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            var session = J2534Session.Open();
            try
            {
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ecu.CreateFlowControlFilter()).ThrowIfError();
                return new J2534DiagnosticLink(session, channel, ecu);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public ICanLink OpenCan()
        {
            var session = J2534Session.Open();
            try
            {
                return new J2534CanLink(session, session.OpenCan());
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
    }

    /// <summary>An ISO 15765 channel to one ECU, on a J2534 session the link owns.</summary>
    internal sealed class J2534DiagnosticLink(J2534Session session, J2534Channel channel, ECUDefinition ecu)
        : IDiagnosticLink
    {
        public ECUDefinition Ecu { get; } = ecu;

        public DiagnosticReply Exchange(byte[] request, int echoLength = 0,
            int timeoutMs = DiagnosticTiming.DefaultReplyTimeoutMs)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);
            Send(request);

            var clock = Stopwatch.StartNew();
            long deadline = Math.Min(timeoutMs, DiagnosticTiming.MaxExchangeMs);
            while (clock.ElapsedMilliseconds < deadline)
            {
                int remaining = (int)Math.Max(1, deadline - clock.ElapsedMilliseconds);
                GetMessagesResult read = channel.ReadMessages(1, remaining);

                // J2534 reports TIMEOUT whenever fewer messages than requested arrived, so the
                // messages are consumed before the status is judged.
                foreach (var message in read.Messages)
                {
                    switch (DiagnosticResponseMatcher.Match(Ecu.ResponseId, request, echoLength, message.Data,
                        out byte nrc))
                    {
                        case DiagnosticMatch.Positive:
                            return DiagnosticReply.Positive(message.Data);
                        case DiagnosticMatch.Negative:
                            return DiagnosticReply.Negative(nrc);
                        case DiagnosticMatch.Pending:
                            deadline = Math.Min(
                                Math.Max(deadline, clock.ElapsedMilliseconds + DiagnosticTiming.ResponsePendingExtensionMs),
                                DiagnosticTiming.MaxExchangeMs);
                            break;
                    }
                }

                // Anything other than "nothing (more) arrived" means the adapter itself failed (for
                // example it was unplugged), which an empty read would otherwise disguise as silence.
                if (!read.IsSuccess && !read.IsTimeout && !read.IsBufferEmpty &&
                    read.Status != ResultCode.BUFFER_OVERFLOW)
                {
                    throw new J2534Exception(read.Status, $"J2534 read failed: {read.Status}");
                }
            }

            return DiagnosticReply.Timeout;
        }

        public void Send(byte[] request)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.Length, 1);
            channel.SendMessage(Frame(Ecu, request)).ThrowIfError();
        }

        /// <summary>The message handed to the ISO 15765 channel: the ECU's request CAN ID, then the request.</summary>
        internal static byte[] Frame(ECUDefinition ecu, byte[] request) => [.. ecu.GetRequestHeader(), .. request];

        public void Dispose() => session.Dispose();
    }

    /// <summary>A raw CAN channel on a J2534 session the link owns.</summary>
    internal sealed class J2534CanLink(J2534Session session, J2534Channel channel) : ICanLink
    {
        public void Send(uint canId, byte[] data) => channel.SendMessage(Frame(canId, data)).ThrowIfError();

        /// <summary>The message handed to the raw CAN channel: the 11-bit ID as four bytes, then the data.</summary>
        internal static byte[] Frame(uint canId, byte[] data)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(canId, 0x7FFu);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, 8);
            return [0x00, 0x00, (byte)(canId >> 8), (byte)canId, .. data];
        }

        public void Dispose() => session.Dispose();
    }
}
