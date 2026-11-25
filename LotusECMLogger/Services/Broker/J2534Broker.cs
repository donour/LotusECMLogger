using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>
    /// Owns the J2534 adapter for the whole application (J2534-session-broker-plan.md, Design A): one device, opened
    /// on first use and closed when the last lease is released, with a shared ISO 15765 channel and a shared raw CAN
    /// channel opened on demand. Conflicts are expressed once, as lease kinds; a lease that cannot be granted is
    /// refused at once with the holder's name, never queued.
    /// <para>Phase 2: the broker exists and is tested against a fake adapter, but the app does not use it yet.</para>
    /// </summary>
    public sealed class J2534Broker : IDisposable
    {
        private readonly IJ2534Backend _backend;
        private readonly BrokerOptions _options;
        private readonly object _sync = new();
        private readonly List<BrokerLease> _leases = [];
        private readonly DllCallers _dll = new();
        private IJ2534Device? _device;
        private SharedIsoChannel? _iso;
        private SharedCanChannel? _can;
        private bool _disposed;

        private static readonly Lazy<J2534Broker> AppBroker = new(() => new J2534Broker());

        /// <summary>
        /// The application's broker, used by the services' parameterless constructors so the designer-built controls
        /// need no wiring. MainWindow disposes it on exit. Tests construct their own broker instead.
        /// </summary>
        public static J2534Broker Shared => AppBroker.Value;

        public J2534Broker() : this(J2534SharpBackend.Instance) { }

        public J2534Broker(IJ2534Backend backend, BrokerOptions? options = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _options = options ?? new BrokerOptions();
        }

        /// <summary>Raised after a lease is granted or released and when the device opens or closes. Any thread.</summary>
        public event EventHandler<BrokerState>? StateChanged;

        public BrokerState State
        {
            get { lock (_sync) return Snapshot(); }
        }

        /// <summary>Takes a Shared, DeviceExclusive or DeviceReleased lease. For EcuExclusive use <see cref="AcquireEcuExclusive"/>.</summary>
        /// <param name="holder">Who is asking, as the user should read it in a refusal ("OBD-II logging").</param>
        /// <exception cref="BrokerBusyException">Another lease holds what this one needs.</exception>
        public BrokerLease Acquire(LeaseKind kind, string holder)
        {
            if (kind == LeaseKind.EcuExclusive)
                throw new ArgumentException("An EcuExclusive lease names its ECU: use AcquireEcuExclusive.", nameof(kind));
            return AcquireCore(kind, holder, null, releaseWithLastLink: false);
        }

        /// <summary>Takes one ECU to itself: other leases may not open links to it until this is released.</summary>
        /// <exception cref="BrokerBusyException">Another lease holds the device, holds the ECU, or has a link open to it.</exception>
        public BrokerLease AcquireEcuExclusive(ECUDefinition ecu, string holder)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            return AcquireCore(LeaseKind.EcuExclusive, holder, ecu, releaseWithLastLink: false);
        }

        /// <summary>
        /// An <see cref="ILinkProvider"/> whose every link takes its own Shared lease for as long as it is open, for
        /// the one-shot services built on the Phase 0 seam.
        /// </summary>
        public ILinkProvider Links(string holder) => new PerLinkProvider(this, holder, null);

        /// <summary>As <see cref="Links"/>, but each link holds <paramref name="ecu"/> exclusively (a DTC clear, a VIN write).</summary>
        public ILinkProvider EcuExclusiveLinks(ECUDefinition ecu, string holder) => new PerLinkProvider(this, holder, ecu);

        private BrokerLease AcquireCore(LeaseKind kind, string holder, ECUDefinition? ecu, bool releaseWithLastLink)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(holder);
            BrokerState state;
            BrokerLease lease;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (FindBlocker(kind, ecu) is BrokerLease blocker)
                    throw Busy(blocker, ecu?.Name);
                lease = new BrokerLease(this, kind, holder, ecu, releaseWithLastLink);
                _leases.Add(lease);
                state = Snapshot();
            }
            StateChanged?.Invoke(this, state);
            return lease;
        }

        private BrokerLease? FindBlocker(LeaseKind kind, ECUDefinition? ecu)
        {
            var whole = _leases.FirstOrDefault(l => l.Kind is LeaseKind.DeviceExclusive or LeaseKind.DeviceReleased);
            if (whole is not null) return whole;
            if (kind is LeaseKind.DeviceExclusive or LeaseKind.DeviceReleased) return _leases.FirstOrDefault();
            if (kind == LeaseKind.EcuExclusive)
            {
                uint key = Key(ecu!);
                return _leases.FirstOrDefault(l =>
                    (l.Kind == LeaseKind.EcuExclusive && Key(l.Ecu!) == key) || l.EcusInUse.ContainsKey(key));
            }
            return null;
        }

        private static BrokerBusyException Busy(BrokerLease blocker, string? ecuName)
        {
            string message = blocker.Kind switch
            {
                LeaseKind.DeviceReleased => $"The adapter is closed while {blocker.Holder} runs.",
                LeaseKind.EcuExclusive => $"The {blocker.Ecu!.Name} is in use: {blocker.Holder} is running.",
                _ when ecuName is not null && blocker.Kind == LeaseKind.Shared =>
                    $"The {ecuName} is in use: {blocker.Holder} is running.",
                _ => $"The adapter is in use: {blocker.Holder} is running.",
            };
            return new BrokerBusyException(blocker.Holder, message);
        }

        // ECUs are told apart by request ID: the TCM and the AEM wideband share 0x7E1, so holding one holds both.
        private static uint Key(ECUDefinition ecu) => ecu.RequestId;

        internal IDiagnosticLink OpenDiagnostic(BrokerLease lease, ECUDefinition ecu)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            bool opened;
            BrokerDiagnosticLink link;
            lock (_sync)
            {
                CheckSharedUse(lease, ecu);
                opened = EnsureDevice();
                _iso ??= new SharedIsoChannel(_device!.OpenChannel(BrokerProtocol.Iso15765), _options,
                    () => Volatile.Read(ref _can) is not null, _dll);
                _iso.AddEcu(ecu);
                link = new BrokerDiagnosticLink(this, lease, _iso, ecu);
                lease.Opened.Add(link);
                Use(lease, ecu);
            }
            if (opened) RaiseState();
            return link;
        }

        internal BrokerCanLink OpenCan(BrokerLease lease, ECUDefinition? talksTo)
        {
            bool opened;
            BrokerCanLink link;
            lock (_sync)
            {
                CheckSharedUse(lease, talksTo);
                opened = EnsureDevice();
                if (_can is null)
                    Volatile.Write(ref _can, new SharedCanChannel(_device!.OpenChannel(BrokerProtocol.Can), _options, _dll));
                link = new BrokerCanLink(this, lease, _can!, talksTo);
                lease.Opened.Add(link);
                if (talksTo is not null) Use(lease, talksTo);
            }
            if (opened) RaiseState();
            return link;
        }

        internal IJ2534Channel OpenExclusiveChannel(BrokerLease lease, BrokerProtocol protocol)
        {
            bool opened;
            IJ2534Channel channel;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(lease.Released, lease);
                if (lease.Kind != LeaseKind.DeviceExclusive)
                    throw new InvalidOperationException("Only a DeviceExclusive lease gets a channel of its own.");
                // No other lease can be held, so the shared channels are closed and no reader touches this one.
                opened = EnsureDevice();
                channel = _device!.OpenChannel(protocol);
                lease.Opened.Add(channel);
            }
            if (opened) RaiseState();
            return channel;
        }

        private void CheckSharedUse(BrokerLease lease, ECUDefinition? ecu)
        {
            ObjectDisposedException.ThrowIf(lease.Released, lease);
            if (lease.Kind == LeaseKind.DeviceReleased)
                throw new InvalidOperationException("A DeviceReleased lease keeps the adapter closed.");
            if (lease.Kind == LeaseKind.DeviceExclusive)
                throw new InvalidOperationException("A DeviceExclusive lease uses its own channels (OpenExclusiveChannel).");
            if (ecu is null) return;
            uint key = Key(ecu);
            var holder = _leases.FirstOrDefault(l => l != lease && l.Kind == LeaseKind.EcuExclusive && Key(l.Ecu!) == key);
            if (holder is not null) throw Busy(holder, ecu.Name);
        }

        private static void Use(BrokerLease lease, ECUDefinition ecu)
        {
            uint key = Key(ecu);
            lease.EcusInUse[key] = lease.EcusInUse.GetValueOrDefault(key) + 1;
        }

        internal void CloseLink(BrokerLease lease, IDisposable link, ECUDefinition? ecu)
        {
            bool releaseLease;
            lock (_sync)
            {
                if (!lease.Opened.Remove(link)) return;
                CloseOpened(link);
                if (ecu is not null) Unuse(lease, ecu);
                releaseLease = lease.ReleaseWithLastLink && lease.Opened.Count == 0;
            }
            if (releaseLease) Release(lease);
        }

        private static void Unuse(BrokerLease lease, ECUDefinition ecu)
        {
            uint key = Key(ecu);
            int count = lease.EcusInUse.GetValueOrDefault(key) - 1;
            if (count > 0) lease.EcusInUse[key] = count;
            else lease.EcusInUse.Remove(key);
        }

        private static void CloseOpened(IDisposable item)
        {
            switch (item)
            {
                case BrokerDiagnosticLink diagnostic: diagnostic.Close(); break;
                case BrokerCanLink can: can.Close(); break;
                default: item.Dispose(); break; // an exclusive channel
            }
        }

        internal void Release(BrokerLease lease)
        {
            BrokerState state;
            lock (_sync)
            {
                if (lease.Released) return;
                lease.Released = true;
                foreach (var item in lease.Opened) CloseOpened(item);
                lease.Opened.Clear();
                lease.EcusInUse.Clear();
                _leases.Remove(lease);
                if (_leases.Count == 0) CloseDevice();
                state = Snapshot();
            }
            StateChanged?.Invoke(this, state);
        }

        /// <summary>Opens the device if it is closed; true when it was opened now.</summary>
        private bool EnsureDevice()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_device is not null) return false;
            _device = _backend.OpenDevice();
            return true;
        }

        private void CloseDevice()
        {
            var iso = _iso;
            var can = _can;
            _iso = null;
            Volatile.Write(ref _can, null);
            iso?.Dispose();
            can?.Dispose();
            _device?.Dispose();
            _device = null;
        }

        private void RaiseState()
        {
            BrokerState state;
            lock (_sync) state = Snapshot();
            StateChanged?.Invoke(this, state);
        }

        private BrokerState Snapshot() => new(_device is not null, [.. _leases.Select(l => l.Info)]);

        public void Dispose()
        {
            BrokerLease[] leases;
            lock (_sync)
            {
                if (_disposed) return;
                leases = [.. _leases];
            }
            foreach (var lease in leases) Release(lease);
            lock (_sync)
            {
                _disposed = true;
                CloseDevice();
            }
        }

        private sealed class PerLinkProvider(J2534Broker broker, string holder, ECUDefinition? exclusiveEcu) : ILinkProvider
        {
            public IDiagnosticLink OpenDiagnostic(ECUDefinition ecu)
            {
                var lease = Take();
                try { return lease.OpenDiagnostic(ecu); }
                catch { lease.Dispose(); throw; }
            }

            public ICanLink OpenCan()
            {
                var lease = Take();
                try { return lease.OpenCan(exclusiveEcu); }
                catch { lease.Dispose(); throw; }
            }

            private BrokerLease Take() => exclusiveEcu is null
                ? broker.AcquireCore(LeaseKind.Shared, holder, null, releaseWithLastLink: true)
                : broker.AcquireCore(LeaseKind.EcuExclusive, holder, exclusiveEcu, releaseWithLastLink: true);
        }
    }
}
