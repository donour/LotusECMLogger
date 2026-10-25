using System.Buffers.Binary;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class A2lTests
{
    private static CcpBuildProfile Build(string name) => CcpBuildProfile.Known.Single(p => p.Build == name);

    private static SymbolCatalog Catalog(CcpBuildProfile build) => SymbolCatalogLoader.LoadByName(build.Catalog)
        ?? throw new InvalidOperationException($"{build.Catalog} catalog missing from the test output.");

    private static A2lResolvedModule Generated(string build) =>
        A2lLayout.Resolve(A2lParser.Parse(A2lGenerator.Generate(Catalog(Build(build)), Build(build)).Text));

    /// <summary>The cell lookup_3D_uint8_interpolated returns when both inputs hit axis points exactly:
    /// <c>lut[y * size_x + x]</c> (C132E0278.c:22510, <c>lut[(int)(short)uVar9 * (int)(short)uVar12 + (int)(short)uVar10]</c>,
    /// uVar9 the y index, uVar12 = size_x, uVar10 the x index).</summary>
    private static int FirmwareCellIndex(int sizeX, int x, int y) => y * sizeX + x;

    // C132E0278.c:10986  lookup_3D_uint8_interpolated(8, 8, coolant_temp, engine_speed_3,
    //     CAL_injtip_transient_throttle_response_time, …_X_coolant_tmp, …_Y_rpm)
    // C132E0278.c:15810  lookup_3D_uint8_interpolated(16, 12, engine_speed_3, temp_engine_air,
    //     CAL_ign_comp_manifold_temp, …_X_engine_speed, …_Y_manifold_temp)
    [Theory]
    [InlineData("CAL_injtip_transient_throttle_response_time", 8, 8, "_X_coolant_tmp", "_Y_rpm")]
    [InlineData("CAL_ign_comp_manifold_temp", 16, 12, "_X_engine_speed", "_Y_manifold_temp")]
    public void GeneratedMapsPutEachCellWhereTheFirmwareLookupReadsIt(string table, int sizeX, int sizeY, string xAxis, string yAxis)
    {
        var resolved = Generated("C132E0278");
        var map = resolved.Items[table];
        var catalogEntry = Catalog(Build("C132E0278")).Resolve(table)!;

        Assert.Equal(A2lItemKind.Map, map.Kind);
        Assert.Equal(A2lIndexMode.RowDir, map.IndexMode);
        Assert.Equal((sizeX, sizeY), (map.CountX, map.CountY));
        Assert.Equal(table + xAxis, map.Axes[0].AxisPtsName);
        Assert.Equal(table + yAxis, map.Axes[1].AxisPtsName);
        Assert.Equal(catalogEntry.Address, map.Address);
        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                Assert.Equal(catalogEntry.Address + (uint)FirmwareCellIndex(sizeX, x, y), map.CellAddress(x, y));
        Assert.Equal(Catalog(Build("C132E0278")).Resolve(table + xAxis)!.Address, map.Axes[0].Address);
    }

    [Fact]
    public void RowDirStoresXFastestAndColumnDirStoresYFastest()
    {
        // ASAM's ROW_DIR / COLUMN_DIR example (as Intrepid's ASAP2 guide shows it): for a map with Y rows (Y1, Y2)
        // and X columns (X1..X3), ROW_DIR is a11 a12 a13 a21 a22 a23 and COLUMN_DIR is a11 a21 a12 a22 a13 a23.
        foreach (var (mode, expected) in new[] { ("ROW_DIR", new[] { 0, 1, 2, 3, 4, 5 }), ("COLUMN_DIR", new[] { 0, 3, 1, 4, 2, 5 }) })
        {
            var map = A2lLayout.Resolve(A2lParser.Parse(Sample(fncValues: $"FNC_VALUES 1 UBYTE {mode} DIRECT"))).Items["MAP_3X2"];
            int[] order = new int[6];
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    order[(int)(map.CellAddress(x, y) - map.Address)] = y * 3 + x; // a(y+1)(x+1) as 0..5, row-major
            Assert.Equal(expected, order);
        }
    }

    [Theory]
    [InlineData("B132E0091")]
    [InlineData("C132E0278")]
    public void GeneratedFilesLoadCompletelyAndStayInsideCalibrationRam(string name)
    {
        var build = Build(name);
        var result = A2lGenerator.Generate(Catalog(build), build);
        var module = A2lParser.Parse(result.Text);
        var resolved = A2lLayout.Resolve(module);

        Assert.Empty(module.Diagnostics);
        Assert.Empty(resolved.Unavailable);
        Assert.Equal(A2lByteOrder.MsbFirst, module.ByteOrder);
        Assert.Equal(build.Build, module.Epk);
        Assert.Equal(build.CalibrationBase, module.EpkAddress);
        Assert.Equal(new A2lMemorySegment("CAL", "DATA", "RAM", build.CalibrationBase, build.CalibrationSize), module.CalibrationSegment);
        Assert.Equal(result.Maps, resolved.Items.Values.Count(i => i.Kind == A2lItemKind.Map));
        Assert.Equal(result.Curves, resolved.Items.Values.Count(i => i.Kind == A2lItemKind.Curve));
        Assert.True(result.Maps > 70 && result.Curves > 120, $"{result.Maps} maps, {result.Curves} curves");
        foreach (var item in resolved.Items.Values)
            foreach (var (address, length) in item.Spans())
                Assert.True(build.ContainsCalibrationRange(address, length), $"{item.Name} 0x{address:X8}+{length}");
        foreach (var item in resolved.Items.Values.Where(i => i.Kind is A2lItemKind.Curve or A2lItemKind.Map))
        {
            Assert.All(item.Axes, a => Assert.Null(a.InputQuantity)); // inputs are added by hand, never guessed
            Assert.Contains("_X_", item.Axes[0].AxisPtsName);
            Assert.Equal(Catalog(build).Resolve(item.Name)!.Type.ArrayLength, item.ElementCount);
        }
        // Every catalog uint8_t/uchar value is editable; only values whose size was inferred are read-only.
        Assert.All(resolved.Items.Values.Where(i => !i.IsEditable), i => Assert.Contains("READ_ONLY", i.ReadOnlyReason));
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var build = Build("C132E0278");
        Assert.Equal(A2lGenerator.Generate(Catalog(build), build).Text, A2lGenerator.Generate(Catalog(build), build).Text);
    }

    [Fact]
    public void GeneratedConversionsMatchTheCatalogScaling()
    {
        var resolved = Generated("C132E0278");
        var axis = resolved.Items["CAL_injtip_transient_throttle_response_time_X_coolant_tmp"];
        Assert.Equal(A2lItemKind.AxisPts, axis.Kind);
        Assert.Equal(8, axis.CountX);
        Assert.Equal("°C", axis.Conversion.Unit);
        Assert.Equal(0.0, axis.Conversion.ToPhysical(64), 9);   // 64 · 5/8 − 40
        Assert.Equal(64.0, axis.Conversion.ToRaw(0), 9);
        var rpm = resolved.Items["CAL_injtip_transient_throttle_response_time_Y_rpm"].Conversion;
        Assert.Equal(500 + 100 * 125 / 4.0, rpm.ToPhysical(100), 9);
        var cell = resolved.Items["CAL_injtip_transient_throttle_response_time"].Conversion;
        Assert.Equal(50.0, cell.ToPhysical(10), 9);            // u8_time_5ms
    }

    [Fact]
    public void UcharIsAByte() => Assert.Equal((1, TypeConfidence.BaseType),
        (ChannelTypeParser.Parse("uchar").Size, ChannelTypeParser.Parse("uchar").Confidence));

    // ── The reader on hand-written files ─────────────────────────────────────────────────────────────────

    private static string Sample(string fncValues = "FNC_VALUES 1 UBYTE ROW_DIR DIRECT", string extraModule = "",
        string modCommon = "/begin MOD_COMMON \"\" BYTE_ORDER MSB_FIRST /end MOD_COMMON") => $$"""
        ASAP2_VERSION 1 61
        /begin A2ML
          struct Protocol_Layer { uint; uint; } ; /* A2ML has its own grammar; skipped whole */
        /end A2ML
        /begin PROJECT P "sample"
          /begin MODULE M "the \"module\""
            {{modCommon}}
            /begin MOD_PAR "" EPK "B132E0091" ADDR_EPK 0x40008E24
              /begin MEMORY_SEGMENT CAL "" DATA RAM INTERN 0x40008E24 0x61D8 -1 -1 -1 -1 -1 /end MEMORY_SEGMENT
            /end MOD_PAR
            /begin IF_DATA XCP /begin DAQ STATIC 0 /end DAQ "anything" /end IF_DATA
            /begin VENDOR_THING whatever 1 2 3 /end VENDOR_THING
            /begin RECORD_LAYOUT RL_U8 {{fncValues}} /end RECORD_LAYOUT
            /begin RECORD_LAYOUT RL_AX_U8 AXIS_PTS_X 1 UBYTE INDEX_INCR DIRECT /end RECORD_LAYOUT
            /begin COMPU_METHOD CM_TEMP "" RAT_FUNC "%6.3" "degC" COEFFS 0 1 40 0 0 0.625 /end COMPU_METHOD
            /begin AXIS_PTS AX_X "x" 0x40009000 NO_INPUT_QUANTITY RL_AX_U8 0 CM_TEMP 3 -40 119 /end AXIS_PTS
            /begin AXIS_PTS AX_Y "y" 0x40009003 NO_INPUT_QUANTITY RL_AX_U8 0 NO_COMPU_METHOD 2 0 255 /end AXIS_PTS
            /begin CHARACTERISTIC MAP_3X2 "" MAP 0x40009010 RL_U8 0 NO_COMPU_METHOD 0 255
              /begin AXIS_DESCR COM_AXIS engine_speed CM_TEMP 3 -40 119 AXIS_PTS_REF AX_X /end AXIS_DESCR
              /begin AXIS_DESCR COM_AXIS NO_INPUT_QUANTITY NO_COMPU_METHOD 2 0 255 AXIS_PTS_REF AX_Y /end AXIS_DESCR
            /end CHARACTERISTIC
            /begin MEASUREMENT engine_speed "rpm" UWORD NO_COMPU_METHOD 0 0 0 65535 ECU_ADDRESS 0x40001000 /end MEASUREMENT
            {{extraModule}}
          /end MODULE
        /end PROJECT
        """;

    [Fact]
    public void ReadsTheSampleAndSkipsWhatItDoesNotKnow()
    {
        var module = A2lParser.Parse(Sample());
        var resolved = A2lLayout.Resolve(module);
        Assert.Empty(module.Diagnostics);
        Assert.Equal(("B132E0091", 0x40008E24u), (module.Epk, module.EpkAddress));
        var map = resolved.Items["MAP_3X2"];
        Assert.Equal("engine_speed", map.Axes[0].InputQuantity);
        Assert.Null(map.Axes[1].InputQuantity);
        Assert.Equal(0x40009003u + 1, map.Axes[1].PointAddress(1));
        Assert.Equal(0x40001000u, module.Measurements["engine_speed"].Address);
        Assert.True(map.IsEditable);
    }

    [Fact]
    public void AssumesMsbFirstWhenTheByteOrderIsMissing()
    {
        var module = A2lParser.Parse(Sample(modCommon: ""));
        Assert.Equal(A2lByteOrder.MsbFirst, module.ByteOrder);
        Assert.Contains(module.Diagnostics, d => d.Message.Contains("MSB_FIRST"));
    }

    [Theory]
    [InlineData("FNC_VALUES 1 UBYTE ALTERNATE_WITH_X DIRECT", "ALTERNATE_WITH_X")]
    [InlineData("FNC_VALUES 1 UBYTE ROW_DIR PBYTE", "PBYTE")]
    [InlineData("FNC_VALUES 2 UBYTE ROW_DIR DIRECT NO_AXIS_PTS_X 1 UBYTE", "NO_AXIS_PTS_X")]
    [InlineData("FNC_VALUES 1 FLOAT16_IEEE ROW_DIR DIRECT", "FLOAT16_IEEE")]
    public void UnsupportedRecordLayoutsMakeItemsUnavailableNotDefaulted(string fncValues, string named)
    {
        var resolved = A2lLayout.Resolve(A2lParser.Parse(Sample(fncValues)));
        Assert.DoesNotContain("MAP_3X2", resolved.Items.Keys);
        Assert.Contains(resolved.Unavailable, u => u.Name == "MAP_3X2" && u.Reason.Contains(named));
    }

    [Theory]
    [InlineData("BIT_MASK 0x0F", "BIT_MASK")]
    [InlineData("ECU_ADDRESS_EXTENSION 1", "ECU_ADDRESS_EXTENSION")]
    [InlineData("BYTE_ORDER MSB_FIRST_MSW_LAST", "MSB_FIRST_MSW_LAST")]
    public void UnsupportedCharacteristicFeaturesMakeItUnavailable(string option, string named)
    {
        var resolved = A2lLayout.Resolve(A2lParser.Parse(Sample(extraModule:
            $"/begin CHARACTERISTIC V \"\" VALUE 0x40009100 RL_U8 0 NO_COMPU_METHOD 0 255 {option} /end CHARACTERISTIC")));
        Assert.Contains(resolved.Unavailable, u => u.Name == "V" && u.Reason.Contains(named));
    }

    [Fact]
    public void ConversionsWithoutAnInverseAreReadOnly()
    {
        var resolved = A2lLayout.Resolve(A2lParser.Parse(Sample(extraModule: """
            /begin COMPU_METHOD CM_FORM "" FORM "%6.2" "V" /begin FORMULA "X1 * 5 / 1023" /end FORMULA /end COMPU_METHOD
            /begin COMPU_METHOD CM_FORM_INV "" FORM "%6.2" "V" /begin FORMULA "X1 * 5 / 1023" FORMULA_INV "X1 * 1023 / 5" /end FORMULA /end COMPU_METHOD
            /begin CHARACTERISTIC F1 "" VALUE 0x40009100 RL_U8 0 CM_FORM 0 5 /end CHARACTERISTIC
            /begin CHARACTERISTIC F2 "" VALUE 0x40009101 RL_U8 0 CM_FORM_INV 0 5 /end CHARACTERISTIC
            /begin CHARACTERISTIC F3 "" VALUE 0x40009102 RL_U8 0 NO_COMPU_METHOD 0 255 READ_ONLY /end CHARACTERISTIC
            """)));
        Assert.Contains("no inverse", resolved.Items["F1"].ReadOnlyReason);
        Assert.True(resolved.Items["F2"].IsEditable);
        Assert.Equal(1023.0, resolved.Items["F2"].Conversion.ToRaw(5), 9);
        Assert.Contains("READ_ONLY", resolved.Items["F3"].ReadOnlyReason);
    }

    [Fact]
    public void ReadsVerbalTablesLinearAndFixedAxes()
    {
        var module = A2lParser.Parse(Sample(extraModule: """
            /begin COMPU_VTAB VT_GEAR "" TAB_VERB 3 0 "neutral" 1 "first" 2 "second" DEFAULT_VALUE "other" /end COMPU_VTAB
            /begin COMPU_METHOD CM_GEAR "" TAB_VERB "%3.0" "" COMPU_TAB_REF VT_GEAR /end COMPU_METHOD
            /begin COMPU_METHOD CM_LIN "" LINEAR "%6.2" "kPa" COEFFS_LINEAR 0.5 -10 /end COMPU_METHOD
            /begin CHARACTERISTIC FIXED_CURVE "" CURVE 0x40009200 RL_U8 0 CM_LIN 0 100
              /begin AXIS_DESCR FIX_AXIS NO_INPUT_QUANTITY NO_COMPU_METHOD 4 0 255 FIX_AXIS_PAR 0 5 4 /end AXIS_DESCR
            /end CHARACTERISTIC
            """));
        Assert.Equal("first", module.Compu("CM_GEAR").ToText(1));
        Assert.Equal("other", module.Compu("CM_GEAR").ToText(7));
        Assert.Equal(15.0, module.Compu("CM_LIN").ToPhysical(50), 9);
        Assert.Equal(50.0, module.Compu("CM_LIN").ToRaw(15), 9);
        var curve = A2lLayout.Resolve(module).Items["FIXED_CURVE"];
        Assert.Equal(new double[] { 0, 32, 64, 96 }, curve.Axes[0].FixedRaw); // shift 5: spacing 2^5
        Assert.Equal(0x40009203u, curve.CellAddress(3));
    }

    [Fact]
    public void LaysOutStandardAxesInsideTheRecord()
    {
        // STD_AXIS: [x0 x1 x2 : UBYTE][pad][y0 y1 : UWORD][values : UWORD 3×2], ordered by Position, word-aligned.
        var item = A2lLayout.Resolve(A2lParser.Parse(Sample(extraModule: """
            /begin RECORD_LAYOUT RL_STD AXIS_PTS_X 1 UBYTE INDEX_INCR DIRECT AXIS_PTS_Y 2 UWORD INDEX_INCR DIRECT
              FNC_VALUES 3 UWORD COLUMN_DIR DIRECT /end RECORD_LAYOUT
            /begin CHARACTERISTIC STD "" MAP 0x40009300 RL_STD 0 NO_COMPU_METHOD 0 65535
              /begin AXIS_DESCR STD_AXIS NO_INPUT_QUANTITY NO_COMPU_METHOD 3 0 255 /end AXIS_DESCR
              /begin AXIS_DESCR STD_AXIS NO_INPUT_QUANTITY NO_COMPU_METHOD 2 0 65535 /end AXIS_DESCR
            /end CHARACTERISTIC
            """))).Items["STD"];
        Assert.Equal(0x40009300u, item.Axes[0].Address);
        Assert.Equal(0x40009304u, item.Axes[1].Address);
        Assert.Equal(0x40009308u, item.Address);
        Assert.Equal(0x40009308u + 2 * (1 * 2 + 1), item.CellAddress(1, 1)); // COLUMN_DIR: x · countY + y
    }

    [Fact]
    public void ToleratesCommentsEscapesAndBadBlocks()
    {
        var module = A2lParser.Parse(""""
            // line comment
            /begin PROJECT P "" /begin MODULE M "say \"hi\" and ""bye"""
              /begin MOD_COMMON "" BYTE_ORDER MSB_LAST /end MOD_COMMON
              /* a /begin inside a comment is ignored */
              /begin COMPU_METHOD BAD "" RAT_FUNC "%5.2" "" COEFFS 1 0 0 0 0 1 /end COMPU_METHOD
              /begin CHARACTERISTIC SHORT "" VALUE /end CHARACTERISTIC
              /include "other.a2l"
            /end MODULE /end PROJECT
            """");
        Assert.Equal(A2lByteOrder.MsbLast, module.ByteOrder);
        Assert.Contains("quadratic", module.CompuMethods["BAD"].Problem);
        Assert.Contains(module.Diagnostics, d => d.Message.Contains("SHORT"));
        Assert.Contains(module.Diagnostics, d => d.Message.Contains("/include"));
    }

    [Theory]
    [InlineData("X1 >> 2 & 0xff", 1023, 255)]
    [InlineData("X >> 2 & 0x0f", 1023, 15)]
    [InlineData("1 + 2 * 3 - X", 1, 6)]
    [InlineData("(X1 + 4) / 2", 6, 5)]
    [InlineData("X1 < 10 && X1 != 3", 2, 1)]
    [InlineData("~X1 & 0xff", 0x0f, 0xf0)]
    [InlineData("-X1 + abs(-3) + pow(2, 3)", 1, 10)]
    public void EvaluatesFormulas(string text, double x, double expected) =>
        Assert.Equal(expected, A2lFormula.Parse(text).Evaluate([x]), 9);

    [Fact]
    public void FormulasNameWhatTheyCannotRead()
    {
        Assert.Contains("frobnicate", Assert.Throws<FormatException>(() => A2lFormula.Parse("frobnicate(X)")).Message);
        Assert.Throws<FormatException>(() => A2lFormula.Parse("X1 +"));
        Assert.Equal(2, A2lFormula.Parse("X1 + X2").InputCount);
    }

    [Fact]
    public void RawValuesRoundTripInBothByteOrders()
    {
        var bytes = A2lDataType.UWord.EncodeRaw(0x1234, A2lByteOrder.MsbFirst);
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(bytes));
        Assert.Equal(0x3412, A2lDataType.UWord.ReadRaw(bytes, A2lByteOrder.MsbLast));
        Assert.Equal(-2.0, A2lDataType.SWord.ReadRaw(A2lDataType.SWord.EncodeRaw(-2.4, A2lByteOrder.MsbFirst), A2lByteOrder.MsbFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => A2lDataType.UByte.EncodeRaw(256, A2lByteOrder.MsbFirst));
    }
}
