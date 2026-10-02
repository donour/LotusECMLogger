using System.Security.Cryptography;
using System.Text.Json;

namespace LotusECMLogger.Services;

/// <summary>A strict, address-preserving Intel HEX image ready for the ABS bootloader.</summary>
public sealed record AbsFirmwareImage
{
    public required string SourcePath { get; init; }
    public required IReadOnlyList<byte> Bytes { get; init; }
    public required uint StartAddress { get; init; }
    public required uint EndAddressExclusive { get; init; }
    public required string Sha256 { get; init; }
    public AbsFirmwareManifest Manifest { get; init; } = new();

    public int BlockCount => (Bytes.Count + AbsFirmwareFlasher.BlockSize - 1) / AbsFirmwareFlasher.BlockSize;

    public IEnumerable<ReadOnlyMemory<byte>> Blocks()
    {
        byte[] snapshot = Bytes.ToArray();
        for (int offset = 0; offset < snapshot.Length; offset += AbsFirmwareFlasher.BlockSize)
            yield return new ReadOnlyMemory<byte>(snapshot, offset,
                Math.Min(AbsFirmwareFlasher.BlockSize, snapshot.Length - offset));
    }

    public static AbsFirmwareImage Load(string path, string? manifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("ABS firmware file was not found.", fullPath);
        var parser = new IntelHexParser();
        var parsed = parser.Parse(File.ReadLines(fullPath));
        if (parsed.Bytes.Count == 0) throw new FormatException("The Intel HEX file contains no data records.");

        string hash = Convert.ToHexString(SHA256.HashData(parsed.Bytes.ToArray())).ToLowerInvariant();
        string sidecar = manifestPath ?? Path.Combine(Path.GetDirectoryName(fullPath) ?? "",
            Path.GetFileNameWithoutExtension(fullPath) + ".manifest.json");
        AbsFirmwareManifest manifest = File.Exists(sidecar)
            ? AbsFirmwareManifest.Load(sidecar)
            : AbsFirmwareManifest.CreateForImage(Path.GetFileName(fullPath), hash, parsed.StartAddress, parsed.EndAddressExclusive);

        manifest.ValidateAgainst(hash, parsed.StartAddress, parsed.EndAddressExclusive);
        return new AbsFirmwareImage
        {
            SourcePath = fullPath,
            Bytes = parsed.Bytes,
            StartAddress = parsed.StartAddress,
            EndAddressExclusive = parsed.EndAddressExclusive,
            Sha256 = hash,
            Manifest = manifest,
        };
    }
}

