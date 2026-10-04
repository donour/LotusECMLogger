using System.Globalization;
using System.Text;

namespace LotusECMLogger.Hc08
{
    /// <summary>
    /// Raised for every condition that must stop the patch before anything is written: wrong
    /// file, unknown firmware, mismatched PROG/CAL pair, or an HC08 image that has already been
    /// modified by something else.
    /// </summary>
    public sealed class Hc08PatchException(string message) : Exception(message);

    public enum Hc08TpsStatus
    {
        /// <summary>No calibration breakpoint exceeds the HC08 table; nothing to write.</summary>
        UpToDate,
        /// <summary>At least one HC08 breakpoint needs raising; a verified patched image is ready.</summary>
        UpdateNeeded,
    }

    public enum Hc08TpsRowAction
    {
        /// <summary>Calibration and HC08 agree, or the calibration is lower and equal after max().</summary>
        None,
        /// <summary>The HC08 value is raised to the calibration value.</summary>
        Raise,
        /// <summary>The calibration is lower; the HC08 value is kept because the tool never lowers it.</summary>
        DecreaseSkipped,
    }

    /// <summary>One breakpoint of the TPS max table, before and after the patch.</summary>
    /// <param name="Index">Breakpoint index (0-based).</param>
    /// <param name="Rpm">Breakpoint rpm, from the calibration axis (the reference axis).</param>
    /// <param name="Hc08Old">HC08 table value currently in PROG.</param>
    /// <param name="Cal">Calibration table value.</param>
    /// <param name="Hc08New">HC08 table value after the patch: max(Hc08Old, Cal).</param>
    public sealed record Hc08TpsRow(int Index, double Rpm, byte Hc08Old, byte Cal, byte Hc08New)
    {
        /// <summary>HC08 rpm axis byte currently in PROG.</summary>
        public byte Hc08AxisOld { get; init; }

        /// <summary>HC08 rpm axis byte after the patch; differs only when the axis is rewritten.</summary>
        public byte Hc08AxisNew { get; init; }

        public double Hc08RpmOld { get; init; }
        public double Hc08RpmNew { get; init; }

        /// <summary>True when this breakpoint's HC08 axis value is rewritten from the calibration axis.</summary>
        public bool AxisChanged => Hc08AxisNew != Hc08AxisOld;

        public Hc08TpsRowAction Action =>
            Hc08New != Hc08Old ? Hc08TpsRowAction.Raise
            : Cal < Hc08Old ? Hc08TpsRowAction.DecreaseSkipped
            : Hc08TpsRowAction.None;

        /// <summary>Both table copies store throttle as x*100/255 percent.</summary>
        public static double Percent(byte x) => x * 100.0 / 255;

        public string ActionText => Action switch
        {
            Hc08TpsRowAction.Raise => $"raise {FormatDelta(Percent(Hc08New) - Percent(Hc08Old))}%",
            Hc08TpsRowAction.DecreaseSkipped => $"keep (cal {FormatDelta(Percent(Cal) - Percent(Hc08Old))}%, decrease skipped)",
            _ => string.Empty,
        };

        private static string FormatDelta(double delta) =>
            delta.ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Outcome of checking a PROG/CAL pair. Every safety check has already passed by the time one
    /// of these exists; when <see cref="Status"/> is <see cref="Hc08TpsStatus.UpdateNeeded"/> the
    /// patched image has also been re-verified from scratch and is ready to write.
    /// </summary>
    public sealed class Hc08TpsAnalysis
    {
        private readonly byte[] originalProg;
        private readonly byte[]? patchedProg;

        internal Hc08TpsAnalysis(
            string calibrationId,
            Hc08TpsProfile profile,
            int hc08ImageOffset,
            int checksumImmediateOffset,
            IReadOnlyList<Hc08TpsRow> rows,
            ushort oldChecksum,
            ushort newChecksum,
            byte[] originalProg,
            byte[]? patchedProg,
            IReadOnlyList<int> changedOffsets)
        {
            CalibrationId = calibrationId;
            Profile = profile;
            Hc08ImageOffset = hc08ImageOffset;
            ChecksumImmediateOffset = checksumImmediateOffset;
            Rows = rows;
            OldChecksum = oldChecksum;
            NewChecksum = newChecksum;
            this.originalProg = originalProg;
            this.patchedProg = patchedProg;
            ChangedOffsets = changedOffsets;
        }

        /// <summary>Calibration ID read from the start of the calibration file.</summary>
        public string CalibrationId { get; }

        public Hc08TpsProfile Profile { get; }

        /// <summary>Offset of the HC08 image within PROG.</summary>
        public int Hc08ImageOffset { get; }

        /// <summary>PROG offset of the 16-bit checksum immediate in the PPC compare instruction.</summary>
        public int ChecksumImmediateOffset { get; }

