using System.Threading;
using System.Threading.Tasks;

namespace LotusECMLogger.Services
{
    /// <summary>Which protocol a UI-initiated engineering write should use.</summary>
    public enum EngineeringWriteTransport
    {
        /// <summary>Proprietary tooling channel (0x202). Works even when the ECU is locked.</summary>
        ToolingChannel,

        /// <summary>Raw RMA engineering family (0x40-0x47 / 0x50-0x57). Serviced only when unlocked.</summary>
        RmaFamily,
    }

    /// <summary>
    /// Runs the read-only engineering-access probes and — when the caller explicitly opts in —
    /// the write operations, against a chosen ECU profile. Opens and closes its own J2534 CAN
    /// session per call, so it must not run while the main logger owns the device.
    /// </summary>
    public interface IEngineeringAccessService
    {
        /// <summary>The ECU profiles the UI can target.</summary>
        IReadOnlyList<EcuMemoryProfile> AvailableProfiles { get; }

        /// <summary>Opens a temporary CAN channel, runs the read-only probes, and returns the report.</summary>
        Task<UnlockProbeReport> ProbeAsync(EcuMemoryProfile profile, CancellationToken ct = default);

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="address"/> on the ECU using the chosen
        /// transport. This is a live memory write; the caller is responsible for confirmation.
        /// </summary>
        Task WriteAsync(EcuMemoryProfile profile, EngineeringWriteTransport transport, uint address, byte[] data, CancellationToken ct = default);

        /// <summary>
        /// Writes each of the profile's calibration-shadow magic bytes to its expected value via the
        /// tooling channel. Per the firmware analysis this does NOT re-run the boot predicate or
        /// re-enable the mailbox interrupt in the same session — it exists to observe that behaviour.
        /// Throws if the profile has no recovered magic pattern.
        /// </summary>
        Task ApplyCalibrationMagicAsync(EcuMemoryProfile profile, CancellationToken ct = default);
    }
}
