using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LotusECMLogger;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class FirmwareSimulatorTests
{
    // Small invented programs test pluggability without distributing production firmware.
    private static (T6FirmwareProfile profile, byte[] program, byte[] cal) Fixture(byte bus = 2, bool alternate = false)
    {
        var program = new byte[0x400];
        var cal = new byte[64];
        string id = alternate ? "NA-TEST" : "GT-TEST";
        Encoding.ASCII.GetBytes(id + " ").CopyTo(cal, 0);
        cal[40] = bus;
        int dataOffset = alternate ? 0x300 : 0x200;
        uint registry = alternate ? 0x40080U : 0x40040U;
        Encoding.ASCII.GetBytes("TEST PROGRAM").CopyTo(program, (int)(registry - 0x40000 - 44));
        Encoding.ASCII.GetBytes("V0001").CopyTo(program, (int)(registry - 0x40000 - 12));
        BinaryPrimitives.WriteUInt32BigEndian(program.AsSpan((int)(registry - 0x40000)), 0x40000010);
        program[registry - 0x40000 + 4] = 2;
        program[dataOffset + 0x10] = 0xAB; program[dataOffset + 0x11] = 0xCD;
        Encoding.ASCII.GetBytes("Ccp_T6\0").CopyTo(program, dataOffset + 0x20);
        var profile = new T6FirmwareProfile(id, id, Convert.ToHexStringLower(SHA256.HashData(program)),
            alternate ? 0x40002000U : 0x40001000U, 64,
            alternate ? 0x40002028U : 0x40001028U, 0x40000U + (uint)dataOffset, 64,
            0x40000020, registry, 1);
        return (profile, program, cal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfilesDriveBootMemoryRegistryDaqAndReboot(bool alternate)
    {
        var (profile, program, cal) = Fixture(alternate: alternate);
        var image = new T6FirmwareImage(profile, program, cal);
        using var ecu = new FakeCcpSlave(image);
        var client = new CcpSimulationClient(ecu);
        client.Connect();
        Assert.Equal(2, ecu.Bus);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, client.ReadMemory(0x40000010, 2));
        Assert.Equal(image.Registry.Entries, client.ReadRegistry(profile).Entries);
        Assert.Equal(cal, client.ReadMemory(profile.CalibrationRamAddress, cal.Length));
        Assert.Equal(CcpFrame.AdditiveChecksum(cal), client.Checksum(profile.CalibrationRamAddress, cal.Length));
        client.WriteRam(0x40000010, [0x12, 0x34]);
        client.StartDaq(0x40000010, 2);
        Assert.All(client.Sample(100), packet => Assert.Equal(new byte[] { 0x12, 0x34 }, packet.Data.Span[..2].ToArray()));
        Assert.Equal(10, client.Sample(100).Count);
        client.WriteRam(profile.CalibrationRamAddress, [0xFF]);
        ecu.Reboot(); client = new CcpSimulationClient(ecu); client.Connect();
        Assert.Equal(new byte[] { 0xAB, 0xCD }, client.ReadMemory(0x40000010, 2));
        Assert.Equal(cal, client.ReadMemory(profile.CalibrationRamAddress, cal.Length));
        Assert.Empty(client.Sample(100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MasterIdentifiesTheProfiledBuildAndReadsItsRegistryAndCalibration(bool alternate)
    {
        var (profile, program, cal) = Fixture(alternate: alternate);
        var image = new T6FirmwareImage(profile, program, cal);
        using var ecu = new FakeCcpSlave(image);
        using var master = new CcpMaster(new SimulatedCcpTransport(ecu, speedFactor: 200));
        await master.ConnectAsync();
        var identification = await master.IdentifyAsync([profile.BuildProfile]);
        Assert.Equal(profile.BuildProfile, identification.Build);
        Assert.Equal(profile.CalibrationId, identification.CalibrationId);
        Assert.Equal("TEST PROGRAM", identification.RegistryBuildId);
        Assert.Equal(image.Registry.Entries, (await master.ReadSymbolRegistryAsync(identification.Build!)).Entries);
        Assert.Equal(cal, await master.ReadAsync(profile.CalibrationRamAddress, cal.Length));
        Assert.Equal(CcpFrame.AdditiveChecksum(cal), await master.ChecksumAsync(profile.CalibrationRamAddress, cal.Length));
    }

    [Fact]
    public async Task GuardedImageWriteUpdatesCalibrationRamAndKeepsTheBus()
    {
        var (profile, program, cal) = Fixture();
        using var ecu = new FakeCcpSlave(new T6FirmwareImage(profile, program, cal));
        using var master = new CcpMaster(new SimulatedCcpTransport(ecu, speedFactor: 200));
        await master.ConnectAsync();
        string backups = Path.Combine(Path.GetTempPath(), "ccp-fw-" + Guid.NewGuid());
        try
        {
            var writer = new CcpCalibrationWriter(master, profile.BuildProfile, CcpBuildSource.Identified, backups);
            var edited = (byte[])cal.Clone();
            edited[50] = 0x5A;
            edited[40] = 1; // the file would move CCP to FlexCAN A mid-write
            var plan = await writer.PrepareAsync(profile.CalibrationRamAddress, writer.CalibrationImageFromFile(edited), wholeImage: true);
            Assert.NotNull(plan.EnableBytePreserved);
            var result = await writer.ExecuteAsync(plan);
            Assert.Equal(cal, File.ReadAllBytes(result.BackupPath!));
            edited[40] = cal[40];
            Assert.Equal(edited, await master.ReadAsync(profile.CalibrationRamAddress, cal.Length));
            Assert.Equal(CcpFrame.AdditiveChecksum(edited), await master.ChecksumAsync(profile.CalibrationRamAddress, cal.Length));
        }
        finally { if (Directory.Exists(backups)) Directory.Delete(backups, recursive: true); }
    }

    private static CcpSelfTestOptions QuickSelfTest(T6FirmwareProfile profile) => new()
    {
        DaqWindow = TimeSpan.FromSeconds(1), // 20 virtual seconds at speed 20: enough samples at 1 Hz
        LatencySamples = 10,
        LatencyWarnMilliseconds = 1000, // simulator latency follows the OS timer, not the protocol
        Candidates = [profile.BuildProfile],
    };

    [Fact]
    public async Task SelfTestPassesAgainstTheFirmwareModel()
    {
        var (profile, program, cal) = Fixture();
        using var ecu = new FakeCcpSlave(new T6FirmwareImage(profile, program, cal));
        using var master = new CcpMaster(new SimulatedCcpTransport(ecu, speedFactor: 20));
        await master.ConnectAsync();
        var seen = new List<string>();
        var report = await CcpSelfTest.RunAsync(master, QuickSelfTest(profile), new SyncProgress<CcpCheckResult>(r => seen.Add(r.Name)));

        Assert.Equal(CcpCheckStatus.Pass, report.Overall);
        Assert.Equal(profile.CalibrationId, report.Build);
        Assert.Equal(report.Checks.Select(c => c.Name), seen);
        Assert.Equal(CcpCheckStatus.Skipped, report.Checks.Single(c => c.Name == "Engine speed").Status);
        var daq = report.Checks.Single(c => c.Name.StartsWith("DAQ"));
        Assert.Equal(CcpCheckStatus.Pass, daq.Status);
        Assert.Contains("event 0: 100.0 Hz (expected 100)", daq.Detail);
        Assert.Contains("event 9: 1.0 Hz (expected 1)", daq.Detail);
        Assert.Contains("DAQ samples arrived", daq.Detail);
        Assert.Contains("1 entries", report.Checks.Single(c => c.Name == "Variable table").Detail);
        Assert.Empty(master.GetDaqAllocations()); // the self-test released its lists

        string text = report.Format(["App: test"]);
        Assert.Contains("Overall:  PASS", text);
        Assert.Contains("App: test", text);
        Assert.Contains("[PASS   ] Checksum", text);
    }

    [Fact]
    public async Task SelfTestSkipsDaqWhileAnotherConsumerHoldsLists()
    {
        var (profile, program, cal) = Fixture();
        using var ecu = new FakeCcpSlave(new T6FirmwareImage(profile, program, cal));
        using var master = new CcpMaster(new SimulatedCcpTransport(ecu, speedFactor: 20));
        await master.ConnectAsync();
        await master.StartDaqAsync([new CcpDaqListRequest(0, 1, [[new CcpDaqElement(0x40000010, 2)]])], "High-Speed Log");
        var report = await CcpSelfTest.RunAsync(master, QuickSelfTest(profile));
        var daq = report.Checks.Single(c => c.Name.StartsWith("DAQ"));
        Assert.Equal(CcpCheckStatus.Skipped, daq.Status);
        Assert.Contains("High-Speed Log", daq.Detail);
        Assert.True(report.Overall == CcpCheckStatus.Pass, report.Format());
    }

    [Fact]
    public async Task SelfTestReportsAnEcuThatFallsSilent()
    {
        using var slave = new FakeCcpSlave();
        var script = new ScriptedTransport(new SimulatedCcpTransport(slave, speedFactor: 50));
        using var master = new CcpMaster(script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(100) });
        await master.ConnectAsync();
        var report = await CcpSelfTest.RunAsync(master, new CcpSelfTestOptions { LatencySamples = 5 },
            new SyncProgress<CcpCheckResult>(r => { if (r.Name == "Variable table") script.Drop = _ => true; }));

        Assert.Equal(CcpCheckStatus.Fail, report.Overall);
        Assert.Equal(CcpCheckStatus.Warn, report.Checks.Single(c => c.Name == "Identification").Status); // no CAL ID loaded
        Assert.Equal(CcpCheckStatus.Fail, report.Checks.Single(c => c.Name == "Command latency").Status);
        Assert.All(report.Checks.SkipWhile(c => c.Name != "Memory read").TakeWhile(c => c.Name != "Session counters"),
            c => Assert.Equal(CcpCheckStatus.Skipped, c.Status));
        Assert.Equal(CcpCheckStatus.Warn, report.Checks[^1].Status);
        Assert.Contains("timeout", report.Checks[^1].Detail);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public void StockBusGateAndTimeoutPoisoningArePreserved()
    {
        var (profile, program, cal) = Fixture();
        using var ecu = new FakeCcpSlave(new(profile, program, cal), bus: 1);
        var client = new CcpSimulationClient(ecu);
        Assert.Throws<TimeoutException>(() => client.Connect());
        Assert.Throws<InvalidOperationException>(() => client.Connect());
        ecu.Load(profile.EnableByteAddress, [1]);
        new CcpSimulationClient(ecu).Connect();
        ecu.Reboot();
        Assert.Equal(2, ecu.Peek(profile.EnableByteAddress));
        Assert.Throws<TimeoutException>(() => new CcpSimulationClient(ecu).Connect());
    }

    [Fact]
    public void FlashIsImmutableAndMissingRegionsAreRejected()
    {
        var (profile, program, cal) = Fixture();
        var image = new T6FirmwareImage(profile, program, cal);
        program[0] = 42; cal[0] = 42;
        Assert.Equal(0, image.ReadByte(0x40000));
        Assert.NotEqual(42, image.ReadByte(0x20000));
        Assert.Throws<InvalidDataException>(() => image.ReadByte(0));
        Assert.Throws<InvalidDataException>(() => image.WriteRam(0x40000, [1]));
        Assert.Throws<InvalidDataException>(() => image.WriteRam(0x4000FFFF, [1, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Read(uint.MaxValue, 2));
        Assert.Equal(new byte[] { 0, 0 }, image.Read(0x4000FFFE, 2));
    }

    [Fact]
    public void LoaderRejectsWrongProgramCalibrationAndRegistryBoundary()
    {
        var (profile, program, cal) = Fixture();
        Assert.Throws<InvalidDataException>(() => new T6FirmwareImage(profile with { ProgramSha256 = new string('0', 64) }, program, cal));
        Assert.Throws<InvalidDataException>(() => new T6FirmwareImage(profile with { CalibrationId = "WRONG" }, program, cal));
        Assert.Throws<InvalidDataException>(() => new T6FirmwareImage(profile, program, cal[..63]));
        Assert.Throws<InvalidDataException>(() => new T6FirmwareImage(profile with { SymbolCount = 2 }, program, cal));
        Assert.Throws<InvalidDataException>(() => new T6FirmwareImage(profile with { CcpIdAddress = 0x40000000 }, program, cal));
    }

    [Fact]
    public void DeferredChecksumRetainsInterleavedDaqFrames()
    {
        var (profile, program, cal) = Fixture();
        using var ecu = new FakeCcpSlave(new T6FirmwareImage(profile, program, cal));
        var client = new CcpSimulationClient(ecu); client.Connect();
        client.StartDaq(0x40000010, 2);
        var expected = CcpFrame.AdditiveChecksum(ecu.Firmware!.Read(0x40000000, 9000));
        Assert.Equal(expected, client.Checksum(0x40000000, 9000));
        var frames = client.TakeDaq();
        Assert.Equal(3, frames.Count);
        Assert.All(frames, frame => Assert.Equal(new byte[] { 0xAB, 0xCD }, frame.Data.Span[..2].ToArray()));
    }

    [Fact]
    public void CrpAndJsonProfileRoundTrip()
    {
        var (profile, program, cal) = Fixture();
        string dir = Path.Combine(Path.GetTempPath(), "ccp-fixture-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string progPath = Path.Combine(dir, "prog.bin"), calPath = Path.Combine(dir, "cal.bin");
            string crpPath = Path.Combine(dir, "test.crp"), profilePath = Path.Combine(dir, "profile.json");
            File.WriteAllBytes(progPath, program); File.WriteAllBytes(calPath, cal);
            File.WriteAllText(profilePath, JsonSerializer.Serialize(profile));
            File.WriteAllBytes(crpPath, CrpCreator.Build(CrpCreator.EcuType.T6, calPath, progPath));
            var image = T6FirmwareImage.FromCrp(crpPath, T6FirmwareProfile.FromJson(profilePath));
            Assert.Equal(profile, image.Profile);
            Assert.Equal(cal, image.Read(profile.CalibrationRamAddress, cal.Length));
            Assert.Throws<InvalidDataException>(() => T6FirmwareImage.FromCrp(crpPath));
            var corrupt = File.ReadAllBytes(crpPath); corrupt[100] ^= 1; File.WriteAllBytes(crpPath, corrupt);
            Assert.Throws<InvalidDataException>(() => T6FirmwareImage.FromCrp(crpPath, profile));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