        public IReadOnlyList<Hc08TpsRow> Rows { get; }

        /// <summary>HC08 checksum before the patch (computed and embedded agree, or analysis would have failed).</summary>
        public ushort OldChecksum { get; }

        /// <summary>HC08 checksum after the patch; equal to <see cref="OldChecksum"/> when up to date.</summary>
        public ushort NewChecksum { get; }

        public Hc08TpsStatus Status => patchedProg is null ? Hc08TpsStatus.UpToDate : Hc08TpsStatus.UpdateNeeded;

        public bool HasSkippedDecreases => Rows.Any(r => r.Action == Hc08TpsRowAction.DecreaseSkipped);

        /// <summary>
        /// True when the HC08 rpm axis did not match the calibration axis and the patch replaces it
        /// with the calibration axis converted to HC08 units.
        /// </summary>
        public bool AxisRewritten => Rows.Any(r => r.AxisChanged);

        /// <summary>The verified patched PROG image; empty when <see cref="Status"/> is up to date.</summary>
        public ReadOnlyMemory<byte> PatchedProg => patchedProg;

        /// <summary>
        /// Every PROG offset the patch changes: the raised table bytes and the checksum immediate.
        /// Empty when up to date.
        /// </summary>
        public IReadOnlyList<int> ChangedOffsets { get; }

        internal ReadOnlySpan<byte> OriginalProg => originalProg;

        /// <summary>
        /// The same report the reference hc08_tps_sync.py prints: header, per-breakpoint table
        /// and checksum change.
        /// </summary>
        public string FormatReport()
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(ic, $"Calibration : {CalibrationId}  (profile {Profile.Name})");
            sb.AppendLine(ic, $"HC08 image  : PROG+0x{Hc08ImageOffset:x}, table @ HC08 0x{Profile.Hc08Table:x4}");
            sb.AppendLine(ic, $"Checksum    : insn imm @ PROG+0x{ChecksumImmediateOffset:x}");
            sb.AppendLine();

            if (Status == Hc08TpsStatus.UpToDate)
            {
                if (HasSkippedDecreases)
                {
                    sb.AppendLine("Calibration is below the HC08 table at some breakpoints (decreases are not applied):");
                    AppendTable(sb);
                    sb.AppendLine();
                }
                sb.AppendLine(ic, $"No calibration value exceeds the HC08 table, checksum {OldChecksum:X4} OK. Nothing to do.");
            }
            else
            {
                if (AxisRewritten)
                {
                    sb.AppendLine("HC08 rpm axis does not match the calibration axis; it is replaced with the calibration axis:");
                    sb.AppendLine(ic, $"  {"idx",3}  {"cal rpm",7}  {"hc08 now",13}  {"hc08 new",13}");
                    foreach (var r in Rows)
                    {
                        string line = string.Create(ic,
                            $"  {r.Index,3}  {r.Rpm,7:F1}  {r.Hc08AxisOld:X2} {r.Hc08RpmOld,6:F0} rpm  {r.Hc08AxisNew:X2} {r.Hc08RpmNew,6:F0} rpm  {(r.AxisChanged ? "changed" : "")}");
                        sb.AppendLine(line.TrimEnd());
                    }
                    sb.AppendLine();
                }
                sb.AppendLine(Rows.Any(r => r.Action == Hc08TpsRowAction.Raise)
                    ? "Calibration exceeds the HC08 TPS max table:"
                    : "No calibration value exceeds the HC08 TPS max table (table values unchanged):");
                AppendTable(sb);
                sb.AppendLine();
                sb.AppendLine(ic, $"HC08 checksum: {OldChecksum:X4} -> {NewChecksum:X4}");
            }
            return sb.ToString();
        }

        private void AppendTable(StringBuilder sb)
        {
            var ic = CultureInfo.InvariantCulture;
            sb.AppendLine(ic, $"  {"idx",3}  {"rpm",5}  {"hc08 now",9}  {"cal",9}  {"hc08 new",9}  action");
            foreach (var r in Rows)
            {
                // Half-to-even matches Python's float formatting, so 812.5 rpm prints as 812 in both.
                double rpm = Math.Round(r.Rpm, MidpointRounding.ToEven);
                string line = string.Create(ic,
                    $"  {r.Index,3}  {rpm,5:F0}  {Cell(r.Hc08Old)}  {Cell(r.Cal)}  {Cell(r.Hc08New)}  {r.ActionText}");
                sb.AppendLine(line.TrimEnd());
            }
        }

        private static string Cell(byte x) =>
            string.Create(CultureInfo.InvariantCulture, $"{x:X2} {Hc08TpsRow.Percent(x),5:F1}%");
    }
}
