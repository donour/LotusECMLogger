using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Hc08;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Exercises <see cref="Hc08TpsPatcher"/> against the test vectors in
    /// hc08_patcher/HC08_TPS_firmware_update.md §10. Factory firmware can't be checked in, so the
    /// PROG/CAL pair is synthesized with the C132E0278 layout: the header, the checksum instruction
    /// at its factory offset, the factory HC08 axis/table, and filler that brings the HC08 checksum
    /// to the factory 0xDA01. That reproduces the documented checksums and changed offsets exactly.
    /// </summary>
    public sealed class Hc08TpsPatcherTests : IDisposable
    {
        private const int ProgLength = 0x98988;
        private const int Hc08Offset = 0x913F0;
        private const int InsnStart = 0x337BC;
        private const int ImmOffset = InsnStart + 6;
        private const int CalLength = 0x69AC;

        private static readonly byte[] Hc08Axis =
            [0x19, 0x1F, 0x27, 0x2F, 0x37, 0x3F, 0x4E, 0x5E, 0x6E, 0x7D, 0x8D, 0x9C, 0xAC, 0xBB, 0xCB, 0xDB];
        private static readonly byte[] Hc08FactoryTable =
            [0x8D, 0xA4, 0xB7, 0xCE, 0xD4, 0xD4, 0xD5, 0xD5, 0xB4, 0xA9, 0xB6, 0xCC, 0xE4, 0xFF, 0xFF, 0xFF];
        private static readonly byte[] CalAxis =
            [0x0A, 0x10, 0x18, 0x20, 0x28, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x8F, 0x9F, 0xAF, 0xBF, 0xCF];
        private static readonly byte[] CalFactoryTable =
            [0x8D, 0xA4, 0xB7, 0xCE, 0xD9, 0xD9, 0xD9, 0xD9, 0xB4, 0xA9, 0xB6, 0xCC, 0xE4, 0xFF, 0xFF, 0xFF];
        private static readonly byte[] Cal93OctaneTable =
            [0x8D, 0xA4, 0xB7, 0xBF, 0xBF, 0xC4, 0xCC, 0xD9, 0xD9, 0xD9, 0xD9, 0xE6, 0xF2, 0xFF, 0xFF, 0xFF];

        private readonly string workDir;

        public Hc08TpsPatcherTests()
        {
            workDir = Path.Combine(Path.GetTempPath(), "Hc08TpsPatcherTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static byte[] FactoryProg()
        {
            var prog = new byte[ProgLength];
            Encoding.ASCII.GetBytes("HC08CODE").CopyTo(prog, 0x80);
            BinaryPrimitives.WriteUInt32BigEndian(prog.AsSpan(0x8C), Hc08Offset + 0x40000);
            Hc08Axis.CopyTo(prog, Hc08Offset + 0x32);
            Hc08FactoryTable.CopyTo(prog, Hc08Offset + 0x42);

            // Spread filler over covered image bytes well away from the table until the checksum
            // reaches the factory value; each byte of value v lowers the checksum by v.
            int delta = (Hc08TpsPatcher.ImageChecksum(prog, Hc08Offset) - 0xDA01) & 0xFFFF;
            for (int i = 0x1000; delta > 0; i++)
            {
                byte v = (byte)Math.Min(delta, 0xFF);
                prog[Hc08Offset + i] = v;
                delta -= v;
            }

            byte[] insn = [0x3C, 0x60, 0x00, 0x01, 0x38, 0x63, 0xDA, 0x01, 0x54, 0x60, 0x04, 0x3E];
            insn.CopyTo(prog, InsnStart);
            return prog;
        }

        private static byte[] Cal(byte[] table, string id = "C132E0278        05-07-2019 9:24")
        {
            var cal = new byte[CalLength];
            Encoding.ASCII.GetBytes(id).CopyTo(cal, 0);
            CalAxis.CopyTo(cal, 0x0E2E);
            table.CopyTo(cal, 0x0E3E);
            return cal;
        }

        private static byte[] Hc08TableOf(ReadOnlySpan<byte> prog) => prog.Slice(Hc08Offset + 0x42, 16).ToArray();

        [Fact]
        public void Checksum_TestVectors()
        {
            Assert.Equal(0x0123, Hc08TpsPatcher.Checksum([]));

            var ramp = new byte[1024];
            for (int i = 0; i < ramp.Length; i++)
                ramp[i] = (byte)i;
            Assert.Equal(0xFF23, Hc08TpsPatcher.Checksum(ramp));
        }

        [Fact]
        public void SyntheticFactoryProg_HasFactoryChecksum()
        {
            Assert.Equal(0xDA01, Hc08TpsPatcher.ImageChecksum(FactoryProg(), Hc08Offset));
        }

        [Fact]
        public void Analyze_93OctaneCal_RaisesOnlyAndMatchesReferenceVector()
        {
            var prog = FactoryProg();
            var a = Hc08TpsPatcher.Analyze(prog, Cal(Cal93OctaneTable));

            Assert.Equal(Hc08TpsStatus.UpdateNeeded, a.Status);
            Assert.Equal("C132E0278", a.CalibrationId);
            Assert.Equal(Hc08Offset, a.Hc08ImageOffset);
            Assert.Equal(ImmOffset, a.ChecksumImmediateOffset);
            Assert.Equal(0xDA01, a.OldChecksum);
            Assert.Equal(0xD95D, a.NewChecksum);

            byte[] expected = [0x8D, 0xA4, 0xB7, 0xCE, 0xD4, 0xD4, 0xD5, 0xD9, 0xD9, 0xD9, 0xD9, 0xE6, 0xF2, 0xFF, 0xFF, 0xFF];
            Assert.Equal(expected, Hc08TableOf(a.PatchedProg.Span));

            Assert.Equal([7, 8, 9, 10, 11, 12],
                a.Rows.Where(r => r.Action == Hc08TpsRowAction.Raise).Select(r => r.Index));
            Assert.Equal([3, 4, 5, 6],
                a.Rows.Where(r => r.Action == Hc08TpsRowAction.DecreaseSkipped).Select(r => r.Index));

            Assert.Equal([0x337C2, 0x337C3, 0x91439, 0x9143A, 0x9143B, 0x9143C, 0x9143D, 0x9143E], a.ChangedOffsets);
            Assert.Equal(
                new byte[] { 0x3C, 0x60, 0x00, 0x01, 0x38, 0x63, 0xD9, 0x5D, 0x54, 0x60, 0x04, 0x3E },
                a.PatchedProg.Span.Slice(InsnStart, 12).ToArray());

            // The caller's buffer is never modified.
            Assert.Equal(Hc08FactoryTable, Hc08TableOf(prog));
        }

        [Fact]
        public void Analyze_SecondRun_IsNoOp()
        {
            var cal = Cal(Cal93OctaneTable);
            var patched = Hc08TpsPatcher.Analyze(FactoryProg(), cal).PatchedProg.ToArray();

            var again = Hc08TpsPatcher.Analyze(patched, cal);

            Assert.Equal(Hc08TpsStatus.UpToDate, again.Status);
            Assert.Equal(0xD95D, again.OldChecksum);
            Assert.True(again.HasSkippedDecreases);
            Assert.Empty(again.ChangedOffsets);
            Assert.True(again.PatchedProg.IsEmpty);
        }

        [Fact]
        public void Analyze_FactoryCal_TableBecomesCalibration()
        {
            var a = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(CalFactoryTable));

            Assert.Equal(0xD9EF, a.NewChecksum);
            Assert.Equal(CalFactoryTable, Hc08TableOf(a.PatchedProg.Span));
            Assert.Equal([0x337C2, 0x337C3, 0x91436, 0x91437, 0x91438, 0x91439], a.ChangedOffsets);
            Assert.False(a.HasSkippedDecreases);
        }

        [Fact]
        public void Analyze_CalEqualToHc08_IsUpToDate()
        {
            var a = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(Hc08FactoryTable));

            Assert.Equal(Hc08TpsStatus.UpToDate, a.Status);
            Assert.False(a.HasSkippedDecreases);
            Assert.All(a.Rows, r => Assert.Equal(Hc08TpsRowAction.None, r.Action));
        }

        [Fact]
        public void Analyze_UnknownCalibration_Throws()
        {
            var ex = Assert.Throws<Hc08PatchException>(() =>
                Hc08TpsPatcher.Analyze(FactoryProg(), Cal(CalFactoryTable, "B132E0091")));
            Assert.Contains("B132E0091", ex.Message);
        }

        [Fact]
        public void Analyze_ProfileOverride_IsUsed()
        {
            var a = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(CalFactoryTable, "SOMETHINGELSE"), "C132E0278");
            Assert.Equal("SOMETHINGELSE", a.CalibrationId);
            Assert.Equal("C132E0278", a.Profile.Name);
        }

        [Fact]
        public void Analyze_SwappedFiles_ReportsMissingMarker()
        {
            var ex = Assert.Throws<Hc08PatchException>(() =>
                Hc08TpsPatcher.Analyze(Cal(CalFactoryTable), FactoryProg()));
            Assert.Contains("HC08CODE", ex.Message);
            Assert.Contains("swapped", ex.Message);
        }

        [Fact]
        public void Analyze_TruncatedCal_Throws()
        {
            var cal = Cal(CalFactoryTable).AsSpan(0, 100).ToArray();
            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(FactoryProg(), cal));
            Assert.Contains("too short", ex.Message);
        }

        [Fact]
        public void Analyze_Hc08PointerOutsideProg_Throws()
        {
            var prog = FactoryProg();
            BinaryPrimitives.WriteUInt32BigEndian(prog.AsSpan(0x8C), 0x40000 + ProgLength - 0x100);
            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(prog, Cal(CalFactoryTable)));
            Assert.Contains("outside PROG", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void Analyze_ChecksumInstructionNotUnique_Throws(int count)
        {
            var prog = FactoryProg();
            if (count == 0)
                prog[InsnStart] = 0x00;
            else
                prog.AsSpan(InsnStart, 12).CopyTo(prog.AsSpan(0x20000));

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(prog, Cal(CalFactoryTable)));
            Assert.Contains($"found {count}", ex.Message);
        }

        [Fact]
        public void Analyze_MisalignedPatternCopy_IsIgnored()
        {
            var prog = FactoryProg();
            prog.AsSpan(InsnStart, 12).CopyTo(prog.AsSpan(0x20002));

            Assert.Equal(ImmOffset, Hc08TpsPatcher.Analyze(prog, Cal(CalFactoryTable)).ChecksumImmediateOffset);
        }

        [Fact]
        public void Analyze_MatchingAxes_LeavesHc08AxisAlone()
        {
            var a = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(CalFactoryTable));

            Assert.False(a.AxisRewritten);
            Assert.Equal(Hc08Axis, a.PatchedProg.Span.Slice(Hc08Offset + 0x32, 16).ToArray());
        }

        [Fact]
        public void Analyze_AxisMismatch_RewritesHc08AxisFromCalibration()
        {
            var cal = Cal(CalFactoryTable);
            cal[0x0E2E + 15] = 0xDF; // 7468.75 rpm, two HC08 counts above the factory 0xDB (6981 rpm)

            var a = Hc08TpsPatcher.Analyze(FactoryProg(), cal);

            Assert.Equal(Hc08TpsStatus.UpdateNeeded, a.Status);
            Assert.True(a.AxisRewritten);
            Assert.Equal([15], a.Rows.Where(r => r.AxisChanged).Select(r => r.Index));

            // round(7468.75 / 31.875) = 234; the other 15 breakpoints convert to the factory bytes.
            byte[] expectedAxis = [.. Hc08Axis[..15], 0xEA];
            Assert.Equal(expectedAxis, a.PatchedProg.Span.Slice(Hc08Offset + 0x32, 16).ToArray());
            Assert.Equal(CalFactoryTable, Hc08TableOf(a.PatchedProg.Span));

            // Table raise lowers the sum by 18, the axis byte 0xDB -> 0xEA by 15 more.
            Assert.Equal(0xDA01 - 18 - 15, a.NewChecksum);
            Assert.Contains(Hc08Offset + 0x32 + 15, a.ChangedOffsets);
            Assert.Contains("replaced with the calibration axis", a.FormatReport());

            var again = Hc08TpsPatcher.Analyze(a.PatchedProg.ToArray(), cal);
            Assert.Equal(Hc08TpsStatus.UpToDate, again.Status);
            Assert.False(again.AxisRewritten);
        }

        [Fact]
        public void Analyze_AxisMismatchOnly_IsStillAnUpdate()
        {
            var cal = Cal(Hc08FactoryTable);
            cal[0x0E2E + 15] = 0xDF;

            var a = Hc08TpsPatcher.Analyze(FactoryProg(), cal);

            Assert.Equal(Hc08TpsStatus.UpdateNeeded, a.Status);
            Assert.All(a.Rows, r => Assert.NotEqual(Hc08TpsRowAction.Raise, r.Action));
            Assert.Equal([ImmOffset, ImmOffset + 1, Hc08Offset + 0x32 + 15], a.ChangedOffsets);
        }

        [Fact]
        public void Analyze_RawCopiedCalAxis_IsConvertedBackToHc08Units()
        {
            // The obsolete t6_patch_hc08_firmware.py copied the calibration axis bytes raw into the
            // HC08 axis (with a valid checksum). Converting fixes that back to the factory axis.
            var prog = FactoryProg();
            CalAxis.CopyTo(prog, Hc08Offset + 0x32);
            ushort sum = Hc08TpsPatcher.ImageChecksum(prog, Hc08Offset);
            BinaryPrimitives.WriteUInt16BigEndian(prog.AsSpan(ImmOffset), sum);

            var a = Hc08TpsPatcher.Analyze(prog, Cal(Hc08FactoryTable));

            Assert.True(a.AxisRewritten);
            Assert.Equal(Hc08Axis, a.PatchedProg.Span.Slice(Hc08Offset + 0x32, 16).ToArray());
        }

        [Theory]
        [InlineData(0x10)] // decreasing
        [InlineData(0xBF)] // equal to the previous breakpoint
        public void Analyze_CalAxisNotIncreasing_Throws(byte last)
        {
            var cal = Cal(CalFactoryTable);
            cal[0x0E2E + 15] = last;

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(FactoryProg(), cal));
            Assert.Contains("calibration rpm axis is not strictly increasing", ex.Message);
            Assert.Contains("[15]", ex.Message);
            Assert.Single(ex.Message.Split(Environment.NewLine), l => l.Contains("is not above"));
        }

        [Fact]
        public void Analyze_Hc08AxisNotIncreasing_Throws()
        {
            var prog = FactoryProg();
            prog[Hc08Offset + 0x32 + 8] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(prog.AsSpan(ImmOffset), Hc08TpsPatcher.ImageChecksum(prog, Hc08Offset));

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(prog, Cal(CalFactoryTable)));
            Assert.Contains("HC08 rpm axis is not strictly increasing", ex.Message);
        }

        [Fact]
        public void Analyze_CalBreakpointBeyondHc08Range_Throws()
        {
            var cal = Cal(CalFactoryTable);
            cal[0x0E2E + 15] = 0xF5; // 8156.25 rpm; the HC08 axis tops out at 8128 rpm

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(FactoryProg(), cal));
            Assert.Contains("outside the range", ex.Message);
            Assert.Contains("[15]", ex.Message);
        }

        [Fact]
        public void Analyze_CalBreakpointsCollapsingToOneHc08Step_Throws()
        {
            var cal = Cal(CalFactoryTable);
            cal[0x0E2E + 0] = 0x09; // 781.25 rpm  -> HC08 25
            cal[0x0E2E + 1] = 0x0A; // 812.5 rpm   -> HC08 25

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(FactoryProg(), cal));
            Assert.Contains("converted HC08 rpm axis is not strictly increasing", ex.Message);
        }

        [Fact]
        public void Analyze_TamperedHc08Table_ReportsInconsistentChecksum()
        {
            var patched = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(CalFactoryTable)).PatchedProg.ToArray();
            patched[0x91436] ^= 0x01; // D9 -> D8 without fixing the immediate

            var ex = Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Analyze(patched, Cal(CalFactoryTable)));
            Assert.Contains("computed D9F0, embedded D9EF", ex.Message);
        }

        [Fact]
        public void FormatReport_MatchesReferenceLayout()
        {
            string report = Hc08TpsPatcher.Analyze(FactoryProg(), Cal(Cal93OctaneTable)).FormatReport();

            Assert.Contains("Calibration : C132E0278  (profile C132E0278)", report);
            Assert.Contains("HC08 image  : PROG+0x913f0, table @ HC08 0xdc42", report);
            Assert.Contains("Checksum    : insn imm @ PROG+0x337c2", report);
            Assert.Contains("    0    812  8D  55.3%  8D  55.3%  8D  55.3%" + Environment.NewLine, report);
            Assert.Contains("    3   1500  CE  80.8%  BF  74.9%  CE  80.8%  keep (cal -5.9%, decrease skipped)", report);
            Assert.Contains("    8   3500  B4  70.6%  D9  85.1%  D9  85.1%  raise +14.5%", report);
            Assert.Contains("HC08 checksum: DA01 -> D95D", report);
        }

        [Fact]
        public void Apply_WritesPatchAndBackup()
        {
            var original = FactoryProg();
            string path = Path.Combine(workDir, "T6EVRGT430E01_BIN.cpt");
            File.WriteAllBytes(path, original);

            var a = Hc08TpsPatcher.AnalyzeFiles(path, WriteCal(Cal93OctaneTable));
            string? backup = Hc08TpsPatcher.Apply(path, a);

            Assert.Equal(a.PatchedProg.ToArray(), File.ReadAllBytes(path));
            Assert.NotNull(backup);
            Assert.Equal(original, File.ReadAllBytes(backup));
            Assert.Single(Directory.GetFiles(workDir, "*.bak"));
            Assert.Empty(Directory.GetFiles(workDir, "*.tmp"));
        }

        [Fact]
        public void Apply_WithoutBackup_WritesOnlyProg()
        {
            string path = Path.Combine(workDir, "prog.cpt");
            File.WriteAllBytes(path, FactoryProg());

            var a = Hc08TpsPatcher.AnalyzeFiles(path, WriteCal(Cal93OctaneTable));

            Assert.Null(Hc08TpsPatcher.Apply(path, a, backup: false));
            Assert.Empty(Directory.GetFiles(workDir, "*.bak"));
        }

        [Fact]
        public void Apply_FileChangedSinceAnalysis_RefusesAndLeavesFile()
        {
            string path = Path.Combine(workDir, "prog.cpt");
            File.WriteAllBytes(path, FactoryProg());
            var a = Hc08TpsPatcher.AnalyzeFiles(path, WriteCal(Cal93OctaneTable));

            var changed = FactoryProg();
            changed[0x10] = 0x42;
            File.WriteAllBytes(path, changed);

            Assert.Throws<Hc08PatchException>(() => Hc08TpsPatcher.Apply(path, a));
            Assert.Equal(changed, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(workDir, "*.bak"));
        }

        [Fact]
        public void Apply_WhenUpToDate_Throws()
        {
            string path = Path.Combine(workDir, "prog.cpt");
            File.WriteAllBytes(path, FactoryProg());
            var a = Hc08TpsPatcher.AnalyzeFiles(path, WriteCal(Hc08FactoryTable));

            Assert.Throws<InvalidOperationException>(() => Hc08TpsPatcher.Apply(path, a));
        }

        private string WriteCal(byte[] table)
        {
            string path = Path.Combine(workDir, "C132E0278_TAB.cpt");
            File.WriteAllBytes(path, Cal(table));
            return path;
        }
    }
}
