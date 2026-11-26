using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using SAE.J2534;

namespace LotusECMLogger.Tests.Fakes
{
    /// <summary>
    /// A J2534 adapter with scripted ECUs, behaving as the OpenPort 2.0 measured on 2026-11-25
    /// (J2534-session-broker-plan.md, Phase 1):
    /// <list type="bullet">
    /// <item>one DLL lock for every call, held through a blocking read, so a send waits out a read (plan §1.3);</item>
    /// <item>10 filters per channel, refused with <see cref="FilterLimitException"/> past that;</item>
    /// <item>a second open while the device is open fails with DEVICE_NOT_CONNECTED;</item>
    /// <item>a CAN channel whose filters pass 0x7E0/0x7E8 also receives the ISO 15765 traffic.</item>
    /// </list>
    /// </summary>
    internal sealed class FakeJ2534Backend : IJ2534Backend
    {
        private readonly object _dllLock = new();
        private readonly ConcurrentDictionary<(uint RequestId, string Request), (ECUDefinition Ecu, (int DelayMs, byte[] Response)[] Replies)> _script = new();
        private readonly List<FakeChannel> _channels = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public int FilterLimit { get; init; } = 10;
        public int DevicesOpened { get; private set; }
        public int DevicesOpen { get; private set; }

        /// <summary>
        /// Called (inside the DLL lock) for every frame sent on a CAN channel, with its ID and data: a test puts an
        /// ECU on the bus by answering with <see cref="InjectCan"/>.
        /// </summary>
        public Action<uint, byte[]>? CanFrameSent { get; set; }

        /// <summary>Thrown by the next open instead of opening (no adapter).</summary>
        public Exception? OpenError { get; set; }

        /// <summary>Every channel opened, in order, including closed ones.</summary>
        public IReadOnlyList<FakeChannel> Channels { get { lock (_channels) return [.. _channels]; } }

        public IEnumerable<FakeChannel> OpenChannels(BrokerProtocol protocol) =>
            Channels.Where(c => c.Protocol == protocol && !c.Closed);

        /// <summary>Every open raw CAN channel, 11-bit or CAN_ID_BOTH: what a frame on the bus reaches.</summary>
        private IEnumerable<FakeChannel> OpenCanChannels() =>
            Channels.Where(c => c.Protocol is BrokerProtocol.Can or BrokerProtocol.CanWithExtendedIds && !c.Closed);

        /// <summary>
        /// The ECU answers <paramref name="request"/> (service ID onwards) with each reply in turn, each after its
        /// delay from the request.
        /// </summary>
        public FakeJ2534Backend Reply(ECUDefinition ecu, byte[] request, params (int DelayMs, byte[] Response)[] replies)
        {
            _script[(ecu.RequestId, Convert.ToHexString(request))] = (ecu, replies);
            return this;
        }

        public FakeJ2534Backend Reply(ECUDefinition ecu, byte[] request, byte[] response) =>
            Reply(ecu, request, (0, response));

        /// <summary>A frame arrives on the bus: delivered to every open CAN channel whose filters pass it.</summary>
        public void InjectCan(uint canId, byte[] data, RxFlag flags = RxFlag.NONE)
        {
            byte[] message = [0x00, 0x00, (byte)(canId >> 8), (byte)canId, .. data];
            foreach (var channel in OpenCanChannels())
                channel.Deliver(message, flags, passFiltersOnly: true);
        }

        public IJ2534Device OpenDevice()
        {
            if (OpenError is Exception error) throw error;
            lock (_dllLock)
            {
                if (DevicesOpen > 0) throw new J2534Exception(ResultCode.DEVICE_NOT_CONNECTED, "");
                DevicesOpened++;
                DevicesOpen++;
            }
            return new FakeDevice(this);
        }

        private uint Now => (uint)(_clock.Elapsed.TotalMilliseconds * 1000);

        private void OnIsoSend(byte[] message)
        {
            uint requestId = BinaryPrimitives.ReadUInt32BigEndian(message);
            byte[] request = message[4..];

            // The request also crosses the bus, so a CAN channel passing 0x7E0 sees it.
            DeliverToCan(message);

            if (!_script.TryGetValue((requestId, Convert.ToHexString(request)), out var entry)) return;
            foreach (var (delayMs, response) in entry.Replies)
            {
                byte[] reply = [.. entry.Ecu.GetResponseHeader(), .. response];
                if (delayMs <= 0) DeliverIsoReply(reply);
                else _ = Task.Delay(delayMs).ContinueWith(_ => DeliverIsoReply(reply), TaskScheduler.Default);
            }
        }

        private void DeliverIsoReply(byte[] reply)
        {
            foreach (var channel in OpenChannels(BrokerProtocol.Iso15765))
                channel.Deliver(reply, RxFlag.NONE, passFiltersOnly: false);
            DeliverToCan(reply);
        }

        private void DeliverToCan(byte[] isoMessage)
        {
            // A single frame on the wire: ID, PCI length, payload (padding omitted).
            if (isoMessage.Length - 4 > 7) return;
            byte[] frame = [.. isoMessage[..4], (byte)(isoMessage.Length - 4), .. isoMessage[4..]];
            foreach (var channel in OpenCanChannels())
                channel.Deliver(frame, RxFlag.NONE, passFiltersOnly: true);
        }

