namespace LotusECMLogger.Services
{
    /// <summary>Engine type reported in bit 3 of byte B of PID 0x01; it selects what the
    /// non-continuous monitor bits mean.</summary>
    public enum IgnitionType
    {
        Spark,
        Compression,
    }

    /// <summary>
    /// Continuous monitors run all the time the engine runs; non-continuous monitors run once
    /// per drive cycle when their enable conditions are met. Inspection programs judge
    /// readiness on the non-continuous ones only.
    /// </summary>
    public enum MonitorKind
    {
        Continuous,
        NonContinuous,
    }

    /// <summary>The OBD-II readiness monitors defined by SAE J1979 PID 0x01 / 0x41.</summary>
    public enum ReadinessMonitorId
    {
        // Continuous (byte B)
        Misfire,
        FuelSystem,
        ComprehensiveComponents,

        // Non-continuous, spark ignition (bytes C/D)
        Catalyst,
        HeatedCatalyst,
        EvaporativeSystem,
        SecondaryAirSystem,
        GasolinePartFilter,
        OxygenSensor,
        OxygenSensorHeater,

        // Non-continuous, shared by both ignition types (bit 7)
        EgrVvt,

        // Non-continuous, compression ignition (bytes C/D)
        NmhcCatalyst,
        NoxScrAftertreatment,
        BoostPressure,
        ExhaustGasSensor,
        PmFilter,
    }

    public enum MonitorReadiness
    {
        NotSupported,
        Complete,
        Incomplete,
    }

    /// <summary>One readiness monitor's support and completion bits.</summary>
    public sealed record ReadinessMonitor
    {
        public required ReadinessMonitorId Id { get; init; }

        /// <summary>
        /// PID 0x01: the vehicle has this monitor. PID 0x41: the monitor is enabled for the
        /// current drive cycle.
        /// </summary>
        public required bool Supported { get; init; }

        /// <summary>The monitor has run to completion. Meaningless when not supported: an
        /// unsupported monitor's completion bit carries no information.</summary>
        public required bool Complete { get; init; }

        public string Name => EmissionsDecoder.MonitorName(Id);

        public MonitorKind Kind => EmissionsDecoder.MonitorKindOf(Id);

        public MonitorReadiness Readiness =>
            !Supported ? MonitorReadiness.NotSupported
            : Complete ? MonitorReadiness.Complete
            : MonitorReadiness.Incomplete;
    }

    /// <summary>A decoded PID 0x01 (since codes cleared) or PID 0x41 (this drive cycle).</summary>
    public sealed record MonitorStatus
    {
        /// <summary>Malfunction indicator lamp commanded on. Always false for PID 0x41.</summary>
        public bool MilOn { get; init; }

        /// <summary>Emissions-related confirmed DTCs the ECU counts. Always 0 for PID 0x41.</summary>
        public int DtcCount { get; init; }

        public IgnitionType Ignition { get; init; }

        /// <summary>Every monitor defined for <see cref="Ignition"/>, supported or not, in
        /// J1979 bit order (continuous first).</summary>
        public IReadOnlyList<ReadinessMonitor> Monitors { get; init; } = [];
    }

    /// <summary>One of the counters J1979 keeps since codes were cleared or the MIL lit.</summary>
    public sealed record EmissionsCounter
    {
        public required byte Pid { get; init; }
        public required string Name { get; init; }
        public required int Value { get; init; }

        /// <summary>"km", "min", or "" for a plain count.</summary>
        public required string Unit { get; init; }
    }

    /// <summary>Everything one emissions check reads from the ECM.</summary>
    public sealed record EmissionsCheckResult
    {
        /// <summary>PID 0x01: MIL, DTC count and monitor status since codes were last cleared.</summary>
        public MonitorStatus SinceClear { get; init; } = new();

        /// <summary>PID 0x41, or null when the ECU does not support it or the read failed.</summary>
        public MonitorStatus? ThisDriveCycle { get; init; }

        /// <summary>PID 0x1C raw value, or null when unavailable.</summary>
        public byte? ObdStandard { get; init; }

        /// <summary>Mode 09 PID 0x02, or null when the read failed.</summary>
        public string? Vin { get; init; }

        /// <summary>Mode 09 PID 0x04: one calibration ID per calibration the ECU reports; empty
        /// when the read failed.</summary>
        public IReadOnlyList<string> CalibrationIds { get; init; } = [];

        /// <summary>Mode 09 PID 0x06: one calibration verification number per calibration ID, in
        /// the same order; empty when the read failed.</summary>
        public IReadOnlyList<uint> Cvns { get; init; } = [];

        public IReadOnlyList<EmissionsCounter> Counters { get; init; } = [];

        /// <summary>Stored, pending and permanent codes; the pending and permanent reads
        /// carry their own error notes.</summary>
        public DtcReadResult Dtcs { get; init; } = new();

        /// <summary>Non-null when the stored-code read (service 0x03) failed.</summary>
        public string? StoredDtcError { get; init; }

        /// <summary>Non-fatal read failures; the check still produced a result.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    public interface IEmissionsService
    {
        /// <summary>
        /// Reads what an OBD-II inspection station reads from the engine ECU: monitor readiness
        /// (PID 0x01, plus PID 0x41 for the current drive cycle), MIL status, the OBD standard
        /// (PID 0x1C), the since-cleared counters, stored/pending/permanent DTCs, the VIN, and the
        /// calibration IDs and CVNs (Mode 09 PIDs 0x04 / 0x06).
        /// </summary>
        /// <returns>
        /// Success flag, an error message when unsuccessful, and the data read. Only a failure to
        /// read PID 0x01 fails the check; every other read degrades to a warning.
        /// </returns>
        (bool success, string errorMessage, EmissionsCheckResult result) RunCheck();
    }
}
