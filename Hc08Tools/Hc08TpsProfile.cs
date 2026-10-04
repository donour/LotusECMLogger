namespace LotusECMLogger.Hc08
{
    /// <summary>
    /// Per-firmware locations of the two copies of the T6 "TPS max vs rpm" table: the
    /// calibration copy (used by the main PPC) and the copy inside the HC08 throttle
    /// monitor image embedded in PROG.
    /// </summary>
    /// <param name="Name">Profile key; the calibration ID it applies to (e.g. C132E0278).</param>
    /// <param name="CalTable">Calibration file offset of the table values (as in the romraider defs).</param>
    /// <param name="CalAxis">Calibration file offset of the rpm axis.</param>
    /// <param name="Hc08Table">HC08 address of the table values (as in Ghidra), not a file offset.</param>
    /// <param name="Hc08Axis">HC08 address of the rpm axis.</param>
    /// <param name="Size">Entries per table and axis.</param>
    public sealed record Hc08TpsProfile(
        string Name,
        int CalTable,
        int CalAxis,
        int Hc08Table,
        int Hc08Axis,
        int Size = 16)
    {
        // The two axes store the same breakpoints in different units, so they are compared in rpm.
        // Calibration: x*125/4 + 500 (romraider "tps: scaling factor rpm" X axis).
        // HC08: x*255/8, i.e. 8 bits spanning 0..8160 rpm (verified against all 16 C132E0278
        // breakpoints; an earlier rpm/32 guess fails at high rpm).
        public double CalAxisScale { get; init; } = 125.0 / 4;
        public double CalAxisOffset { get; init; } = 500;
        public double Hc08AxisScale { get; init; } = 255.0 / 8;
        public double Hc08AxisOffset { get; init; } = 0;

        /// <summary>Maximum allowed |cal rpm − HC08 rpm| per breakpoint: one HC08 axis count.</summary>
        public double AxisToleranceRpm { get; init; } = 255.0 / 8;

        public double CalAxisRpm(byte x) => x * CalAxisScale + CalAxisOffset;
        public double Hc08AxisRpm(byte x) => x * Hc08AxisScale + Hc08AxisOffset;
    }

    /// <summary>
    /// Firmwares the patcher knows how to handle, keyed by the calibration ID at the start of
    /// the calibration file.
    /// </summary>
    /// <remarks>
    /// D132E0231, C132E0271 and E132E0288 share 0278's HC08 image bytes and very likely its
    /// offsets, but are deliberately not listed until their calibration offsets are confirmed
    /// against their romraider definitions. The NA Evora (B132E0091) HC08 image has no
    /// rpm-dependent TPS max table at all and must never get a profile copied from 0278.
    /// </remarks>
    public static class Hc08TpsProfiles
    {
        public static IReadOnlyDictionary<string, Hc08TpsProfile> Known { get; } =
            new Dictionary<string, Hc08TpsProfile>(StringComparer.Ordinal)
            {
                ["C132E0278"] = new("C132E0278", CalTable: 0x0E3E, CalAxis: 0x0E2E, Hc08Table: 0xDC42, Hc08Axis: 0xDC32),
            };
    }
}