        // As J2534SharpBackend does while its device is open: without it a 1 ms sleep lasts a 15.6 ms timer tick.
        [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

        private sealed class FakeDevice : IJ2534Device
        {
            private readonly FakeJ2534Backend owner;
            private readonly List<FakeChannel> _mine = [];
            private readonly bool _timerRaised;
            private bool _closed;

            public FakeDevice(FakeJ2534Backend owner)
            {
                this.owner = owner;
                _timerRaised = timeBeginPeriod(1) == 0;
            }

            public IJ2534Channel OpenChannel(BrokerProtocol protocol)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                var channel = new FakeChannel(owner, protocol);
                lock (owner._channels) owner._channels.Add(channel);
                _mine.Add(channel);
                return channel;
            }

            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                foreach (var channel in _mine) channel.Dispose();
                lock (owner._dllLock) owner.DevicesOpen--;
                if (_timerRaised) timeEndPeriod(1);
            }
        }

        /// <summary>One fake channel. Records what the broker did to it.</summary>
        internal sealed class FakeChannel(FakeJ2534Backend owner, BrokerProtocol protocol) : IJ2534Channel
        {
            private readonly Queue<ReceivedMessage> _rx = new();
            private readonly Dictionary<int, MessageFilter> _filters = [];
            private int _nextFilterId = 1;

            public BrokerProtocol Protocol { get; } = protocol;
            public bool Closed { get; private set; }

            /// <summary>Every message sent, with the fake clock's millisecond time.</summary>
            public ConcurrentQueue<(double AtMs, byte[] Message)> Sent { get; } = new();

            /// <summary>Every read: how many messages were asked for and the timeout.</summary>
            public ConcurrentQueue<(int Max, int TimeoutMs)> Reads { get; } = new();

            public int FilterCount { get { lock (_filters) return _filters.Count; } }

            /// <summary>When set, the next read throws it, as an unplugged adapter does.</summary>
            public Exception? ReadError { get; set; }

            public void Send(byte[] message)
            {
                lock (owner._dllLock)
                {
                    ObjectDisposedException.ThrowIf(Closed, this);
                    Sent.Enqueue((owner._clock.Elapsed.TotalMilliseconds, message));
                    if (Protocol == BrokerProtocol.Iso15765) owner.OnIsoSend(message);
                    else owner.CanFrameSent?.Invoke(BinaryPrimitives.ReadUInt32BigEndian(message), message[4..]);
                }
            }

            /// <summary>The size of every <see cref="SendBatch"/>, in order.</summary>
            public ConcurrentQueue<int> Batches { get; } = new();

            public bool SendBatch(IReadOnlyList<byte[]> messages)
            {
                lock (owner._dllLock) // one DLL call for the batch; Send re-enters the lock
                {
                    Batches.Enqueue(messages.Count);
                    foreach (var message in messages) Send(message);
                }
                return true;
            }

            public IReadOnlyList<ReceivedMessage> Read(int maxMessages, int timeoutMs)
            {
                Reads.Enqueue((maxMessages, timeoutMs));
                // J2534-Sharp's own check, before any DLL call.
                ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxMessages, IJ2534Channel.MaxReadMessages);
                lock (owner._dllLock) // held through the wait, as the real DLL does
                {
                    ObjectDisposedException.ThrowIf(Closed, this);
                    if (ReadError is Exception error) throw error;
                    long deadline = Environment.TickCount64 + timeoutMs;
                    while (true)
                    {
                        lock (_rx)
                        {
                            if (_rx.Count >= maxMessages || (_rx.Count > 0 && Environment.TickCount64 >= deadline) ||
                                (_rx.Count > 0 && timeoutMs == 0))
                            {
                                var batch = new List<ReceivedMessage>();
                                while (batch.Count < maxMessages && _rx.Count > 0) batch.Add(_rx.Dequeue());
                                return batch;
                            }
                        }
                        if (Environment.TickCount64 >= deadline) return [];
                        Thread.Sleep(1);
                    }
                }
            }

            public int StartFilter(MessageFilter filter)
            {
                lock (_filters)
                {
                    if (_filters.Count >= owner.FilterLimit) throw new FilterLimitException("fake: EXCEEDED_LIMIT");
                    int id = _nextFilterId++;
                    _filters[id] = filter;
                    return id;
                }
            }

            public void StopFilter(int filterId)
            {
                lock (_filters) _filters.Remove(filterId);
            }

            internal void Deliver(byte[] message, RxFlag flags, bool passFiltersOnly)
            {
                if (Closed || !Passes(message, passFiltersOnly)) return;
                lock (_rx) _rx.Enqueue(new ReceivedMessage(message, flags, owner.Now));
            }

            private bool Passes(byte[] message, bool passFiltersOnly)
            {
                lock (_filters)
                {
                    foreach (var filter in _filters.Values)
                    {
                        if (!passFiltersOnly && filter.FilterType == Filter.FLOW_CONTROL_FILTER &&
                            filter.Pattern.AsSpan().SequenceEqual(message.AsSpan(0, 4)))
                            return true;
                        if (filter.FilterType == Filter.PASS_FILTER && Masked(message, filter))
                            return true;
                    }
                }
                return false;
            }

            private static bool Masked(byte[] message, MessageFilter filter)
            {
                for (int i = 0; i < 4; i++)
                    if ((message[i] & filter.Mask[i]) != (filter.Pattern[i] & filter.Mask[i])) return false;
                return true;
            }

            public void Dispose() => Closed = true;
        }
    }
}
