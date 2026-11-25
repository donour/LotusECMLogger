using LotusECMLogger.Services.Broker;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Follows the J2534 broker's state for one control (J2534-session-broker-plan.md, Phase 7): whether someone
    /// else holds the whole adapter (OBD-II logging, the ABS probe or sniff, the T6 flasher), which refuses every
    /// other lease. Controls disable their adapter buttons while <see cref="Blocked"/>, in place of the
    /// <c>IsLoggerActive</c> flags MainWindow used to push into each of them. Operations that hold one ECU are not
    /// shown here: they are brief, and the broker refuses a conflicting request with the holder's name.
    /// </summary>
    internal sealed class AdapterAvailability
    {
        private readonly Control _owner;
        private readonly Action _changed;
        private readonly J2534Broker _broker;
        private readonly string[] _ownHolders;
        private volatile BrokerLeaseInfo? _blocking;

        private AdapterAvailability(Control owner, Action changed, J2534Broker broker, string[] ownHolders)
        {
            _owner = owner;
            _changed = changed;
            _broker = broker;
            _ownHolders = ownHolders;
            _blocking = Filter(broker.State);
            broker.StateChanged += OnStateChanged;
            owner.Disposed += (_, _) => broker.StateChanged -= OnStateChanged;
        }

        /// <summary>
        /// Starts following <paramref name="broker"/> (the app's broker when omitted) for <paramref name="owner"/>,
        /// calling <paramref name="changed"/> on the UI thread whenever <see cref="Blocked"/> changes.
        /// </summary>
        /// <param name="ownHolders">Holder names of the control's own whole-adapter operations, which do not block it.</param>
        public static AdapterAvailability Track(Control owner, Action changed, J2534Broker? broker = null,
            params string[] ownHolders) =>
            new(owner, changed, broker ?? J2534Broker.Shared, ownHolders);

        /// <summary>True while another holder has the whole adapter.</summary>
        public bool Blocked => _blocking is not null;

        /// <summary>Why the adapter is unavailable, as the broker words its refusals; null when it is not blocked.</summary>
        public string? Reason => _blocking switch
        {
            null => null,
            { Kind: LeaseKind.DeviceReleased } b => $"The adapter is closed while {b.Holder} runs.",
            var b => $"The adapter is in use: {b.Holder} is running.",
        };

        private BrokerLeaseInfo? Filter(BrokerState state) =>
            state.Blocking is { } blocking && !_ownHolders.Contains(blocking.Holder) ? blocking : null;

        private void OnStateChanged(object? sender, BrokerState state)
        {
            var blocking = Filter(state);
            if (Equals(blocking, _blocking)) return;
            _blocking = blocking;

            // Raised on whichever thread changed the broker. Before the handle exists there is nothing on screen to
            // update, and the control reads Blocked when it first lays itself out.
            if (_owner.IsDisposed || !_owner.IsHandleCreated) return;
            try
            {
                _owner.BeginInvoke(_changed);
            }
            catch (InvalidOperationException)
            {
                // The handle went away between the check and the post.
            }
        }
    }
}
