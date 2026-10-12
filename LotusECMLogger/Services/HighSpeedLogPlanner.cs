using LotusECMLogger.Models;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Turns a user selection of channels + per-channel rates into a validated <see cref="LoggingPlan"/>
    /// for the T6 high-speed CAN channel logger, applying the firmware's packing rules:
    /// <list type="bullet">
    ///   <item>each frame holds at most 7 payload bytes of channels (hard limit, silent truncation);</item>
    ///   <item>each group holds at most the target's ODTs per list (12 on T6), and there are at most its
    ///   DAQ list count (10 on T6);</item>
    ///   <item>a channel address of 0 is invalid (the ECU treats it as end-of-list).</item>
    /// </list>
    /// In CCP terms a group is a DAQ list, a frame an ODT, a slot an event channel, a divider a prescaler
    /// and a label a PID. Limits and event rates come from the <see cref="CcpTarget"/>.
    /// Pure and deterministic — no I/O — so it can be unit-tested against the worked example in
    /// <c>ChannelLogger.md</c>.
    /// </summary>
    public static class HighSpeedLogPlanner
    {
        public const int MaxPayloadBytes = 7;

        /// <summary>Rates (Hz) the scheduler can produce, for populating the UI rate dropdown.</summary>
        public static readonly int[] SupportedRatesHz = [1, 2, 5, 10, 20, 50, 100];

        /// <summary>
        /// Base event channel (scheduler slot) and prescaler (divider) for a rate: the event whose rate is a
        /// multiple of it with the smallest prescaler, lowest event first. On T6 (events 0–9 at
        /// 100/100/50/50/20/20/10/10/2/1 Hz) this gives 100→0, 50→2, 20→4, 10→6, 5→6÷2, 2→8, 1→9.
        /// </summary>
        private static (int slot, ushort divider)? EventFor(CcpTarget target, int rateHz) =>
            target.EventRatesHz
                .Where(e => rateHz > 0 && e.Value % rateHz == 0 && e.Value / rateHz <= ushort.MaxValue)
                .OrderBy(e => e.Value / rateHz).ThenBy(e => e.Key)
                .Select(e => ((int, ushort)?)(e.Key, (ushort)(e.Value / rateHz)))
                .FirstOrDefault();

        /// <summary>
        /// Builds a logging plan from the selected (channel, rateHz) pairs. Throws
        /// <see cref="ArgumentException"/> for an empty/invalid selection and
        /// <see cref="InvalidOperationException"/> when the program exceeds the ECU's capacity.
        /// </summary>
        public static LoggingPlan Plan(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> selected,
            CcpTarget? target = null)
        {
            ArgumentNullException.ThrowIfNull(selected);
            target ??= CcpTarget.T6;
            if (selected.Count == 0)
                throw new ArgumentException("Select at least one channel to log.", nameof(selected));

            foreach (var (channel, rateHz) in selected)
            {
                if (channel.Size is not (1 or 2 or 4))
                    throw new ArgumentException($"Channel '{channel.Name}' has invalid size {channel.Size}; must be 1, 2 or 4.");
                if (channel.Address == 0)
                    throw new ArgumentException($"Channel '{channel.Name}' has address 0, which the ECU treats as end-of-list.");
                if (!SupportedRatesHz.Contains(rateHz) || EventFor(target, rateHz) is null)
                    throw new ArgumentException($"Channel '{channel.Name}' has unsupported rate {rateHz} Hz.");
            }

            var plan = new LoggingPlan();
            int groupIndex = 0;

            // Deterministic order: fastest rates first, original selection order within a rate.
            var rateBuckets = selected
                .Select((s, i) => (s.channel, s.rateHz, order: i))
                .GroupBy(s => s.rateHz)
                .OrderByDescending(g => g.Key);

            foreach (var bucket in rateBuckets)
            {
                int rate = bucket.Key;
                var channels = bucket.OrderBy(s => s.order).Select(s => s.channel).ToList();
                var frames = PackFrames(channels);
                var (baseSlot, divider) = EventFor(target, rate)!.Value;

                int chunkIndex = 0;
                for (int start = 0; start < frames.Count; start += target.OdtsPerList)
                {
                    if (groupIndex >= target.DaqListCount)
                        throw new InvalidOperationException(
                            $"This selection needs more than {target.DaqListCount} ECU groups. Reduce the number of channels or rates.");

                    var chunk = frames.GetRange(start, Math.Min(target.OdtsPerList, frames.Count - start));
                    var group = new PlannedGroup
                    {
                        GroupIndex = groupIndex,
                        Slot = PairedSlot(target, baseSlot, chunkIndex),
                        Divider = divider,
                        RateHz = rate,
                    };

                    for (int f = 0; f < chunk.Count; f++)
                    {
                        // Expected PID; the logger uses the first PID GET_DAQ_SIZE actually returns.
                        byte label = (byte)(target.ExpectedFirstPid(groupIndex) + f);
                        var frame = new PlannedFrame { FrameIndex = f, Label = label };
                        frame.Channels.AddRange(chunk[f]);
                        group.Frames.Add(frame);
                        plan.LayoutByLabel[label] = frame.Channels.Select(pc => pc.Channel).ToList();
                    }

                    plan.Groups.Add(group);
                    groupIndex++;
                    chunkIndex++;
                }
            }

            return plan;
        }

        /// <summary>Packs channels sequentially into frames, never exceeding 7 payload bytes per frame.</summary>
        private static List<List<PlannedChannel>> PackFrames(IReadOnlyList<HighSpeedChannel> channels)
        {
            var frames = new List<List<PlannedChannel>>();
            var current = new List<PlannedChannel>();
            int used = 0;

            foreach (var ch in channels)
            {
                if (used + ch.Size > MaxPayloadBytes && current.Count > 0)
                {
                    frames.Add(current);
                    current = [];
                    used = 0;
                }

                current.Add(new PlannedChannel { Channel = ch, ByteOffset = used });
                used += ch.Size;
            }

            if (current.Count > 0)
                frames.Add(current);

            return frames;
        }

        /// <summary>
        /// Alternates spill groups of the same rate across a staggered event pair (on T6 0/1, 2/3, 4/5, 6/7)
        /// to avoid bursting both groups onto the same scheduler tick. An event's partner is the next one
        /// when it runs at the same rate; on T6, events 8 and 9 have none.
        /// </summary>
        private static int PairedSlot(CcpTarget target, int baseSlot, int chunkIndex)
        {
            bool paired = target.EventRatesHz.TryGetValue(baseSlot + 1, out int next) && next == target.EventRatesHz[baseSlot];
            return paired ? baseSlot + (chunkIndex % 2) : baseSlot;
        }
    }
}
