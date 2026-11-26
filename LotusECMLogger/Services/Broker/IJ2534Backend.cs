using SAE.J2534;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>The channel types the broker opens (J2534-session-broker-plan.md §3.1, Design A).</summary>
    public enum BrokerProtocol
    {
        /// <summary>The adapter does ISO-TP (flow control, padding, STmin) at 500 kbaud.</summary>
        Iso15765,
        /// <summary>Raw 11-bit CAN at 500 kbaud.</summary>
        Can,
        /// <summary>
        /// Raw CAN that also receives 29-bit frames (CAN_ID_BOTH), for the bus sniff. Only on an exclusive channel:
        /// the shared CAN channel is 11-bit.
        /// </summary>
        CanWithExtendedIds,
    }

    /// <summary>One message as the adapter delivered it: the 4-byte CAN ID header, then the payload.</summary>
    public readonly record struct ReceivedMessage(byte[] Data, RxFlag RxStatus, uint TimestampMicroseconds);

    /// <summary>
    /// The part of J2534 the broker uses, so its tests can run against a fake adapter. <see cref="J2534SharpBackend"/>
    /// is the real one. Only the broker calls this; services go through leases and links.
    /// </summary>
    public interface IJ2534Backend
    {
        /// <summary>Opens the adapter. Throws <see cref="IOException"/> with a user-facing reason when it cannot.</summary>
        IJ2534Device OpenDevice();
    }

    /// <summary>An open adapter. Disposing it closes its channels and the device.</summary>
    public interface IJ2534Device : IDisposable
    {
        IJ2534Channel OpenChannel(BrokerProtocol protocol);
    }

    /// <summary>One J2534 channel. Calls may come from several threads; the DLL serialises them.</summary>
    public interface IJ2534Channel : IDisposable
    {
        BrokerProtocol Protocol { get; }

        /// <summary>Sends one message (4-byte CAN ID header, then payload). Throws when the adapter refuses it.</summary>
        void Send(byte[] message);

        /// <summary>
        /// Sends several messages in one DLL call, and returns whether the adapter took them; it does not throw.
        /// The OBD logger's loop (plan §3.3) sends this way and has always ignored the result: a refused batch is
        /// one missed poll, and the next one follows at once.
        /// </summary>
        bool SendBatch(IReadOnlyList<byte[]> messages);

        /// <summary>
        /// Reads up to <paramref name="maxMessages"/>. J2534 returns early only once that many have arrived, so a
        /// blocking read (<paramref name="timeoutMs"/> above 0) should ask for one. Zero returns what is buffered.
        /// Returns an empty list when nothing arrived; throws on an adapter failure. J2534-Sharp refuses more than
        /// <see cref="MaxReadMessages"/> per call.
        /// </summary>
        IReadOnlyList<ReceivedMessage> Read(int maxMessages, int timeoutMs);

        /// <summary>The most messages one <see cref="Read"/> may ask for (J2534-Sharp's ReadMessages limit).</summary>
        const int MaxReadMessages = 200;

        /// <summary>Starts a filter and returns its ID. Throws <see cref="FilterLimitException"/> when the channel is full.</summary>
        int StartFilter(MessageFilter filter);

        void StopFilter(int filterId);
    }

    /// <summary>
    /// Counts short DLL calls (sends, zero-timeout reads) in flight on either shared channel. The DLL holds one lock
    /// through a blocking read (plan §1.3), and a reader that re-blocks slice after slice keeps winning it, so the
    /// ISO 15765 reader blocks only while no other call is waiting. Such a call then waits for at most one slice.
    /// Callers count themselves before taking any lock of their own, so a call queued behind another is seen too.
    /// </summary>
    internal sealed class DllCallers
    {
        private int _waiting;

        public bool AnyWaiting => Volatile.Read(ref _waiting) > 0;

        public IDisposable Enter()
        {
            Interlocked.Increment(ref _waiting);
            return new Exit(this);
        }

        private sealed class Exit(DllCallers owner) : IDisposable
        {
            private int _done;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref owner._waiting);
            }
        }
    }

    /// <summary>The channel has no free filter (the OpenPort 2.0 allows 10 per channel, measured 2026-11-25).</summary>
    public sealed class FilterLimitException(string message) : IOException(message);
}
