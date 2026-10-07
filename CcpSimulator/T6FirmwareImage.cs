using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LotusECMLogger.Services.Ccp.Simulation;

/// <summary>Build-specific boot copy locations. Addresses and semantic bindings must be verified per program.</summary>
public sealed record T6FirmwareProfile(string Name, string CalibrationId, string ProgramSha256,
    uint CalibrationRamAddress, int CalibrationSize, uint EnableByteAddress,
    uint InitializedDataFlashAddress, int InitializedDataSize, uint CcpIdAddress,
    uint SymbolEntriesAddress, int SymbolCount)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public CcpBuildProfile BuildProfile => new(CalibrationId, CcpTarget.T6, CalibrationRamAddress,
        checked((uint)CalibrationSize), EnableByteAddress, SymbolEntriesAddress: SymbolEntriesAddress);

    public static T6FirmwareProfile Gt430 { get; } = new("Evora GT430 MY19 ROW manual", "C132E0278",
        "b11a1812dc742ace61eb190e41d6435deef8b908158e5090d14ee1a4f4c52aea",
        0x40008654, 0x69AC, 0x400088EC, 0xD38C8, 0x1648, 0x40001588, 0xCE984, 753);

    public static T6FirmwareProfile FromJson(string path) =>
        JsonSerializer.Deserialize<T6FirmwareProfile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Missing firmware profile.");
}

/// <summary>
/// Immutable PROG/CAL flash plus resettable 64 KiB RAM. Models boot data copies, not PowerPC execution.
/// Unknown flash (e.g. bootloader omitted from CRP) raises an error instead of inventing bytes.
/// </summary>
public sealed class T6FirmwareImage
{
    public const uint RamBase = 0x40000000;
    private readonly byte[] _ram = new byte[0x10000];
    private readonly byte[] _program, _calibration;
    public T6FirmwareProfile Profile { get; }
    public string ProgramSha256 { get; }
    public string CalibrationSha256 { get; }
    public T6SymbolRegistry Registry { get; }

    public static T6FirmwareImage FromCrp(string path, T6FirmwareProfile? profile = null)
    {
        var chunks = CrpUnpacker.Unpack(path).Chunks;
        var program = chunks.SingleOrDefault(c => c.RealAddress == 0x40000);
        var calibration = chunks.SingleOrDefault(c => c.RealAddress == 0x20000);
        if (program is null || calibration is null || chunks.Any(c => c.EcuId != "ECU T6"))
            throw new InvalidDataException("Expected one T6 PROG and one CAL chunk.");
        return new(profile ?? T6FirmwareProfile.Gt430, program.Data, calibration.Data);
    }

    public T6FirmwareImage(T6FirmwareProfile profile, ReadOnlySpan<byte> program, ReadOnlySpan<byte> calibration)
    {
        Profile = profile;
        _program = program.ToArray(); _calibration = calibration.ToArray();
        ProgramSha256 = Convert.ToHexStringLower(SHA256.HashData(_program));
        CalibrationSha256 = Convert.ToHexStringLower(SHA256.HashData(_calibration));
        if (profile.ProgramSha256 is null || profile.ProgramSha256.Length != 64 ||
            !ProgramSha256.Equals(profile.ProgramSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Program hash does not match the selected firmware profile.");
        if (_program.Length > 0xC0000 || _calibration.Length != profile.CalibrationSize || _calibration.Length > 0x10000)
            throw new InvalidDataException("Unexpected PROG/CAL extent.");
        if (profile.SymbolCount is < 1 or > 1024 || profile.InitializedDataSize < 1)
            throw new InvalidDataException("Invalid registry or initialized-data size.");
        var calId = Encoding.ASCII.GetString(_calibration.AsSpan(0, Math.Min(32, _calibration.Length)));
        if (!calId.StartsWith(profile.CalibrationId + " ", StringComparison.Ordinal))
            throw new InvalidDataException("Calibration ID does not match the selected profile.");
        Registry = T6SymbolRegistry.ParseTable(Read(profile.SymbolEntriesAddress - 44,
            44 + profile.SymbolCount * 6), profile.SymbolCount);
        if (Read(profile.SymbolEntriesAddress + (uint)(profile.SymbolCount * 6), 6).Any(b => b != 0))
            throw new InvalidDataException("Missing registry terminator at the profiled boundary.");
        ResetRam();
        if (Encoding.ASCII.GetString(Read(profile.CcpIdAddress, 7)) != "Ccp_T6\0")
            throw new InvalidDataException("Profile's initialized CCP ID location is incorrect.");
        Read(profile.EnableByteAddress, 1);
    }

    public byte ReadByte(uint address)
    {
        if (address >= RamBase && address < RamBase + _ram.Length) return _ram[address - RamBase];
        if (address >= 0x40000 && (ulong)address < 0x40000UL + (uint)_program.Length) return _program[address - 0x40000];
        if (address >= 0x20000 && (ulong)address < 0x20000UL + (uint)_calibration.Length) return _calibration[address - 0x20000];
        throw new InvalidDataException($"Address 0x{address:X8} is absent from the firmware image.");
    }
    public byte[] Read(uint address, int length)
    {
        if (length < 0 || (ulong)address + (uint)length > 0x100000000UL) throw new ArgumentOutOfRangeException(nameof(length));
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = ReadByte(address + (uint)i);
        return bytes;
    }
    public void WriteRam(uint address, ReadOnlySpan<byte> bytes)
    {
        if (address < RamBase || (ulong)address + (uint)bytes.Length > (ulong)RamBase + (uint)_ram.Length)
            throw new InvalidDataException("Simulator writes must fit entirely in RAM; flash is immutable.");
        bytes.CopyTo(_ram.AsSpan((int)(address - RamBase)));
    }
    public void ResetRam()
    {
        Array.Clear(_ram);
        WriteRam(RamBase, Read(Profile.InitializedDataFlashAddress, Profile.InitializedDataSize));
        WriteRam(Profile.CalibrationRamAddress, _calibration);
        // Verified GT430 stack fill; not needed by the CCP model. Leave other profiles' stack unspecified (zero).
        if (Profile == T6FirmwareProfile.Gt430) _ram.AsSpan(0xF000, 0xE00).Fill(0x55);
    }
}
