using LotusECMLogger.Models;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Turns a user selection of channels + per-channel rates into a validated <see cref="LoggingPlan"/>
    /// for the T6e high-speed CAN channel logger, applying the firmware's packing rules:
    /// <list type="bullet">
    ///   <item>each frame holds at most 7 payload bytes of channels (hard limit, silent truncation);</item>
    ///   <item>each group holds at most 12 frames; there are at most 10 groups;</item>
    ///   <item>a channel address of 0 is invalid (the ECU treats it as end-of-list).</item>
    /// </list>
    /// Pure and deterministic — no I/O — so it can be unit-tested against the worked example in
    /// <c>ChannelLogger.md</c>.
    /// </summary>
    public static class HighSpeedLogPlanner
    {
        public const int MaxGroups = 10;
        public const int MaxFramesPerGroup = 12;
        public const int MaxPayloadBytes = 7;

        /// <summary>Rates (Hz) the scheduler can produce, for populating the UI rate dropdown.</summary>
        public static readonly int[] SupportedRatesHz = [1, 2, 5, 10, 20, 50, 100];

        /// <summary>
        /// Base scheduler slot and per-group divider for each supported rate. Slots 0/2/4/6 have a
        /// staggered partner (1/3/5/7) used to spread two same-rate groups across the bus; slots 8/9
        /// have no partner. See the rate-control table in <c>ChannelLogger.md</c>.
        /// </summary>
        private static readonly Dictionary<int, (int slot, ushort divider)> RateTable = new()
        {
            [100] = (0, 1),
            [50]  = (2, 1),
            [20]  = (4, 1),
            [10]  = (6, 1),
            [5]   = (6, 2),
            [2]   = (8, 1),
            [1]   = (9, 1),
        };

        /// <summary>
        /// Builds a logging plan from the selected (channel, rateHz) pairs. Throws
        /// <see cref="ArgumentException"/> for an empty/invalid selection and
        /// <see cref="InvalidOperationException"/> when the program exceeds the ECU's capacity.
        /// </summary>
        public static LoggingPlan Plan(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> selected)
        {
            ArgumentNullException.ThrowIfNull(selected);
            if (selected.Count == 0)
                throw new ArgumentException("Select at least one channel to log.", nameof(selected));

            foreach (var (channel, rateHz) in selected)
            {
                if (channel.Size is not (1 or 2 or 4))
                    throw new ArgumentException($"Channel '{channel.Name}' has invalid size {channel.Size}; must be 1, 2 or 4.");
                if (channel.Address == 0)
                    throw new ArgumentException($"Channel '{channel.Name}' has address 0, which the ECU treats as end-of-list.");
                if (!RateTable.ContainsKey(rateHz))
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
                var (baseSlot, divider) = RateTable[rate];

                int chunkIndex = 0;
                for (int start = 0; start < frames.Count; start += MaxFramesPerGroup)
                {
                    if (groupIndex >= MaxGroups)
                        throw new InvalidOperationException(
                            $"This selection needs more than {MaxGroups} ECU groups. Reduce the number of channels or rates.");

                    var chunk = frames.GetRange(start, Math.Min(MaxFramesPerGroup, frames.Count - start));
                    var group = new PlannedGroup
                    {
                        GroupIndex = groupIndex,
                        Slot = PairedSlot(baseSlot, chunkIndex),
                        Divider = divider,
                        RateHz = rate,
                    };

                    for (int f = 0; f < chunk.Count; f++)
                    {
                        byte label = (byte)(groupIndex * 12 + f);
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
        /// Alternates spill groups of the same rate across a staggered slot pair (0/1, 2/3, 4/5, 6/7)
        /// to avoid bursting both groups onto the same scheduler tick. Slots 8 and 9 have no partner.
        /// </summary>
        private static int PairedSlot(int baseSlot, int chunkIndex)
        {
            if (baseSlot <= 6 && baseSlot % 2 == 0)
                return baseSlot + (chunkIndex % 2);
            return baseSlot;
        }
    }
}
