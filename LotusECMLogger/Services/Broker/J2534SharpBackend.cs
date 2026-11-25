using System.Runtime.InteropServices;
using SAE.J2534;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>
    /// <see cref="IJ2534Backend"/> over J2534-Sharp: the first installed driver and its first device (device
    /// selection is Phase 8). This is the only code in the app that opens a J2534 device.
    /// </summary>
    public sealed class J2534SharpBackend : IJ2534Backend
    {
        public static readonly J2534SharpBackend Instance = new();

        private J2534SharpBackend() { }

        public IJ2534Device OpenDevice()
        {
            var apis = J2534APIFactory.DiscoverAPIs().ToList();
            if (apis.Count == 0)
                throw new IOException("No J2534 driver is installed. Install the driver for your adapter (for example the OpenPort 2.0 driver) and try again.");

            // Never disposed: J2534-Sharp.Core v2's LoadAPI caches one API instance per DLL for the whole process,
            // and disposing it poisons that cache, so the next LoadAPI returns a disposed instance and OpenDevice
            // throws ObjectDisposedException. Disposing the device (PTClose) is what frees the adapter.
            J2534API api = J2534APIFactory.LoadAPI(apis[0].FileName).Unwrap();
            var device = api.OpenDevice("");
            if (device.IsError)
            {
                // The OpenPort 2.0 returns DEVICE_NOT_CONNECTED, with no message, both when it is unplugged and when
                // it is already open (measured 2026-11-25, plan Phase 1), so the message names both. Within this app
                // only the broker opens the device, so "already open" means another program.
                if (device.Status == ResultCode.DEVICE_NOT_CONNECTED)
                    throw new IOException(
                        $"The J2534 adapter ({apis[0].Name}) is not connected, or another program is using it.");
                throw new J2534Exception(device.Status, device.ErrorMessage);
            }
            return new Device(device.Value);
        }

        private sealed class Device : IJ2534Device
        {
            private readonly J2534Device _device;
            private readonly List<Channel> _channels = [];
            private readonly bool _timerRaised;

            public Device(J2534Device device)
            {
                _device = device;
                // Zero-timeout polling with 1 ms sleeps needs the 1 ms timer to poll at 1 ms: at the default 15.6 ms
                // tick a CCP command cost 32 ms on a 2011 Evora (2026-10-24), 20 ms at 1 ms, 2 ms once polled.
                _timerRaised = timeBeginPeriod(1) == 0;
            }

            public IJ2534Channel OpenChannel(BrokerProtocol protocol)
            {
                var channel = protocol switch
                {
                    BrokerProtocol.Iso15765 => _device.OpenChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE),
                    BrokerProtocol.Can => _device.OpenChannel(Protocol.CAN, Baud.CAN, ConnectFlag.NONE),
                    BrokerProtocol.CanWithExtendedIds => _device.OpenChannel(Protocol.CAN, Baud.CAN, ConnectFlag.CAN_ID_BOTH),
                    _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
                };
                var opened = new Channel(protocol, channel.Unwrap());
                lock (_channels) _channels.Add(opened);
                return opened;
            }

            public void Dispose()
            {
                lock (_channels)
                {
                    foreach (var channel in _channels) channel.Dispose();
                    _channels.Clear();
                }
                _device.Dispose();
                if (_timerRaised) timeEndPeriod(1);
            }
        }

        private sealed class Channel(BrokerProtocol protocol, J2534Channel channel) : IJ2534Channel
        {
            private int _disposed;

            public BrokerProtocol Protocol { get; } = protocol;

            public void Send(byte[] message) => channel.SendMessage(message).ThrowIfError();

            public bool SendBatch(IReadOnlyList<byte[]> messages)
            {
                var batch = new SAE.J2534.Message[messages.Count];
                for (int i = 0; i < batch.Length; i++) batch[i] = new SAE.J2534.Message(messages[i], channel.DefaultTxFlags);
                return channel.SendMessages(batch).IsSuccess;
            }

            public IReadOnlyList<ReceivedMessage> Read(int maxMessages, int timeoutMs)
            {
                GetMessagesResult read = channel.ReadMessages(maxMessages, timeoutMs);
                // J2534 reports TIMEOUT whenever fewer messages than asked for arrived, so the messages are kept
                // before the status is judged. Anything else means the adapter itself failed.
                if (!read.IsSuccess && !read.IsTimeout && !read.IsBufferEmpty && read.Status != ResultCode.BUFFER_OVERFLOW)
                    throw new J2534Exception(read.Status, $"J2534 read failed: {read.Status}");
                if (read.Messages.Length == 0) return [];
                return Array.ConvertAll(read.Messages, m => new ReceivedMessage(m.Data, m.RxStatus, m.Timestamp));
            }

            public int StartFilter(MessageFilter filter)
            {
                var result = channel.StartMessageFilter(filter);
                if (result.Status == ResultCode.EXCEEDED_LIMIT)
                    throw new FilterLimitException($"The adapter has no free {Protocol} filter.");
                return result.Unwrap();
            }

            public void StopFilter(int filterId) => channel.StopMessageFilter(filterId).ThrowIfError();

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) channel.Dispose();
            }
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
    }
}
