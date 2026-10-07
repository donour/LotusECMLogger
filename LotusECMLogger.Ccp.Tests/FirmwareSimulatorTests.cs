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
