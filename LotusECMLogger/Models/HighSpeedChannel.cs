namespace LotusECMLogger.Models
{
    /// <summary>
    /// A single loggable channel for the high-speed CAN channel logger: an ECU memory address
    /// of a given size, plus a linear transform (<c>value = raw * Scale + Offset</c>) and a unit
    /// label. Channels are defined in JSON presets (see <c>config/highSpeedLogger</c>).
    /// </summary>
    /// <remarks>
    /// The ECU is big-endian (PowerPC); raw bytes for a channel are streamed MSB-first.
    /// </remarks>
    public sealed class HighSpeedChannel
    {
        public string Name { get; init; } = "Channel";

        /// <summary>32-bit ECU memory address (big-endian on the wire).</summary>
        public uint Address { get; init; }

        /// <summary>Channel width in bytes; only 1, 2 or 4 are valid per the protocol.</summary>
        public byte Size { get; init; } = 1;

        /// <summary>Whether the raw value is two's-complement signed.</summary>
        public bool Signed { get; init; }

        /// <summary>Multiplier applied to the raw integer to produce engineering units.</summary>
        public double Scale { get; init; } = 1.0;

        /// <summary>Additive offset applied after <see cref="Scale"/>.</summary>
        public double Offset { get; init; }

        /// <summary>Unit label for display/CSV (e.g. "rpm", "°C", "%").</summary>
        public string Unit { get; init; } = "";

        /// <summary>Suggested logging rate (Hz) when the preset is loaded.</summary>
        public int DefaultRate { get; init; } = 10;

        /// <summary>Whether the channel is checked for logging by default.</summary>
        public bool DefaultSelected { get; init; }

        /// <summary>Symbol name this channel was resolved from (null for hand-authored channels).</summary>
        public string? SourceSymbol { get; init; }

        /// <summary>Quantity/kind hint from the symbol's type (e.g. "rspeed", "temp"); optional.</summary>
        public string Category { get; init; } = "";

        /// <summary>
        /// Decodes this channel's raw bytes (big-endian, MSB first) into its engineering value
        /// by applying <see cref="Signed"/> sign-extension then <see cref="Scale"/>/<see cref="Offset"/>.
        /// </summary>
        public double Decode(ReadOnlySpan<byte> raw)
        {
            long v = 0;
            int n = Math.Min(Size, raw.Length);
            for (int i = 0; i < n; i++)
                v = (v << 8) | raw[i];

            if (Signed && Size is 1 or 2 or 4)
            {
                int bits = Size * 8;
                long signBit = 1L << (bits - 1);
                if ((v & signBit) != 0)
                    v -= 1L << bits;
            }

            return v * Scale + Offset;
        }
    }

    /// <summary>One channel placed at a byte offset within a frame's 7-byte payload.</summary>
    public sealed class PlannedChannel
    {
        public required HighSpeedChannel Channel { get; init; }

        /// <summary>Byte offset of this channel within the frame payload (0..6).</summary>
        public int ByteOffset { get; init; }
    }

    /// <summary>One CAN frame on 0x351 — up to 7 payload bytes of packed channels.</summary>
    public sealed class PlannedFrame
    {
        /// <summary>Frame index within its group (0..11).</summary>
        public int FrameIndex { get; init; }

        /// <summary>Demux label the ECU stamps on the frame: <c>group*12 + frameIndex</c>.</summary>
        public byte Label { get; init; }

        public List<PlannedChannel> Channels { get; } = [];
    }

    /// <summary>
    /// One scheduler group: a set of frames sampled together at <see cref="RateHz"/>, driven by
    /// scheduler <see cref="Slot"/> divided by <see cref="Divider"/>.
    /// </summary>
    public sealed class PlannedGroup
    {
        /// <summary>Group index (0..9).</summary>
        public int GroupIndex { get; init; }

        /// <summary>Scheduler timeslot (0..9).</summary>
        public int Slot { get; init; }

        /// <summary>Per-group rate down-divider (u16) applied within the slot.</summary>
        public ushort Divider { get; init; }

        /// <summary>Effective sample rate of this group in Hz.</summary>
        public int RateHz { get; init; }

        public List<PlannedFrame> Frames { get; } = [];

        /// <summary>Highest frame index used; the ECU emits frames <c>0..FrameCount</c> inclusive.</summary>
        public int FrameCount => Frames.Count - 1;
    }

    /// <summary>
    /// A complete, validated channel-logging program for the ECU: the groups to configure plus a
    /// map from each emitted frame label to the ordered channels it carries (for decoding the stream).
    /// </summary>
    public sealed class LoggingPlan
    {
        public List<PlannedGroup> Groups { get; } = [];

        /// <summary>Label byte (without the 0x80 overflow bit) → channels in payload order.</summary>
        public Dictionary<byte, IReadOnlyList<HighSpeedChannel>> LayoutByLabel { get; } = new();
    }
}
