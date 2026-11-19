using System.Globalization;
using LotusECMLogger.Models;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// Where one firmware build keeps its own load statistics (CCP-client-plan.md, 6g): the background loop's duration,
    /// the 2 kHz timer interrupt's time per rate slot and its entry-to-entry period, the stack watermark, and two health
    /// counters. Addresses come from the build's decompile and are kept here rather than resolved from catalog names,
    /// which change; <c>EcuLoadTests</c> checks each against the build's catalog.
    /// </summary>
    /// <remarks>
    /// Times are TCR1 ticks of 0.1 µs, wall time including preemption by higher-priority interrupts. The slot maxima
    /// and the minimum period are cleared when <see cref="StatsResetRequest"/> is written non-zero (the interrupt clears
    /// the request); the main-loop and CCP-background maxima only at power-on.
    /// </remarks>
    public sealed record EcuLoadMap(string Build, uint MainLoopLast, uint MainLoopMax, uint CcpBackgroundMax, uint IsrPeriodLast,
        uint IsrPeriodMin, uint SlotLast, uint SlotMax, uint StackPeakAddress, uint StackPeakFound, uint CcpTxStalls, uint SyncLosses,
        uint StatsResetRequest)
    {
        /// <summary>Rate slots 0–10; the max array has a twelfth entry, the longest interrupt period.</summary>
        public const int SlotCount = 11;
        public const double IsrPeriodUs = 500;
        /// <summary>The stack's sentinel area: filled with 0x55 at start-up, the stack growing down from the top of RAM into it.</summary>
        public const uint StackBottom = 0x4000_F000, StackTop = 0x4001_0000;
        public const int StackBytes = (int)(StackTop - StackBottom);
        /// <summary>The live values' rate, and the slot maxima's. The ECU holds the peaks, so slow sampling misses none.</summary>
        public const int FastRateHz = 5, SlowRateHz = 1;

        /// <summary>What each slot runs at. Slot 2 contains slots 3–10, and slot 6 contains 7–10.</summary>
        public static readonly IReadOnlyList<string> SlotRates =
            ["whole interrupt", "1 kHz", "200 Hz", "100 Hz", "50 Hz", "20 Hz", "10 Hz", "5 Hz", "3.33 Hz", "2 Hz", "1 Hz"];
        public static readonly IReadOnlyList<double> SlotPeriodsUs =
            [500, 1_000, 5_000, 10_000, 20_000, 50_000, 100_000, 200_000, 300_000, 500_000, 1_000_000];

        public static readonly IReadOnlyList<EcuLoadMap> Known =
        [
            new("C132E0278", MainLoopLast: 0x40002EF8, MainLoopMax: 0x40002EFC, CcpBackgroundMax: 0x40002F0C, IsrPeriodLast: 0x40002EEC,
                IsrPeriodMin: 0x40001710, SlotLast: 0x40002EC0, SlotMax: 0x40002F10, StackPeakAddress: 0x40001688,
                StackPeakFound: 0x40001680, CcpTxStalls: 0x40002530, SyncLosses: 0x4000173A, StatsResetRequest: 0x400016F3),
            new("B132E0091", MainLoopLast: 0x40002B60, MainLoopMax: 0x40002B64, CcpBackgroundMax: 0x40002B74, IsrPeriodLast: 0x40002B54,
                IsrPeriodMin: 0x400015E8, SlotLast: 0x40002B28, SlotMax: 0x40002B78, StackPeakAddress: 0x40001578,
                StackPeakFound: 0x40001570, CcpTxStalls: 0x40002260, SyncLosses: 0x40001612, StatsResetRequest: 0x400015D7),
        ];

        /// <summary>The map for <paramref name="build"/>, or null when its addresses have not been worked out.</summary>
        public static EcuLoadMap? For(string? build) => Known.FirstOrDefault(m => m.Build == build);

        public uint SlotLastAddress(int slot) => SlotLast + 4u * (uint)slot;
        public uint SlotMaxAddress(int slot) => SlotMax + 4u * (uint)slot;

        /// <summary>
        /// The channels to stream or record, each with its rate. Each 4-byte value fills a DAQ frame, so a 1-byte counter
        /// rides in each of the first three; two lists in all (8 frames at 5 Hz, 12 at 1 Hz), about 52 frames a second.
        /// </summary>
        public IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> Channels()
        {
            static HighSpeedChannel Time(string name, uint address, int rate) => new()
            {
                Name = name, Address = address, Size = 4, Scale = 0.1, Unit = "us", DefaultRate = rate, DefaultSelected = true,
                SourceSymbol = name,
            };
            static HighSpeedChannel Raw(string name, uint address, byte size, string unit, int rate) => new()
            {
                Name = name, Address = address, Size = size, Unit = unit, DefaultRate = rate, DefaultSelected = true, SourceSymbol = name,
            };
            var channels = new List<HighSpeedChannel>
            {
                Time("timing_main_loop_last", MainLoopLast, FastRateHz),
                Raw("ccp_tx_stall_count", CcpTxStalls, 1, "count", FastRateHz),
                Time("isr_period_ticks", IsrPeriodLast, FastRateHz),
                Raw("engine_sync_reset_count", SyncLosses, 1, "count", FastRateHz),
                Time("task_last_execution_time_0", SlotLastAddress(0), FastRateHz),
                Raw("stack_peak_found", StackPeakFound, 1, "bool", FastRateHz),
                Time("task_last_execution_time_2", SlotLastAddress(2), FastRateHz),
                Time("timing_main_loop_max", MainLoopMax, FastRateHz),
                Time("timing_ccp_background_max", CcpBackgroundMax, FastRateHz),
                Time("isr_period_min_ticks", IsrPeriodMin, FastRateHz),
                Raw("stack_peak_addr", StackPeakAddress, 4, "address", FastRateHz),
            };
            for (int slot = 0; slot <= SlotCount; slot++)
                channels.Add(Time($"task_max_execution_time_{slot}", SlotMaxAddress(slot), SlowRateHz));
            return channels.Select(c => (c, c.DefaultRate)).ToList();
        }
    }

    public enum EcuLoadLevel { Normal, Warning, Fault }

    /// <summary>The ECU's load statistics at one moment, in µs and bytes; each value null while it has none.</summary>
    public sealed record EcuLoadSample(double? MainLoopUs, double? MainLoopMaxUs, double? CcpBackgroundMaxUs, double? IsrPeriodUs,
        double? IsrPeriodMinUs, IReadOnlyList<double?> SlotLastUs, IReadOnlyList<double?> SlotMaxUs, uint? StackPeakAddress,
        bool? StackFound, int? CcpTxStalls, int? SyncLosses)
    {
        /// <summary>The longest the whole 2 kHz interrupt took (slot 0).</summary>
        public double? IsrMaxUs => SlotMaxUs[0];
        /// <summary>The longest interrupt entry-to-entry period: nominally 500 µs, more when a tick started late.</summary>
        public double? IsrPeriodMaxUs => SlotMaxUs[EcuLoadMap.SlotCount];

        /// <summary>The deepest the stack has reached, once the scan has found it (the pointer is meaningless before).</summary>
        public int? StackUsedBytes => StackFound == true && StackPeakAddress is { } a ? (int)((long)EcuLoadMap.StackTop - a) : null;

        public bool HasAny => MainLoopUs is not null || IsrPeriodUs is not null || SlotLastUs[0] is not null || IsrMaxUs is not null;

        public static EcuLoadSample Read(IRawValueSource source, EcuLoadMap map)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(map);
            double? Us(uint address) => source.Bits(address, 4) is { } bits ? (uint)bits * 0.1 : null;
            int? Byte(uint address) => source.Bits(address, 1) is { } bits ? (int)(bits & 0xFF) : null;
            var last = new double?[EcuLoadMap.SlotCount];
            last[0] = Us(map.SlotLastAddress(0));
            last[2] = Us(map.SlotLastAddress(2));
            var max = Enumerable.Range(0, EcuLoadMap.SlotCount + 1).Select(slot => Us(map.SlotMaxAddress(slot))).ToArray();
            return new EcuLoadSample(Us(map.MainLoopLast), Us(map.MainLoopMax), Us(map.CcpBackgroundMax), Us(map.IsrPeriodLast),
                Us(map.IsrPeriodMin), last, max, source.Bits(map.StackPeakAddress, 4) is { } a ? (uint)a : null,
                Byte(map.StackPeakFound) is { } found ? found != 0 : null, Byte(map.CcpTxStalls), Byte(map.SyncLosses));
        }

        /// <summary>Over half the 500 µs budget is a warning; the whole of it means a tick overran.</summary>
        public static EcuLoadLevel IsrLevel(double? maxUs) =>
            maxUs >= EcuLoadMap.IsrPeriodUs ? EcuLoadLevel.Fault : maxUs > EcuLoadMap.IsrPeriodUs / 2 ? EcuLoadLevel.Warning : EcuLoadLevel.Normal;

        /// <summary>A tick 10% late is a warning; a period of two ticks means one was missed.</summary>
        public static EcuLoadLevel IsrPeriodLevel(double? maxUs) =>
            maxUs >= 2 * EcuLoadMap.IsrPeriodUs ? EcuLoadLevel.Fault : maxUs > 1.1 * EcuLoadMap.IsrPeriodUs ? EcuLoadLevel.Warning : EcuLoadLevel.Normal;

        /// <summary>75% of the 4 KB is a warning, 95% a fault (beyond 100% the stack has left the sentinel area).</summary>
        public static EcuLoadLevel StackLevel(int? usedBytes) =>
            usedBytes >= 0.95 * EcuLoadMap.StackBytes ? EcuLoadLevel.Fault
            : usedBytes >= 0.75 * EcuLoadMap.StackBytes ? EcuLoadLevel.Warning : EcuLoadLevel.Normal;
    }

    /// <summary>
    /// The ECU's 8-bit health counters as increases since watching began (or since the last <see cref="Reset"/>, e.g. a
    /// replay seeking back), wrapping at 256.
    /// </summary>
    public sealed class EcuLoadCounters
    {
        private int? stallsAtStart, syncLossesAtStart;

        public void Reset() => (stallsAtStart, syncLossesAtStart) = (null, null);

        public (int? CcpTxStalls, int? SyncLosses) Since(EcuLoadSample sample)
        {
            ArgumentNullException.ThrowIfNull(sample);
            stallsAtStart ??= sample.CcpTxStalls;
            syncLossesAtStart ??= sample.SyncLosses;
            return (Delta(sample.CcpTxStalls, stallsAtStart), Delta(sample.SyncLosses, syncLossesAtStart));
        }

        private static int? Delta(int? now, int? start) => now is { } n && start is { } s ? (n - s) & 0xFF : null;
    }

    /// <summary>The Tuning page's one-line summary, as text segments each with its level.</summary>
    public static class EcuLoadText
    {
        public static IReadOnlyList<(string Text, EcuLoadLevel Level)> Segments(EcuLoadSample sample, (int? CcpTxStalls, int? SyncLosses) counters)
        {
            ArgumentNullException.ThrowIfNull(sample);
            var segments = new List<(string, EcuLoadLevel)>
            {
                ($"loop {Ms(sample.MainLoopUs)} ms (max {Ms(sample.MainLoopMaxUs)})", EcuLoadLevel.Normal),
                ($"ISR {Us(sample.SlotLastUs[0])} µs (max {Us(sample.IsrMaxUs)} of {EcuLoadMap.IsrPeriodUs:0})", EcuLoadSample.IsrLevel(sample.IsrMaxUs)),
                (sample.IsrPeriodMaxUs is { } period ? $"tick late max +{Us(Math.Max(0, period - EcuLoadMap.IsrPeriodUs))} µs" : "tick late max —",
                    EcuLoadSample.IsrPeriodLevel(sample.IsrPeriodMaxUs)),
                (sample.StackUsedBytes is { } used
                    ? string.Create(CultureInfo.CurrentCulture, $"stack {used / 1024.0:0.0} of {EcuLoadMap.StackBytes / 1024} KB")
                    : sample.StackFound == false ? "stack: measuring" : "stack —",
                    EcuLoadSample.StackLevel(sample.StackUsedBytes)),
                ($"CCP stalls {Count(counters.CcpTxStalls)}", counters.CcpTxStalls > 0 ? EcuLoadLevel.Warning : EcuLoadLevel.Normal),
                ($"sync losses {Count(counters.SyncLosses)}", counters.SyncLosses > 0 ? EcuLoadLevel.Fault : EcuLoadLevel.Normal),
            };
            return segments;
        }

        public static string Ms(double? us) => us is { } v ? (v / 1000).ToString("0.00", CultureInfo.CurrentCulture) : "—";
        public static string Us(double? us) => us is { } v ? v.ToString("0", CultureInfo.CurrentCulture) : "—";
        private static string Count(int? count) => count?.ToString(CultureInfo.CurrentCulture) ?? "—";
    }

    /// <summary>
    /// Streams an <see cref="EcuLoadMap"/>'s channels over its own DAQ lists on the shared session (DAQ owner
    /// <see cref="OwnerName"/>). A running recording logs the same channels, so the Tuning page reads that instead.
    /// </summary>
    public sealed class EcuLoadMonitor : IRawValueSource, IAsyncDisposable
    {
        public const string OwnerName = "ECU load";

        private readonly CcpVariableWatch watch;
        private readonly Dictionary<(uint Address, int Size), int> slots;

        private EcuLoadMonitor(EcuLoadMap map, CcpVariableWatch watch, Dictionary<(uint, int), int> slots) =>
            (Map, this.watch, this.slots) = (map, watch, slots);

        public EcuLoadMap Map { get; }
        public bool IsActive => watch.IsActive;

        public static async Task<EcuLoadMonitor> StartAsync(CcpMaster master, EcuLoadMap map, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(map);
            var channels = map.Channels();
            var slots = new Dictionary<(uint, int), int>();
            for (int i = 0; i < channels.Count; i++) slots[(channels[i].Channel.Address, channels[i].Channel.Size)] = i;
            var variables = channels.Select(c => (new CcpVariable(c.Channel.Name, c.Channel.Address, c.Channel.Size, false, c.Channel.Scale,
                c.Channel.Offset, c.Channel.Unit, null, false, ""), c.RateHz)).ToList();
            var watch = await CcpVariableWatch.StartAsync(master, variables, OwnerName, cancellationToken).ConfigureAwait(false);
            return new EcuLoadMonitor(map, watch, slots);
        }

        public ulong? Bits(uint address, int size)
        {
            if (!slots.TryGetValue((address, size), out int slot)) return null;
            return watch.Snapshot()[slot] is { } value ? RawValues.Bits(Convert.FromHexString(value.RawHex)) : null;
        }

        public EcuLoadSample Read() => EcuLoadSample.Read(this, Map);

        /// <summary>
        /// Clears the slot maxima and the minimum period by writing 1 to the ECU's reset request (RAM, outside the
        /// calibration; nothing else is allowed). The interrupt clears the request within 500 µs, so a read-back of 0 is
        /// the expected result, not a failure. The main-loop and CCP-background maxima are not cleared by it.
        /// </summary>
        public static async Task ResetMaximaAsync(CcpMaster master, EcuLoadMap map, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            ArgumentNullException.ThrowIfNull(map);
            try
            {
                await master.WriteVerifiedAsync(map.StatsResetRequest, new byte[] { 1 }, new CcpMemoryRange(map.StatsResetRequest, 1),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (CcpWriteVerificationException)
            {
                // Read back as 0: the interrupt has already acted on the request.
            }
        }

        public ValueTask DisposeAsync() => watch.DisposeAsync();
    }
}
