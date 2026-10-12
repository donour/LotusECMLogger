using System.Buffers.Binary;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpVariableTests : IDisposable
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private readonly FakeCcpSlave _slave = new();
    private CcpMaster? _master;

    public void Dispose()
    {
        _master?.Dispose();
        _slave.Dispose();
    }

    private static SymbolEntry Symbol(string name, uint address, string type) =>
        new() { Name = name, Address = address, RawType = type, Type = ChannelTypeParser.Parse(type) };

    private static SymbolCatalog Real => SymbolCatalogLoader.LoadByName("C132E0278")
        ?? throw new InvalidOperationException("C132E0278 catalog missing from the test output.");

    private static T6SymbolRegistry Registry(params (uint Address, byte Size)[] entries)
    {
        var bytes = new byte[T6SymbolRegistry.HeaderSize + entries.Length * 6];
        "TEST PROGRAM"u8.CopyTo(bytes);
        "V0001"u8.CopyTo(bytes.AsSpan(32));
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44 + i * 6), entries[i].Address);
            bytes[44 + i * 6 + 4] = entries[i].Size;
        }
        return T6SymbolRegistry.ParseTable(bytes, entries.Length);
    }

    [Fact]
    public void JoinsTheCatalogWithTheVariableTable()
    {
        var catalog = new SymbolCatalog("test",
        [
            Symbol("engine_speed_16bit", 0x40001762, "u16_rspeed_rpm"),
            Symbol("knock_retard", 0x40001E38, "u8_angle_1/4deg[6]"),
            Symbol("CAL_table", 0x40008A9A, "u8_time_5ms[64]"),
            Symbol("CAL_scalar", 0x40008700, "u16_factor_1/256"),
        ]);
        var registry = Registry((0x40001762, 2), (0x40001E38, 6), (0x40002000, 10), (0x40003000, 4));
        var variables = CcpVariableCatalog.Build(catalog, registry, Gt430);

        var rpm = variables.Single(v => v.Name == "engine_speed_16bit");
        Assert.Equal((0x40001762u, 2, 0, false), (rpm.Address, rpm.Size, rpm.RegistryIndex, rpm.IsCalibration));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => $"knock_retard[{i}]"), variables.Where(v => v.Name.StartsWith("knock")).Select(v => v.Name));
        Assert.Equal(0.25, variables.Single(v => v.Name == "knock_retard[5]").Scale);
        Assert.DoesNotContain(variables, v => v.Name.StartsWith("CAL_table")); // tables are read, not watched
        Assert.True(variables.Single(v => v.Name == "CAL_scalar").IsCalibration);
        var unnamed = variables.Where(v => v.Name.StartsWith("var#")).ToList();
        Assert.Equal([("var#2", 10, false), ("var#3", 4, true)], unnamed.Select(v => (v.Name, v.Size, v.Watchable)));
        Assert.Equal(variables.Select(v => v.Address).Order(), variables.Select(v => v.Address));
    }

    [Fact]
    public void TheRealCatalogYieldsWatchableVariables()
    {
        var variables = CcpVariableCatalog.Build(Real, null, Gt430);
        Assert.Contains(variables, v => v is { Name: "engine_speed_16bit", Address: 0x40001762, Size: 2, Watchable: true });
        Assert.Contains(variables, v => v is { Name: "obd_ii_ign_adv_per_cylinder[5]", Address: 0x4000192E, Signed: true, Scale: 0.25 });
        Assert.True(variables.Count > 500);
    }

    [Fact]
    public void LooksUpTheSymbolAndTableCellAtAnAddress()
    {
        var lookup = new SymbolAddressLookup(Real);
        Assert.Equal("CAL_injtip_transient_throttle_response_time[y=3][x=7]", lookup.At(0x40008A9Au + 3 * 8 + 7));
        Assert.Equal("CAL_injtip_transient_throttle_response_time_X_coolant_tmp[2]", lookup.At(0x40008A8C));
        Assert.Equal("engine_speed_16bit", lookup.At(0x40001762));
        Assert.Equal("engine_speed_16bit+1", lookup.At(0x40001763));
        Assert.Null(lookup.At(0x3FFFFFFF));
    }

    [Fact]
    public void DiffsAreRunsAttributedToSymbols()
    {
        byte[] ecu = [1, 2, 3, 4, 5, 6, 7, 8], file = [1, 9, 9, 4, 5, 6, 7, 0];
        var runs = CcpMemoryDiff.Compare(0x40008A9A, ecu, file, new SymbolAddressLookup(Real).At);
        Assert.Equal([(0x40008A9Bu, "0203", "0909"), (0x40008AA1u, "08", "00")],
            runs.Select(r => (r.Address, Convert.ToHexString(r.Ecu), Convert.ToHexString(r.File))));
        Assert.Equal("CAL_injtip_transient_throttle_response_time[y=0][x=1]", runs[0].Symbol);
        string report = CcpMemoryDiff.Format(runs);
        Assert.StartsWith("2 differing run(s), 3 byte(s).", report);
        Assert.Contains("0x40008A9B      2 B  CAL_injtip_transient_throttle_response_time[y=0][x=1]", report);
        Assert.Equal("No differences.\r\n", CcpMemoryDiff.Format(CcpMemoryDiff.Compare(0, ecu, ecu)));
    }

    [Fact]
    public async Task WatchesVariablesLiveThroughDaq()
    {
        _slave.Load(0x40001762, [0x03, 0x20]);   // 800 rpm
        _slave.Load(0x40001E38, [0x10]);         // 4°
        _slave.Load(0x40001924, [0xFF, 0xF8]);   // −2°
        var variables = CcpVariableCatalog.Build(Real, null, Gt430)
            .Where(v => v.Name is "engine_speed_16bit" or "obd_ii_ign_adv_per_cylinder[0]" || v.Address == 0x40001E38)
            .ToList();
        _master = new CcpMaster(new SimulatedCcpTransport(_slave, speedFactor: 20));
        await _master.ConnectAsync();
        await using var watch = await CcpVariableWatch.StartAsync(_master, variables, 10);
        for (int i = 0; i < 300 && watch.Snapshot().Any(v => v is null); i++) await Task.Delay(10);

        var values = watch.Snapshot();
        var byName = variables.Select((v, i) => (v.Name, values[i]!.Value.Value)).ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal(800.0, byName["engine_speed_16bit"]);
        Assert.Equal(-2.0, byName["obd_ii_ign_adv_per_cylinder[0]"]);
        Assert.Equal("0320", values[variables.FindIndex(v => v.Name == "engine_speed_16bit")]!.Value.RawHex);
        Assert.Equal(CcpVariableWatch.OwnerName, Assert.Single(_master.GetDaqAllocations()).Owner);

        await watch.StopAsync();
        Assert.Empty(_master.GetDaqAllocations());
        Assert.False(watch.IsActive);
    }

    [Fact]
    public async Task ObjectsLargerThanFourBytesCannotBeWatched()
    {
        _master = new CcpMaster(new SimulatedCcpTransport(_slave));
        var large = new CcpVariable("var#2", 0x40002000, 10, false, 1, 0, "", 2, false, "");
        await Assert.ThrowsAsync<ArgumentException>(() => CcpVariableWatch.StartAsync(_master, [large], 10));
    }
}
