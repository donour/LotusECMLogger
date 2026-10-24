using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>Phase 6e: axis inputs drafted from the decompile, bound in the A2L, and streamed as a live cursor.</summary>
public sealed class LiveCursorTests
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static SymbolCatalog Catalog => SymbolCatalogLoader.LoadByName(Gt430.Catalog)!;
    private const string Fan = "CAL_cooling_fan_afterrun"; // 8 × 8: X temp_engine_air, Y coolant_temp

    [Theory]
    [InlineData(5, 0, 0.0)]      // below the axis: clamped to the first point
    [InlineData(10, 0, 0.0)]
    [InlineData(15, 0, 0.5)]
    [InlineData(20, 1, 0.0)]
    [InlineData(35, 2, 0.25)]    // a quarter of the way from 30 to 50
    [InlineData(99, 4, 0.0)]     // above: clamped to the last point
    public void LocatesInputsBetweenRawAxisPoints(double input, int index, double fraction)
    {
        var position = OperatingPoint.Locate([10, 20, 30, 50, 70], input);
        Assert.Equal(index, position.Index);
        Assert.Equal(fraction, position.Fraction, 9);
    }

    [Fact]
    public void RepeatedPointsGiveFractionZeroAndTheLastEqualPoint()
    {
        Assert.Equal(new AxisPosition(2, 0), OperatingPoint.Locate([10, 20, 20, 30], 20));
        Assert.Equal(new AxisPosition(2, 0.5), OperatingPoint.Locate([10, 20, 20, 30], 25));
    }

    [Fact]
    public void InterpolatesTheValueAtTheOperatingPoint()
    {
        var values = new double[2, 2] { { 0, 10 }, { 20, 30 } }; // [x, y]
        Assert.Equal(15, OperatingPoint.Interpolate(values, new AxisPosition(0, 0.5), new AxisPosition(0, 0.5)), 9);
        Assert.Equal(10, OperatingPoint.Interpolate(values, new AxisPosition(0, 0.5)), 9);
        Assert.Equal(30, OperatingPoint.Interpolate(values, new AxisPosition(1, 0), new AxisPosition(1, 0)), 9);
    }

    // ── Extraction ───────────────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, SymbolEntry> Vars = new[]
    {
        ("engine_speed_3", "u8_rspeed_125/4+500rpm"), ("load", "uint8_t"), ("speed16", "uint16_t"), ("signed8", "int8_t"),
    }.ToDictionary(v => v.Item1, v => new SymbolEntry { Name = v.Item1, RawType = v.Item2, Type = ChannelTypeParser.Parse(v.Item2) });

    [Fact]
    public void ExtractsInputsThatEveryCallSiteAgreesOn()
    {
        const string source = """
            uint8_t lookup_3D_uint8_interpolated
                      (uint8_t size_x,uint8_t size_y,uint16_t input_x,uint16_t input_y,uint8_t *lut,uint8_t *x_axis,uint8_t *y_axis)
            {
              return lut[0];
            }
            void f(void) {
              a = lookup_3D_uint8_interpolated(8,8,(ushort)engine_speed_3,speed16,CAL_t,CAL_t_X_rpm,CAL_t_Y_load);
              b = lookup_3D_uint8_interpolated('\b','\b',(ushort)engine_speed_3,load,CAL_t,CAL_t_X_rpm,CAL_t_Y_load);
              c = lookup_2D_uint8_interpolated(8,uVar3,CAL_u,CAL_u_X_rpm);
              d = lookup_2D_uint16_interpolated(8,(byte)speed16,CAL_v,CAL_v_X_rpm);
              e = lookup_2D_uint8_interpolated(8,signed8,CAL_w,CAL_w_X_rpm);
              g = lookup_2D_uint8_interpolated(8,speed16 >> 2,CAL_x,CAL_x_X_rpm + 5);
              h = lookup_2D_uint8_interpolated(8,speed16 >> 2 & 0xff,CAL_y,CAL_y_X_rpm);
            }
            """;
        var axes = new HashSet<string> { "CAL_t_X_rpm", "CAL_t_Y_load", "CAL_u_X_rpm", "CAL_v_X_rpm", "CAL_w_X_rpm", "CAL_x_X_rpm", "CAL_y_X_rpm" };
        var (bound, skipped) = AxisInputs.Extract(source, axes, Vars);
        var byAxis = bound.ToDictionary(b => b.Axis, b => b.Expression);

        Assert.Equal("engine_speed_3", byAxis["CAL_t_X_rpm"]);                     // both sites agree; a u8 needs no mask
        Assert.Equal("7 8", bound.Single(b => b.Axis == "CAL_t_X_rpm").Lines);
        Assert.Contains(skipped, s => s.Axis == "CAL_t_Y_load" && s.Reason.Contains("2 different inputs"));
        Assert.Contains(skipped, s => s.Axis == "CAL_u_X_rpm" && s.Reason.Contains("uVar3"));
        Assert.Contains(skipped, s => s.Axis == "CAL_v_X_rpm" && s.Reason.Contains("(byte) cast"));
        Assert.Equal("signed8 & 0xff", byAxis["CAL_w_X_rpm"]);                     // signed: the lookup sees the low byte
        Assert.DoesNotContain("CAL_x_X_rpm", byAxis.Keys);                          // offset axis pointer
        Assert.Equal("speed16 >> 2 & 0xff", byAxis["CAL_y_X_rpm"]);                 // already masked
        Assert.Equal("(speed16 >> 2) & 0xff", AxisInputs.Normalize("speed16 >> 2", 0xFF, Vars, out _));
        Assert.Null(AxisInputs.Normalize("COD_base.COD[0]", 0xFF, Vars, out var why));
        Assert.Contains("not a plain expression", why);
    }

    [Fact]
    public void TheInputsFileRoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"inputs-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, AxisInputs.Format([new AxisInput("CAL_a_X_b", "(x >> 2) & 0xff", "lookup_2D_uint8_interpolated", "1 2")], "test.c"));
            Assert.Equal("(x >> 2) & 0xff", AxisInputs.Load(path)["CAL_a_X_b"]);
        }
        finally { File.Delete(path); }
    }

    // ── Generation and resolution ────────────────────────────────────────────────────────────────────────

    private static A2lResolvedModule Generate(Dictionary<string, string> inputs, out A2lGenerationResult result)
    {
        result = A2lGenerator.Generate(Catalog, Gt430, inputs);
        return A2lLayout.Resolve(A2lParser.Parse(result.Text));
    }

    [Fact]
    public void BoundAxesCarryDirectOrVirtualInputQuantities()
    {
        var a2l = Generate(new()
        {
            [Fan + "_X_air_temp"] = "temp_engine_air",
            [Fan + "_Y_coolant_temp"] = "(coolant_temp >> 1) & 0xff",
            ["CAL_not_an_axis"] = "coolant_temp",
        }, out var result);
        Assert.Equal(2, result.Inputs);
        Assert.Contains(result.Warnings, w => w.Contains("CAL_not_an_axis"));
        var map = a2l.Items[Fan];
        Assert.Equal("temp_engine_air", map.Axes[0].InputQuantity);
        Assert.Equal("V_" + Fan + "_Y_coolant_temp", map.Axes[1].InputQuantity);
        Assert.Equal("temp_engine_air", a2l.Module.AxisPts[Fan + "_X_air_temp"].InputQuantity);

        var (x, y, problems) = LiveCursor.Inputs(a2l.Module, map);
        Assert.Empty(problems);
        Assert.Null(x!.Formula);
        Assert.Equal(0x4000183Au, x.Sources.Single().Address);
        Assert.Equal(["coolant_temp"], y!.Sources.Select(s => s.Name));
        Assert.Equal(100, y.Evaluate([200.0]));
    }

    [Fact]
    public void UnknownVariablesAreLeftUnbound()
    {
        Generate(new() { [Fan + "_X_air_temp"] = "no_such_variable" }, out var result);
        Assert.Equal(0, result.Inputs);
        Assert.Contains(result.Warnings, w => w.Contains("no_such_variable"));
    }

    // ── Streaming ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCursorFollowsTheEcusInputsOverDaq()
    {
        var a2l = Generate(new()
        {
            [Fan + "_X_air_temp"] = "temp_engine_air",
            [Fan + "_Y_coolant_temp"] = "(coolant_temp >> 1) & 0xff",
        }, out _);
        var map = a2l.Items[Fan];
        var bytes = new byte[Gt430.CalibrationSize];
        Encoding.ASCII.GetBytes("C132E0278 01-01-2020 00:00").CopyTo(bytes, 0);
        var image = CalibrationImage.FromBytes(bytes);
        image.SetRaw(a2l.Items[Fan + "_X_air_temp"], Enumerable.Range(0, 8).Select(i => new CellValue(i, 0, 40 + 20 * i)).ToList());
        image.SetRaw(a2l.Items[Fan + "_Y_coolant_temp"], Enumerable.Range(0, 8).Select(i => new CellValue(i, 0, 10 * i)).ToList());

        using var slave = new FakeCcpSlave();
        slave.Load(Gt430.CalibrationBase, bytes.AsSpan(0, 32));
        slave.Load(0x4000183A, [90]);  // temp_engine_air: between 80 (point 2) and 100 (point 3)
        slave.Load(0x40001836, [150]); // coolant_temp: the lookup sees 150 >> 1 = 75, between 70 and 80
        using var master = new CcpMaster(new SimulatedCcpTransport(slave, speedFactor: 50),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromSeconds(1) });
        await master.ConnectAsync();
        await using var cursor = await LiveCursor.StartAsync(master, a2l.Module, map);

        OperatingPointSample? sample = null;
        for (int i = 0; i < 300 && sample is null; i++)
        {
            await Task.Delay(10);
            sample = cursor!.Read(image);
        }
        Assert.NotNull(sample);
        Assert.Equal((90.0, 75.0), (sample.XInput, sample.YInput));
        Assert.Equal(new AxisPosition(2, 0.5), sample.X);
        Assert.Equal(new AxisPosition(7, 0), sample.Y); // 75 is past the last point (70): clamped
        Assert.True(cursor!.IsActive);
    }

    [Fact]
    public async Task ItemsWithoutBoundAxesHaveNoCursor()
    {
        var a2l = Generate([], out _);
        using var slave = new FakeCcpSlave();
        using var master = new CcpMaster(new SimulatedCcpTransport(slave, speedFactor: 50));
        Assert.Null(await LiveCursor.StartAsync(master, a2l.Module, a2l.Items[Fan]));
    }
}
