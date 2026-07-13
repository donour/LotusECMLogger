using System.Threading;
using System.Threading.Tasks;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// J2534-backed implementation of <see cref="IEngineeringAccessService"/>. Each probe opens a
    /// short-lived raw CAN channel with a pass-all filter, delegates to <see cref="T6eUnlockHelper"/>,
    /// and disposes the session — mirroring the temporary-session pattern in T6RMAService.
    /// </summary>
    public sealed class EngineeringAccessService : IEngineeringAccessService
    {
        public IReadOnlyList<EcuMemoryProfile> AvailableProfiles { get; } =
            [EcuMemoryProfile.Evora, EcuMemoryProfile.Emira];

        public Task<UnlockProbeReport> ProbeAsync(EcuMemoryProfile profile, CancellationToken ct = default)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));

            // J2534 device open + polling are blocking, so run off the UI thread.
            return Task.Run(async () =>
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenCan();

                // Pass every 11-bit CAN ID; the clients filter to their response IDs in software.
                var passAll = new MessageFilter
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = [0x00, 0x00, 0x00, 0x00],
                    Pattern = [0x00, 0x00, 0x00, 0x00],
                };
                channel.StartMessageFilter(passAll).ThrowIfError();

                var helper = new T6eUnlockHelper(channel, profile);
                return await helper.ProbeAsync(ct);
            }, ct);
        }
    }
}
