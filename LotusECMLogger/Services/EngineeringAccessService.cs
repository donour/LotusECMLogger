using System.Threading;
using System.Threading.Tasks;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// J2534-backed implementation of <see cref="IEngineeringAccessService"/>. Each operation opens
    /// a short-lived raw CAN channel with a pass-all filter, delegates to the T6e clients, and
    /// disposes the session — mirroring the temporary-session pattern in T6RMAService.
    /// </summary>
    public sealed class EngineeringAccessService : IEngineeringAccessService
    {
        private const int ToolingChunk = 5; // tooling channel moves at most five bytes per frame

        public IReadOnlyList<EcuMemoryProfile> AvailableProfiles { get; } =
            [EcuMemoryProfile.Evora, EcuMemoryProfile.Emira];

        public Task<UnlockProbeReport> ProbeAsync(EcuMemoryProfile profile, CancellationToken ct = default)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            return RunWithChannelAsync(async channel =>
            {
                var helper = new T6eUnlockHelper(channel, profile);
                return await helper.ProbeAsync(ct);
            }, ct);
        }

        public Task WriteAsync(EcuMemoryProfile profile, EngineeringWriteTransport transport, uint address, byte[] data, CancellationToken ct = default)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            if (data is null || data.Length == 0) throw new ArgumentException("No bytes to write.", nameof(data));

            return RunWithChannelAsync<object?>(async channel =>
            {
                switch (transport)
                {
                    case EngineeringWriteTransport.RmaFamily:
                        var rma = new T6eRMAClient(channel, profile) { AllowWrites = true };
                        await rma.Write(address, data, ct);
                        break;

                    case EngineeringWriteTransport.ToolingChannel:
                        var tooling = new T6eToolingClient(channel, profile) { AllowWrites = true };
                        for (int offset = 0; offset < data.Length; offset += ToolingChunk)
                        {
                            int len = Math.Min(ToolingChunk, data.Length - offset);
                            await tooling.WriteAsync((uint)(address + offset), data.AsMemory(offset, len), ct);
                        }
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(transport));
                }
                return null;
            }, ct);
        }

        public Task ApplyCalibrationMagicAsync(EcuMemoryProfile profile, CancellationToken ct = default)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            if (profile.CalibrationMagic.Length == 0)
                throw new InvalidOperationException($"No calibration magic pattern is recovered for {profile.Name}.");

            return RunWithChannelAsync<object?>(async channel =>
            {
                var helper = new T6eUnlockHelper(channel, profile);
                var writableTooling = new T6eToolingClient(channel, profile) { AllowWrites = true };
                await helper.ApplyCalibrationMagicAsync(writableTooling, confirm: true, ct);
                return null;
            }, ct);
        }

        // Opens a temporary session + raw CAN channel with a pass-all filter, runs the body off the
        // UI thread, and always disposes the session.
        private static Task<T> RunWithChannelAsync<T>(Func<J2534Channel, Task<T>> body, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenCan();

                var passAll = new MessageFilter
                {
                    FilterType = Filter.PASS_FILTER,
                    Mask = [0x00, 0x00, 0x00, 0x00],
                    Pattern = [0x00, 0x00, 0x00, 0x00],
                };
                channel.StartMessageFilter(passAll).ThrowIfError();

                return await body(channel);
            }, ct);
        }
    }
}