/// <summary>Metadata used to prevent sending an image to the wrong ABS module.</summary>
public sealed record AbsFirmwareManifest
{
    public string Format { get; init; } = "ABS-IntelHex-v1";
    public string Module { get; init; } = "ABS/ESP";
    public string Variant { get; init; } = "";
    public string VinPattern { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public uint AddressStart { get; init; }
    public uint AddressEndExclusive { get; init; }
    /// <summary>Optional exact records captured from the current module, keyed by diagnostic id (for example 85 or 86).</summary>
    public IReadOnlyDictionary<string, string> RequiredIdentifications { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool IntegrityVerified { get; init; }
    public string IntegrityNote { get; init; } = AbsFirmwareFlasher.IntegrityWarning;

    public static AbsFirmwareManifest CreateForImage(string fileName, string hash, uint start, uint end) => new()
    {
        Sha256 = hash,
        AddressStart = start,
        AddressEndExclusive = end,
        IntegrityNote = $"Generated from {fileName}; bootloader trailer/recovery acceptance remains unresolved.",
    };

    public static AbsFirmwareManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        var value = JsonSerializer.Deserialize<AbsFirmwareManifest>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return value ?? throw new FormatException("The ABS firmware manifest is empty.");
    }

    public void ValidateAgainst(string hash, uint start, uint end)
    {
        if (!string.Equals(Format, "ABS-IntelHex-v1", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Unsupported ABS firmware manifest format '{Format}'.");
        if (!string.IsNullOrWhiteSpace(Sha256) && !string.Equals(Sha256, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Firmware SHA-256 {hash} does not match the manifest ({Sha256}).");
        if (AddressStart != 0 && AddressStart != start)
            throw new InvalidDataException($"Firmware starts at 0x{start:X8}, manifest requires 0x{AddressStart:X8}.");
        if (AddressEndExclusive != 0 && AddressEndExclusive != end)
            throw new InvalidDataException($"Firmware ends at 0x{end:X8}, manifest requires 0x{AddressEndExclusive:X8}.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

internal sealed class IntelHexParser
{
    internal sealed record Parsed(IReadOnlyList<byte> Bytes, uint StartAddress, uint EndAddressExclusive);

    public Parsed Parse(IEnumerable<string> lines)
    {
        var data = new SortedDictionary<uint, byte>();
        uint linearBase = 0;
        uint segmentBase = 0;
        bool eof = false;
        int lineNumber = 0;
        foreach (string raw in lines)
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (eof) throw new FormatException($"Intel HEX data appears after EOF at line {lineNumber}.");
            if (line[0] != ':' || line.Length < 11 || (line.Length & 1) == 0)
                throw new FormatException($"Malformed Intel HEX record at line {lineNumber}.");
            byte[] record;
            try { record = Convert.FromHexString(line[1..]); }
            catch (FormatException error) { throw new FormatException($"Invalid hexadecimal data at line {lineNumber}.", error); }
            int count = record[0];
            if (record.Length != count + 5)
                throw new FormatException($"Intel HEX length does not match byte count at line {lineNumber}.");
            byte checksum = 0;
            foreach (byte value in record) checksum = unchecked((byte)(checksum + value));
            if (checksum != 0) throw new FormatException($"Intel HEX checksum failed at line {lineNumber}.");
            ushort offset = (ushort)((record[1] << 8) | record[2]);
            byte type = record[3];
            ReadOnlySpan<byte> payload = record.AsSpan(4, count);
            switch (type)
            {
                case 0x00:
                    ulong address = (ulong)(linearBase + segmentBase) + offset;
                    if (address > uint.MaxValue || address + (uint)count > uint.MaxValue + 1UL)
                        throw new FormatException($"Intel HEX address overflows 32 bits at line {lineNumber}.");
                    for (int i = 0; i < payload.Length; i++)
                    {
                        uint current = checked((uint)(address + (uint)i));
                        if (!data.TryAdd(current, payload[i]))
                            throw new FormatException($"Overlapping Intel HEX data at 0x{current:X8} (line {lineNumber}).");
                    }
                    break;
                case 0x01:
                    if (count != 0 || offset != 0) throw new FormatException($"Malformed EOF record at line {lineNumber}.");
                    eof = true;
                    break;
                case 0x02:
                    if (count != 2 || offset != 0) throw new FormatException($"Malformed segment address record at line {lineNumber}.");
                    segmentBase = (uint)((payload[0] << 8 | payload[1]) << 4);
                    linearBase = 0;
                    break;
                case 0x04:
                    if (count != 2 || offset != 0) throw new FormatException($"Malformed linear address record at line {lineNumber}.");
                    linearBase = (uint)(payload[0] << 24 | payload[1] << 16);
                    segmentBase = 0;
                    break;
                default:
                    throw new FormatException($"Unsupported Intel HEX record type 0x{type:X2} at line {lineNumber}.");
            }
        }
        if (!eof) throw new FormatException("Intel HEX file has no EOF record.");
        if (data.Count == 0) return new Parsed([], 0, 0);
        uint start = data.First().Key;
        uint end = checked(data.Last().Key + 1);
        if ((ulong)end - start != (ulong)data.Count)
            throw new FormatException($"Intel HEX image has an address gap between 0x{start:X8} and 0x{end:X8}; gaps are never filled.");
        var bytes = new byte[data.Count];
        foreach ((uint address, byte value) in data) bytes[checked((int)(address - start))] = value;
        return new Parsed(bytes, start, end);
    }
}
