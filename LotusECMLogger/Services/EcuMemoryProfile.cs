namespace LotusECMLogger.Services
{
    /// <summary>
    /// One of the four dispersed calibration bytes whose values gate the boot-time
    /// <c>ecu_unlocked</c> engineering predicate. The firmware copies calibration flash
    /// into the RAM shadow at startup, then requires all of these offsets to hold their
    /// expected byte before enabling the raw engineering mailbox family.
    /// </summary>
    /// <param name="ShadowOffset">Byte offset within the RAM calibration shadow.</param>
    /// <param name="ExpectedValue">Value the firmware compares against at that offset.</param>
    public readonly record struct CalibrationMagicByte(uint ShadowOffset, byte ExpectedValue);

    /// <summary>
    /// CAN IDs and memory layout for a specific Lotus T6-family ECU. Everything the
    /// unlock/tooling clients need is captured here so the same code can target either
    /// the Evora T6e or the Emira G6 ECU (or a hand-tuned variant) from configuration.
    ///
    /// Sources:
    ///   • Evora RMA IDs/window: existing T6RMAService (firmware flexcan_a_rx_50_51_52_53).
    ///   • Emira engineering/tooling IDs, cal-shadow base and magic bytes:
    ///     disassembly/emira/8896915220A_ROW/analysis/ENGINEERING_UNLOCK_PATHS.md.
    /// </summary>
    public sealed record EcuMemoryProfile
    {
        public required string Name { get; init; }

        /// <summary>
        /// Base standard CAN ID of the raw memory family. The eight operations are laid out
        /// contiguously from this base: +0 read32, +1 read16, +2 read8, +3 block-read,
        /// +4 write32, +5 write16, +6 write8, +7 block/stream-write.
        /// Evora = 0x50, Emira = 0x40.
        /// </summary>
        public required uint RmaBaseId { get; init; }

        /// <summary>CAN ID the ECU replies on for every raw memory read. Both ECUs use 0x7A0.</summary>
        public required uint RmaResponseId { get; init; }

        /// <summary>Inclusive start of the address window the raw memory family will service.</summary>
        public required uint RamStart { get; init; }

        /// <summary>Inclusive end of the address window the raw memory family will service.</summary>
        public required uint RamEnd { get; init; }

        /// <summary>
        /// Request ID of the always-initialized proprietary tooling channel. This channel is
        /// NOT gated by <c>ecu_unlocked</c>, which is why it is the interesting unlock surface.
        /// Emira = 0x202. Unverified on the Evora — treat as a hypothesis to probe.
        /// </summary>
        public required uint ToolingRequestId { get; init; }

        /// <summary>Response/stream IDs the tooling channel replies on. Emira = {0x200, 0x201}.</summary>
        public required uint[] ToolingResponseIds { get; init; }

        /// <summary>
        /// Base address of the RAM calibration shadow (where calibration flash is copied at boot).
        /// The magic bytes below are expressed as offsets into this region. Null when unknown.
        /// Emira = 0x4002E000.
        /// </summary>
        public uint? CalShadowBase { get; init; }

        /// <summary>
        /// The dispersed calibration bytes that must match for the boot predicate to set
        /// <c>ecu_unlocked = true</c>. Empty when the pattern for this ECU is not yet recovered.
        /// </summary>
        public CalibrationMagicByte[] CalibrationMagic { get; init; } = [];

        /// <summary>Absolute address of a magic byte, or null if the shadow base is unknown.</summary>
        public uint? MagicAddress(CalibrationMagicByte b) =>
            CalShadowBase is uint baseAddr ? baseAddr + b.ShadowOffset : null;

        /// <summary>
        /// Evora T6e. RMA family 0x50-0x57 replying on 0x7A0 over the 64 KiB RAM window.
        /// The tooling-channel IDs mirror the Emira as an untested cross-model hypothesis, and
        /// the Evora's calibration magic offsets are not yet recovered here.
        /// </summary>
        public static readonly EcuMemoryProfile Evora = new()
        {
            Name = "Evora T6e",
            RmaBaseId = 0x50,
            RmaResponseId = 0x7A0,
            RamStart = 0x40000000,
            RamEnd = 0x4000FFFF,
            ToolingRequestId = 0x202,
            ToolingResponseIds = [0x200, 0x201],
            CalShadowBase = null,
            CalibrationMagic = [],
        };

        /// <summary>
        /// Emira G6 (8896915220A_ROW). Raw engineering family 0x40-0x47 replying on 0x7A0 over
        /// the 256 KiB SRAM window, plus the un-gated 0x202 tooling channel and the four
        /// calibration-shadow magic bytes recovered in ENGINEERING_UNLOCK_PATHS.md.
        /// </summary>
        public static readonly EcuMemoryProfile Emira = new()
        {
            Name = "Emira G6 (8896915220A)",
            RmaBaseId = 0x40,
            RmaResponseId = 0x7A0,
            RamStart = 0x40000000,
            RamEnd = 0x4003FFFF,
            ToolingRequestId = 0x202,
            ToolingResponseIds = [0x200, 0x201],
            CalShadowBase = 0x4002E000,
            CalibrationMagic =
            [
                new CalibrationMagicByte(0x00E2, 0x0D),
                new CalibrationMagicByte(0x0218, 0xB8),
                new CalibrationMagicByte(0x0290, 0x45),
                new CalibrationMagicByte(0x0337, 0xD4),
            ],
        };
    }
}
