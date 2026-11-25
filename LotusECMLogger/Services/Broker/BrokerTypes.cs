namespace LotusECMLogger.Services.Broker
{
    /// <summary>What a lease needs from the adapter (J2534-session-broker-plan.md §3.4).</summary>
    public enum LeaseKind
    {
        /// <summary>Uses the bus alongside anything else.</summary>
        Shared,
        /// <summary>Nothing else may talk to one ECU while held; other ECUs are unaffected.</summary>
        EcuExclusive,
        /// <summary>The holder gets dedicated channels on the device; every other lease is refused.</summary>
        DeviceExclusive,
        /// <summary>The device is closed so another program can open it; every other lease is refused.</summary>
        DeviceReleased,
    }

    /// <summary>Timing the broker uses. The defaults come from the 2026-11-25 OpenPort 2.0 measurements (plan §4, Phase 1).</summary>
    public sealed class BrokerOptions
    {
        /// <summary>
        /// Minimum gap between ISO 15765 sends while the CAN channel is open: CCP kept its 2 ms latency with OBD at
        /// 200 requests/s and lost replies when OBD ran flat out.
        /// </summary>
        public int IsoSendIntervalWithCanMs { get; init; } = 5;

        /// <summary>
        /// Longest blocking read while a reply is awaited. Blocking reads give 0.8 ms OBD round trips (2 ms polled)
        /// and did not slow CCP; slicing them keeps a failed adapter or a stop request noticed promptly.
        /// </summary>
        public int IsoReplySliceMs { get; init; } = 10;

        /// <summary>Zero-timeout poll period of the ISO 15765 channel while no reply is awaited.</summary>
        public int IsoIdlePollMs { get; init; } = 10;

        /// <summary>CAN poll period while someone asked for fast polling (a CCP command is waiting).</summary>
        public int CanFastPollMs { get; init; } = 1;

        /// <summary>CAN poll period otherwise; broadcasts carry hardware timestamps, so polling slowly loses no timing.</summary>
        public int CanIdlePollMs { get; init; } = 5;

        /// <summary>Per-subscriber queue bound; a full queue drops (and counts) rather than stall the reader.</summary>
        public int StreamCapacity { get; init; } = 4096;
    }

    /// <summary>One lease, as shown in <see cref="BrokerState"/>.</summary>
    public sealed record BrokerLeaseInfo(LeaseKind Kind, string Holder, string? Ecu);

    /// <summary>A snapshot of the broker for the UI: whether the device is open and who holds what.</summary>
    public sealed record BrokerState(bool DeviceOpen, IReadOnlyList<BrokerLeaseInfo> Leases)
    {
        /// <summary>The DeviceExclusive or DeviceReleased lease that blocks every other one, if any.</summary>
        public BrokerLeaseInfo? Blocking =>
            Leases.FirstOrDefault(l => l.Kind is LeaseKind.DeviceExclusive or LeaseKind.DeviceReleased);
    }

    /// <summary>A lease or link was refused because another lease holds what it needs. Never waits.</summary>
    public sealed class BrokerBusyException(string holder, string message) : InvalidOperationException(message)
    {
        /// <summary>Who holds what was asked for, as they named themselves ("OBD-II logging").</summary>
        public string Holder { get; } = holder;
    }
}
