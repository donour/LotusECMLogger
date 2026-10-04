using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Hc08
{
    /// <summary>
    /// Raises the TPS max table inside the T6 ECU's HC08 throttle-monitor firmware so it is never
    /// below the calibration's "tps: scaling factor rpm" table, and repairs the HC08 checksum that
    /// PROG verifies at runtime. C# port of hc08_tps_sync.py; see HC08_TPS_firmware_update.md for
    /// the full specification and test vectors.
    /// </summary>
    /// <remarks>
    /// The T6 carries two copies of the table: one in the calibration (main PPC) and one in the
    /// HC08 image embedded in PROG. Tuning tools only edit the calibration, so raising it alone
    /// leaves the monitor enforcing the old, lower limit. Only increases are ever applied to the
    /// table: each HC08 breakpoint becomes max(HC08, cal), so a second run is a no-op. If the HC08
    /// rpm axis does not match the calibration axis, the calibration axis is converted to HC08
    /// units and written over it, so both tables are compared and interpolated on the same
    /// breakpoints. Both axes must be strictly increasing.
    /// </remarks>
    public static class Hc08TpsPatcher
    {
        /// <summary>Main-CPU flash address PROG is loaded at; PROG file offset 0 maps here.</summary>
        public const int ProgBase = 0x40000;

        private const int CalIdLength = 16;

        // PROG+0x80 holds "HC08CODE", PROG+0x8C the HC08 image's absolute flash address
        // (big-endian). The image is mapped at HC08 address 0xDC00.
        private const int Hc08MagicOffset = 0x80;
        private static readonly byte[] Hc08Magic = "HC08CODE"u8.ToArray();
        private const int Hc08PointerOffset = 0x8C;
        private const int Hc08Base = 0xDC00;

        // Checksum regions relative to the image start: HC08 0xDC00-0xFBFF, then the vectors at
        // 0xFFDC-0xFFFF. Image bytes 0x2000-0x23DB are not covered.
        private const int SumRange1Start = 0x0000, SumRange1Length = 0x2000;
        private const int SumRange2Start = 0x23DC, SumRange2Length = 0x24;
        private const int Hc08ImageLength = SumRange2Start + SumRange2Length;
        private const ushort ChecksumSeed = 0x0123;

        // lis r3,1 ; addi r3,r3,<imm> ; clrlwi r0,r3,16 — the result is exactly the 16-bit
        // immediate for any value, so patching the two immediate bytes is always correct.
        private static readonly byte[] ChecksumInsnPattern =
            [0x3C, 0x60, 0x00, 0x01, 0x38, 0x63, 0x00, 0x00, 0x54, 0x60, 0x04, 0x3E];
        private const int ChecksumImmOffset = 6;

        /// <summary>
        /// T6 HC08 checksum of <paramref name="data"/>: a 16-bit running sum of (0xFF00 | ~byte),
        /// seeded with 0x0123. Each step is equivalent to crc = crc - byte - 1 (mod 65536).
        /// </summary>
        public static ushort Checksum(ReadOnlySpan<byte> data, ushort seed = ChecksumSeed)
        {
            ushort crc = seed;
            foreach (byte b in data)
                crc = unchecked((ushort)(crc + 0xFFFF - b));
            return crc;
        }

        /// <summary>Checksum of the HC08 image that starts at <paramref name="hc08Offset"/> in PROG.</summary>
        public static ushort ImageChecksum(ReadOnlySpan<byte> prog, int hc08Offset)
        {
            ushort crc = Checksum(prog.Slice(hc08Offset + SumRange1Start, SumRange1Length));
            return Checksum(prog.Slice(hc08Offset + SumRange2Start, SumRange2Length), crc);
        }

        /// <summary>The calibration ID: the first 16 bytes of the calibration file, trimmed.</summary>
        public static string ReadCalibrationId(ReadOnlySpan<byte> cal) =>
            Encoding.ASCII.GetString(cal[..Math.Min(CalIdLength, cal.Length)]).Trim().Trim('\0').Trim();

        /// <summary>Reads both files and runs <see cref="Analyze"/>. Neither file is modified.</summary>
        public static Hc08TpsAnalysis AnalyzeFiles(string progPath, string calPath, string? profileName = null) =>
            Analyze(File.ReadAllBytes(progPath), File.ReadAllBytes(calPath), profileName);

        /// <summary>
        /// Runs every safety check on a PROG/CAL pair and, when a raise is needed, builds and
        /// re-verifies the patched PROG image in memory. Never touches the disk.
        /// </summary>
        /// <param name="prog">PROG image as flashed at 0x40000 (e.g. *_BIN.cpt).</param>
        /// <param name="cal">Calibration image as flashed at 0x20000 (e.g. *_TAB.cpt). Read only.</param>
        /// <param name="profileName">Profile to use instead of the one matching the calibration ID.</param>
        /// <exception cref="Hc08PatchException">Any abort condition; nothing should be written.</exception>
        public static Hc08TpsAnalysis Analyze(byte[] prog, byte[] cal, string? profileName = null)
        {
            ArgumentNullException.ThrowIfNull(prog);
            ArgumentNullException.ThrowIfNull(cal);

            var original = (byte[])prog.Clone();
            var layout = Locate(original, cal, profileName);
            var p = layout.Profile;

            byte[] calAxis = cal.AsSpan(p.CalAxis, p.Size).ToArray();
            byte[] oldAxis = original.AsSpan(layout.Hc08AxisOffset, p.Size).ToArray();

            // Interpolation needs strictly increasing breakpoints. A non-increasing HC08 axis also
            // means the profile offsets point at something that is not the table, which matters
            // now that a mismatched axis is rewritten rather than rejected.
            CheckIncreasing("calibration", calAxis.Select(p.CalAxisRpm).ToArray());
            CheckIncreasing("HC08", oldAxis.Select(p.Hc08AxisRpm).ToArray());

            ushort oldSum = ImageChecksum(original, layout.Hc08Offset);
            ushort embedded = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(layout.ImmOffset, 2));
            if (oldSum != embedded)
                throw new Hc08PatchException(
                    $"HC08 checksum already inconsistent: computed {oldSum:X4}, embedded {embedded:X4}. " +
                    "The HC08 image has been modified by something else; refusing to patch.");

            // When the axes disagree the calibration axis is the reference: it is converted to HC08
            // units and written over the HC08 axis, so both tables share the same breakpoints.
            byte[] newAxis = AxesMatch(oldAxis, calAxis, p) ? oldAxis : ConvertCalAxis(calAxis, p);

            byte[] oldTable = original.AsSpan(layout.Hc08TableOffset, p.Size).ToArray();
            byte[] calTable = cal.AsSpan(p.CalTable, p.Size).ToArray();
            byte[] newTable = RaisedTable(oldTable, calTable);

            var rows = new List<Hc08TpsRow>(p.Size);
            for (int i = 0; i < p.Size; i++)
                rows.Add(new Hc08TpsRow(i, p.CalAxisRpm(calAxis[i]), oldTable[i], calTable[i], newTable[i])
                {
                    Hc08AxisOld = oldAxis[i],
                    Hc08AxisNew = newAxis[i],
                    Hc08RpmOld = p.Hc08AxisRpm(oldAxis[i]),
                    Hc08RpmNew = p.Hc08AxisRpm(newAxis[i]),
                });

            if (newTable.AsSpan().SequenceEqual(oldTable) && newAxis.AsSpan().SequenceEqual(oldAxis))
                return new Hc08TpsAnalysis(layout.CalId, p, layout.Hc08Offset, layout.ImmOffset, rows,
                    oldSum, oldSum, original, patchedProg: null, changedOffsets: []);

            byte[] patched = (byte[])original.Clone();
            newAxis.CopyTo(patched, layout.Hc08AxisOffset);
            newTable.CopyTo(patched, layout.Hc08TableOffset);
            ushort newSum = ImageChecksum(patched, layout.Hc08Offset);
            BinaryPrimitives.WriteUInt16BigEndian(patched.AsSpan(layout.ImmOffset, 2), newSum);

            var changed = Verify(original, patched, cal, layout, newAxis, newTable, newSum);

            return new Hc08TpsAnalysis(layout.CalId, p, layout.Hc08Offset, layout.ImmOffset, rows,
                oldSum, newSum, original, patched, changed);
        }

        /// <summary>
        /// Writes the patched image from <paramref name="analysis"/> over the PROG file: optional
        /// timestamped backup, then temp file + rename so the original is never left half-written.
        /// </summary>
        /// <returns>Path of the backup copy, or null when <paramref name="backup"/> is false.</returns>
        /// <exception cref="Hc08PatchException">The file on disk is no longer the one that was analyzed.</exception>
        public static string? Apply(string progPath, Hc08TpsAnalysis analysis, bool backup = true)
        {
            ArgumentNullException.ThrowIfNull(analysis);
            if (analysis.Status != Hc08TpsStatus.UpdateNeeded)
                throw new InvalidOperationException("The HC08 table is already up to date; there is nothing to apply.");

            // The user reviews the diff between analysis and apply; refuse to overwrite a file that
            // changed in the meantime, since the patched image was built from the old contents.
            if (!File.ReadAllBytes(progPath).AsSpan().SequenceEqual(analysis.OriginalProg))
                throw new Hc08PatchException(
                    "The PROG file changed on disk after it was checked. Check it again before patching.");

            string? backupPath = null;
            if (backup)
            {
                backupPath = UniqueBackupPath(progPath);
                File.Copy(progPath, backupPath, overwrite: false);
            }

            string fullPath = Path.GetFullPath(progPath);
            string tmp = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
                {
                    fs.Write(analysis.PatchedProg.Span);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }

            if (!File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(analysis.PatchedProg.Span))
                throw new Hc08PatchException(
                    $"Read-back of {fullPath} does not match the patched image." +
                    (backupPath is null ? string.Empty : $" Restore the original from {backupPath}."));

            return backupPath;
        }

        /// <summary>Only raise HC08 values: each breakpoint becomes max(hc08, cal).</summary>
        private static byte[] RaisedTable(byte[] old, byte[] cal)
        {
            var result = new byte[old.Length];
            for (int i = 0; i < old.Length; i++)
                result[i] = Math.Max(old[i], cal[i]);
            return result;
        }

        private sealed record Layout(string CalId, Hc08TpsProfile Profile, int Hc08Offset, int ImmOffset)
        {
            public int Hc08TableOffset => Hc08Offset + Profile.Hc08Table - Hc08Base;
            public int Hc08AxisOffset => Hc08Offset + Profile.Hc08Axis - Hc08Base;
        }

        /// <summary>
        /// Validates the PROG header, selects the profile, checks the calibration length, and finds
        /// the checksum instruction (procedure steps 2-4). PROG is validated first so that passing
        /// the files in the wrong order gives the clear "not a T6 PROG file" error.
        /// </summary>
        private static Layout Locate(byte[] prog, byte[] cal, string? profileName)
        {
            if (prog.Length < Hc08PointerOffset + 4
                || !prog.AsSpan(Hc08MagicOffset, Hc08Magic.Length).SequenceEqual(Hc08Magic))
                throw new Hc08PatchException(
                    "HC08CODE marker not found at PROG+0x80. This is not a T6 PROG file " +
                    "(or the PROG and calibration files are swapped).");

            long hc08Offset = (long)BinaryPrimitives.ReadUInt32BigEndian(prog.AsSpan(Hc08PointerOffset, 4)) - ProgBase;
            if (hc08Offset < 0 || hc08Offset > prog.Length - Hc08ImageLength)
                throw new Hc08PatchException(
                    $"HC08 image offset 0x{hc08Offset:x} is outside PROG (0x{prog.Length:x} bytes); the header pointer is invalid.");

            string calId = ReadCalibrationId(cal);
            string name = string.IsNullOrEmpty(profileName) ? calId : profileName;
            if (!Hc08TpsProfiles.Known.TryGetValue(name, out var profile))
            {
                string known = string.Join(", ", Hc08TpsProfiles.Known.Keys);
                throw new Hc08PatchException(string.IsNullOrEmpty(profileName)
                    ? $"No profile for calibration '{calId}' (known: {known}). This firmware is not supported."
                    : $"No profile named '{profileName}' (known: {known}).");
            }

            if (cal.Length < Math.Max(profile.CalTable, profile.CalAxis) + profile.Size)
                throw new Hc08PatchException(
                    $"Calibration is only 0x{cal.Length:x} bytes; too short for profile {profile.Name}.");

            foreach (int addr in new[] { profile.Hc08Table, profile.Hc08Axis })
                if (addr < Hc08Base || addr + profile.Size > Hc08Base + Hc08ImageLength)
                    throw new Hc08PatchException(
                        $"Profile {profile.Name} HC08 address 0x{addr:x4} is outside the HC08 image.");

            return new Layout(calId, profile, (int)hc08Offset, FindChecksumImmediate(prog));
        }

        /// <summary>
        /// Finds the unique word-aligned "lis r3,1; addi r3,r3,????; clrlwi r0,r3,16" sequence and
        /// returns the offset of its immediate. The immediate is a wildcard so that a stale value is
        /// reported as an inconsistent checksum rather than a missing instruction.
        /// </summary>
        private static int FindChecksumImmediate(byte[] prog)
        {
            var hits = new List<int>();
            for (int i = 0; i <= prog.Length - ChecksumInsnPattern.Length; i += 4)
            {
                var window = prog.AsSpan(i, ChecksumInsnPattern.Length);
                if (window[..ChecksumImmOffset].SequenceEqual(ChecksumInsnPattern.AsSpan(0, ChecksumImmOffset))
                    && window[(ChecksumImmOffset + 2)..].SequenceEqual(ChecksumInsnPattern.AsSpan(ChecksumImmOffset + 2)))
                    hits.Add(i);
            }
            if (hits.Count != 1)
                throw new Hc08PatchException($"Expected 1 HC08 checksum instruction in PROG, found {hits.Count}.");
            return hits[0] + ChecksumImmOffset;
        }

        /// <summary>
        /// True when the HC08 axis agrees with the calibration axis to within one HC08 count at
        /// every breakpoint, in which case it is left exactly as it is.
        /// </summary>
        private static bool AxesMatch(byte[] hc08Axis, byte[] calAxis, Hc08TpsProfile p)
        {
            for (int i = 0; i < p.Size; i++)
                if (Math.Abs(p.CalAxisRpm(calAxis[i]) - p.Hc08AxisRpm(hc08Axis[i])) > p.AxisToleranceRpm)
                    return false;
            return true;
        }

        /// <summary>
        /// Converts the calibration rpm axis to HC08 axis bytes. The two axes use different units,
        /// so the bytes are never copied raw (the obsolete t6_patch_hc08_firmware.py did, which is
        /// wrong). Converting the factory C132E0278 calibration axis reproduces the factory HC08
        /// axis exactly.
        /// </summary>
        private static byte[] ConvertCalAxis(byte[] calAxis, Hc08TpsProfile p)
        {
            var result = new byte[p.Size];
            var bad = new StringBuilder();
            for (int i = 0; i < p.Size; i++)
            {
                double rpm = p.CalAxisRpm(calAxis[i]);
                double counts = Math.Round((rpm - p.Hc08AxisOffset) / p.Hc08AxisScale, MidpointRounding.AwayFromZero);
                if (counts < 0 || counts > byte.MaxValue)
                    bad.AppendLine(CultureInfo.InvariantCulture, $"  [{i,2}] {rpm:F0} rpm");
                else
                    result[i] = (byte)counts;
            }
            if (bad.Length > 0)
                throw new Hc08PatchException(
                    "The HC08 rpm axis does not match the calibration, and these calibration breakpoints are outside the range " +
                    string.Create(CultureInfo.InvariantCulture,
                        $"the HC08 axis can hold ({p.Hc08AxisRpm(0):F0}-{p.Hc08AxisRpm(byte.MaxValue):F0} rpm):") +
                    Environment.NewLine + bad.ToString().TrimEnd());

            CheckIncreasing("converted HC08", result.Select(p.Hc08AxisRpm).ToArray(),
                string.Create(CultureInfo.InvariantCulture,
                    $" Adjacent calibration breakpoints closer than one HC08 axis step ({p.Hc08AxisScale:0.###} rpm) map to the same HC08 breakpoint."));
            return result;
        }

        /// <summary>Requires every breakpoint to be above the one before it.</summary>
        private static void CheckIncreasing(string axisName, double[] rpm, string hint = "")
        {
            var bad = new StringBuilder();
            for (int i = 1; i < rpm.Length; i++)
                if (rpm[i] <= rpm[i - 1])
                    bad.AppendLine(CultureInfo.InvariantCulture, $"  [{i,2}] {rpm[i],6:F0} rpm is not above [{i - 1,2}] {rpm[i - 1],6:F0} rpm");
            if (bad.Length > 0)
                throw new Hc08PatchException(
                    $"The {axisName} rpm axis is not strictly increasing.{hint}" + Environment.NewLine + bad.ToString().TrimEnd());
        }

        /// <summary>
        /// Re-runs the header/profile/instruction lookup on the patched buffer and checks that only
        /// the axis, table and checksum immediate changed, and that the axes now agree. Returns the
        /// changed offsets.
        /// </summary>
        private static List<int> Verify(byte[] original, byte[] patched, byte[] cal, Layout layout,
            byte[] newAxis, byte[] newTable, ushort newSum)
        {
            var p = layout.Profile;
            var check = Locate(patched, cal, p.Name);

            if (patched.Length != original.Length)
                Fail("patched PROG length differs");
            if (check.Hc08Offset != layout.Hc08Offset || check.ImmOffset != layout.ImmOffset)
                Fail("HC08 image or checksum instruction moved");
            if (!patched.AsSpan(check.Hc08TableOffset, newTable.Length).SequenceEqual(newTable))
                Fail("HC08 table does not hold the new values");
            byte[] axis = patched.AsSpan(check.Hc08AxisOffset, p.Size).ToArray();
            if (!axis.AsSpan().SequenceEqual(newAxis))
                Fail("HC08 axis does not hold the expected values");
            if (!AxesMatch(axis, cal.AsSpan(p.CalAxis, p.Size).ToArray(), p))
                Fail("HC08 axis still does not match the calibration axis");
            CheckIncreasing("patched HC08", axis.Select(p.Hc08AxisRpm).ToArray());
            ushort computed = ImageChecksum(patched, check.Hc08Offset);
            ushort embedded = BinaryPrimitives.ReadUInt16BigEndian(patched.AsSpan(check.ImmOffset, 2));
            if (computed != newSum || embedded != newSum)
                Fail($"checksum mismatch after patch (computed {computed:X4}, embedded {embedded:X4}, expected {newSum:X4})");

            var changed = new List<int>();
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i] == patched[i])
                    continue;
                bool inTable = i >= layout.Hc08TableOffset && i < layout.Hc08TableOffset + newTable.Length;
                bool inAxis = i >= layout.Hc08AxisOffset && i < layout.Hc08AxisOffset + newAxis.Length;
                bool inImm = i == layout.ImmOffset || i == layout.ImmOffset + 1;
                if (!inTable && !inAxis && !inImm)
                    Fail($"unexpected change at PROG+0x{i:x}");
                changed.Add(i);
            }
            return changed;

            static void Fail(string what) =>
                throw new Hc08PatchException($"Internal verification of the patched PROG failed: {what}. Nothing was written.");
        }

        private static string UniqueBackupPath(string progPath)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = $"{progPath}.{stamp}.bak";
            for (int n = 1; File.Exists(path); n++)
                path = $"{progPath}.{stamp}-{n}.bak";
            return path;
        }
    }
}
