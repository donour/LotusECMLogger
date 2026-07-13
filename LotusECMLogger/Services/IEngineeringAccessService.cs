using System.Threading;
using System.Threading.Tasks;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Runs the read-only engineering-access probes (RMA engineering family, proprietary tooling
    /// channel, and calibration-shadow predicate) against a chosen ECU profile and returns a
    /// combined report. Opens and closes its own J2534 CAN session per call, so it must not run
    /// while the main logger owns the device.
    /// </summary>
    public interface IEngineeringAccessService
    {
        /// <summary>The ECU profiles the UI can target.</summary>
        IReadOnlyList<EcuMemoryProfile> AvailableProfiles { get; }

        /// <summary>Opens a temporary CAN channel, runs the read-only probes, and returns the report.</summary>
        Task<UnlockProbeReport> ProbeAsync(EcuMemoryProfile profile, CancellationToken ct = default);
    }
}
