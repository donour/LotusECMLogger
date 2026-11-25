using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>
    /// One holder's claim on the adapter (plan §3.4). A Shared or EcuExclusive lease opens links on the broker's
    /// shared channels; a DeviceExclusive lease opens dedicated channels instead. Disposing it closes everything it
    /// opened and, with the last lease, the device.
    /// </summary>
    public sealed class BrokerLease : ILinkProvider, IDisposable
    {
        private readonly J2534Broker _broker;
        // Guarded by the broker's lock.
        internal readonly List<IDisposable> Opened = [];
        internal readonly Dictionary<uint, int> EcusInUse = [];
        internal bool Released;

        internal BrokerLease(J2534Broker broker, LeaseKind kind, string holder, ECUDefinition? ecu, bool releaseWithLastLink)
        {
            _broker = broker;
            Kind = kind;
            Holder = holder;
            Ecu = ecu;
            ReleaseWithLastLink = releaseWithLastLink;
        }

        public LeaseKind Kind { get; }
        public string Holder { get; }
        /// <summary>The ECU an EcuExclusive lease holds; null for the other kinds.</summary>
        public ECUDefinition? Ecu { get; }
        /// <summary>A lease taken for a single link (<see cref="J2534Broker.Links"/>) ends with that link.</summary>
        internal bool ReleaseWithLastLink { get; }

        internal BrokerLeaseInfo Info => new(Kind, Holder, Ecu?.Name);

        /// <summary>An ISO 15765 link to one ECU on the shared channel.</summary>
        /// <exception cref="BrokerBusyException">Another lease holds that ECU exclusively.</exception>
        public IDiagnosticLink OpenDiagnostic(ECUDefinition ecu) => _broker.OpenDiagnostic(this, ecu);

        /// <summary>A raw CAN link on the shared channel. <paramref name="talksTo"/> names the ECU it addresses, if
        /// any (the ECM for CCP), so an EcuExclusive lease on that ECU refuses it.</summary>
        public BrokerCanLink OpenCan(ECUDefinition? talksTo) => _broker.OpenCan(this, talksTo);

        ICanLink ILinkProvider.OpenCan() => OpenCan(null);

        /// <summary>
        /// A channel of its own on the broker's device, for a DeviceExclusive holder to drive directly (the OBD
        /// logger's loop, plan §3.3). The broker does not read it; it is closed when the lease is released.
        /// </summary>
        public IJ2534Channel OpenExclusiveChannel(BrokerProtocol protocol) => _broker.OpenExclusiveChannel(this, protocol);

        public void Dispose() => _broker.Release(this);
    }

    /// <summary>An <see cref="IDiagnosticLink"/> on the broker's shared ISO 15765 channel.</summary>
    internal sealed class BrokerDiagnosticLink : IDiagnosticLink
    {
        private readonly J2534Broker _broker;
        private readonly BrokerLease _lease;
        private readonly SharedIsoChannel _channel;
        private int _disposed;

        internal BrokerDiagnosticLink(J2534Broker broker, BrokerLease lease, SharedIsoChannel channel, ECUDefinition ecu)
        {
            _broker = broker;
            _lease = lease;
            _channel = channel;
            Ecu = ecu;
        }

        public ECUDefinition Ecu { get; }

        public DiagnosticReply Exchange(byte[] request, int echoLength = 0,
            int timeoutMs = DiagnosticTiming.DefaultReplyTimeoutMs)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ArgumentOutOfRangeException.ThrowIfNegative(echoLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(echoLength, request.Length - 1);
            return _channel.Exchange(Ecu, request, echoLength, timeoutMs);
        }

        public void Send(byte[] request)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _channel.Send(Ecu, request);
        }

        /// <summary>Called by the broker, under its lock, when the link or its lease closes.</summary>
        internal void Close()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _channel.RemoveEcu(Ecu);
        }

        public void Dispose() => _broker.CloseLink(_lease, this, Ecu);
    }

    /// <summary>
    /// An <see cref="ICanLink"/> on the broker's shared CAN channel, which can also receive by CAN ID. Its
    /// subscriptions end when it is disposed.
    /// </summary>
    public sealed class BrokerCanLink : ICanLink
    {
        private readonly J2534Broker _broker;
        private readonly BrokerLease _lease;
        private readonly SharedCanChannel _channel;
        private readonly List<IDisposable> _owned = [];
        private int _disposed;

        internal BrokerCanLink(J2534Broker broker, BrokerLease lease, SharedCanChannel channel, ECUDefinition? talksTo)
        {
            _broker = broker;
            _lease = lease;
            _channel = channel;
            TalksTo = talksTo;
        }

        /// <summary>The ECU this link addresses, if it declared one.</summary>
        public ECUDefinition? TalksTo { get; }

        public void Send(uint canId, byte[] data)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _channel.Send(canId, data);
        }

        /// <summary>Receives frames with this 11-bit ID until the stream or the link is disposed.</summary>
        /// <exception cref="IOException">The adapter has no free CAN filter.</exception>
        public CcpStream<BrokerCanFrame> Subscribe(uint canId)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var stream = _channel.Subscribe(canId);
            lock (_owned) _owned.Add(stream);
            return stream;
        }

        /// <summary>Adds an ID to one of this link's subscriptions; its frames join the same ordered stream.</summary>
        /// <exception cref="IOException">The adapter has no free CAN filter.</exception>
        public void AddId(CcpStream<BrokerCanFrame> subscription, uint canId)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lock (_owned)
            {
                if (!_owned.Contains(subscription))
                    throw new ArgumentException("The subscription belongs to another link.", nameof(subscription));
            }
            _channel.AddId(subscription, canId);
        }

        /// <summary>Polls the CAN channel every 1 ms until disposed: hold it while a reply is awaited.</summary>
        public IDisposable PollFast()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var demand = _channel.PollFast();
            lock (_owned) _owned.Add(demand);
            return demand;
        }

        internal void Close()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            IDisposable[] owned;
            lock (_owned)
            {
                owned = [.. _owned];
                _owned.Clear();
            }
            foreach (var item in owned) item.Dispose();
        }

        public void Dispose() => _broker.CloseLink(_lease, this, TalksTo);
    }
}
