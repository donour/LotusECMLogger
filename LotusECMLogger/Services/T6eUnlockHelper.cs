using System.Threading;
using System.Threading.Tasks;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>Result of reading one calibration-shadow magic byte from a live ECU.</summary>
    public readonly record struct MagicByteReading(uint Address, byte Expected, byte? Actual)
    {
        public bool Matches => Actual == Expected;
        public bool Read => Actual.HasValue;
    }

    /// <summary>Snapshot of what a given ECU exposes, gathered by <see cref="T6eUnlockHelper"/>.</summary>
    public sealed record UnlockProbeReport
    {
        public required string ProfileName { get; init; }

        /// <summary>The raw engineering memory family answered a read (ecu_unlocked was true at boot).</summary>
        public bool EngineeringAccessLive { get; init; }

        /// <summary>The proprietary tooling channel answered a version query.</summary>
        public bool ToolingChannelReachable { get; init; }

        /// <summary>Per-byte state of the calibration-shadow predicate (empty if not recovered for this ECU).</summary>
        public IReadOnlyList<MagicByteReading> CalibrationMagic { get; init; } = [];

        /// <summary>All recovered magic bytes are present and match — the boot predicate would pass.</summary>
        public bool CalibrationMagicComplete =>
            CalibrationMagic.Count > 0 && CalibrationMagic.All(b => b.Matches);
    }

    /// <summary>
    /// High-level helper for inspecting the engineering-access state of a T6-family ECU on the
    /// bench. It combines three read-only signals:
    ///   1. does the raw memory family answer (i.e. is <c>ecu_unlocked</c> true) — via RMA;
    ///   2. is the un-gated tooling channel reachable — via a tooling version query;
    ///   3. what do the four calibration-shadow bytes currently hold — read through the tooling
    ///      channel, which the firmware services regardless of the engineering gate.
    ///
    /// It also offers an OPT-IN patch of those four bytes. Per
    /// disassembly/emira/8896915220A_ROW/analysis/ENGINEERING_UNLOCK_PATHS.md, patching the RAM
    /// shadow does NOT re-run the boot predicate or re-enable the mailbox interrupt in the same
    /// session — persistent enablement requires a calibration reflash. The patch here exists so
    /// you can observe that behaviour directly, not because it is expected to unlock live.
    /// </summary>
    public sealed class T6eUnlockHelper
    {
        private readonly EcuMemoryProfile _profile;
        private readonly T6eRMAClient _rma;
        private readonly T6eToolingClient _tooling;

        public T6eUnlockHelper(J2534Channel channel, EcuMemoryProfile profile, TimeSpan? timeout = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _rma = new T6eRMAClient(channel, profile, timeout);
            _tooling = new T6eToolingClient(channel, profile, timeout);
        }

        /// <summary>Runs all three read-only probes and returns a combined report.</summary>
        public async Task<UnlockProbeReport> ProbeAsync(CancellationToken ct = default)
        {
            bool engineering = await _rma.ProbeEngineeringAccessAsync(ct);

            bool tooling;
            try { tooling = await _tooling.GetVersionAsync(ct) is not null; }
            catch { tooling = false; }

            IReadOnlyList<MagicByteReading> magic = tooling
                ? await ReadCalibrationMagicAsync(ct)
                : [];

            return new UnlockProbeReport
            {
                ProfileName = _profile.Name,
                EngineeringAccessLive = engineering,
                ToolingChannelReachable = tooling,
                CalibrationMagic = magic,
            };
        }

        /// <summary>
        /// Reads the four calibration-shadow magic bytes through the tooling channel and reports
        /// whether each matches its expected value. Returns an empty list when the pattern for the
        /// active profile has not been recovered.
        /// </summary>
        public async Task<IReadOnlyList<MagicByteReading>> ReadCalibrationMagicAsync(CancellationToken ct = default)
        {
            var readings = new List<MagicByteReading>();
            foreach (var b in _profile.CalibrationMagic)
            {
                if (_profile.MagicAddress(b) is not uint addr)
                    continue;

                byte? actual;
                try
                {
                    byte[] bytes = await _tooling.ReadAsync(addr, 1, ct);
                    actual = bytes.Length > 0 ? bytes[0] : null;
                }
                catch (OperationCanceledException) { throw; }
                catch { actual = null; }

                readings.Add(new MagicByteReading(addr, b.ExpectedValue, actual));
            }
            return readings;
        }

        /// <summary>
        /// Writes the expected value into each calibration-shadow magic byte via the tooling
        /// channel. Requires <paramref name="confirm"/> = true AND the tooling client's
        /// <see cref="T6eToolingClient.AllowWrites"/> enabled by the caller. See the class remarks:
        /// this is a diagnostic experiment, not an expected live unlock.
        /// </summary>
        public async Task ApplyCalibrationMagicAsync(T6eToolingClient writableTooling, bool confirm, CancellationToken ct = default)
        {
            if (!confirm)
                throw new InvalidOperationException("Refusing to patch calibration-shadow bytes without explicit confirmation.");
            if (writableTooling is null)
                throw new ArgumentNullException(nameof(writableTooling));
            if (_profile.CalibrationMagic.Length == 0)
                throw new InvalidOperationException($"No calibration magic pattern is recovered for {_profile.Name}.");

            foreach (var b in _profile.CalibrationMagic)
            {
                if (_profile.MagicAddress(b) is not uint addr)
                    continue;
                await writableTooling.WriteAsync(addr, new[] { b.ExpectedValue }, ct);
            }
        }
    }
}
