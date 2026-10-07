using System.Buffers.Binary;
using System.Text;

namespace LotusECMLogger.Services.Ccp;

/// <summary>A nameless firmware export. Size is the whole exported byte count, not a C type or DAQ width.</summary>
public readonly record struct T6SymbolEntry(int Index, uint Address, byte Size)
{
    public bool IsRam => Address >= 0x40000000 && Address < 0x40010000;
}

/// <summary>
/// Decodes the T6 address/size registry used by freeze frames and the raw-memory interface.
/// This is firmware metadata, not a CCP command. The raw 0x53 query returns the entry-array
/// address (header + 0x2C); an enabled CCP session can read the same flash bytes via UPLOAD.
/// No names, signedness, scaling, write permissions or cross-build index stability are inferred.
/// </summary>
public sealed class T6SymbolRegistry
{
    public const int HeaderSize = 0x2C;
    public const int EntrySize = 6;
    public const int MaximumEntries = 1024; // bound discovery; freeze-frame indexes use ten bits
    public const uint DiscoveryRequestCanId = 0x53;
    public const uint DiscoveryResponseCanId = 0x7A0;

    private T6SymbolRegistry(string buildId, string version, IReadOnlyList<T6SymbolEntry> entries)
        => (BuildId, Version, Entries) = (buildId, version, entries);

    public string BuildId { get; }
    public string Version { get; }
    public IReadOnlyList<T6SymbolEntry> Entries { get; }
    public IEnumerable<T6SymbolEntry> FindByAddress(uint address) => Entries.Where(e => e.Address == address);

    /// <summary>Parses the owner's header + counted entries layout, excluding an optional list terminator.</summary>
    public static T6SymbolRegistry ParseTable(ReadOnlySpan<byte> bytes, int entryCount)
    {
        if (entryCount is < 1 or > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(entryCount));
        if (bytes.Length != HeaderSize + entryCount * EntrySize)
            throw new InvalidDataException("The T6 registry length does not match the supplied entry count.");
        string buildId = ReadText(bytes[..32]);
        string version = ReadText(bytes.Slice(32, 12));
        var entries = new T6SymbolEntry[entryCount];
        for (int i = 0; i < entryCount; i++) entries[i] = ParseEntry(bytes.Slice(HeaderSize + i * EntrySize, EntrySize), i);
        return new(buildId, version, Array.AsReadOnly(entries));
    }

    /// <summary>
    /// Parses an entry-array download with a six-zero-byte terminator, as lib/ltacc.py does.
    /// Trailing bytes from a batched read are ignored after the terminator. A missing terminator,
    /// truncated entry or more than MaximumEntries entries is rejected rather than reading indefinitely.
    /// </summary>
    public static IReadOnlyList<T6SymbolEntry> ParseTerminatedEntries(ReadOnlySpan<byte> bytes)
    {
        var entries = new List<T6SymbolEntry>();
        for (int offset = 0; offset + EntrySize <= bytes.Length; offset += EntrySize)
        {
            var record = bytes.Slice(offset, EntrySize);
            if (record.IndexOfAnyExcept((byte)0) < 0)
            {
                if (entries.Count == 0) throw new InvalidDataException("The T6 registry is empty.");
                return entries.AsReadOnly();
            }
            if (entries.Count == MaximumEntries) throw new InvalidDataException("T6 registry exceeds the discovery limit.");
            entries.Add(ParseEntry(record, entries.Count));
        }
        throw new InvalidDataException("T6 registry is truncated or lacks its zero terminator.");
    }

    /// <summary>Decodes the four-byte raw-memory discovery reply and checks its T6 flash range.</summary>
    public static uint ParseEntriesAddress(ReadOnlySpan<byte> reply)
    {
        if (reply.Length != 4) throw new InvalidDataException("The registry pointer reply must contain four bytes.");
        uint address = BinaryPrimitives.ReadUInt32BigEndian(reply);
        if (address < HeaderSize || address > 0x100000 - EntrySize)
            throw new InvalidDataException("The registry entry-array pointer is outside T6 flash.");
        return address;
    }

    private static T6SymbolEntry ParseEntry(ReadOnlySpan<byte> record, int index)
    {
        uint address = BinaryPrimitives.ReadUInt32BigEndian(record);
        byte size = record[4];
        if (record[5] != 0) throw new InvalidDataException($"T6 registry entry {index} has a nonzero reserved byte.");
        if (size == 0) throw new InvalidDataException($"T6 registry entry {index} has zero size.");
        ulong end = (ulong)address + size;
        bool flash = address < 0x100000 && end <= 0x100000;
        bool ram = address >= 0x40000000 && end <= 0x40010000;
        if (!flash && !ram) throw new InvalidDataException($"T6 registry entry {index} is outside T6 flash/RAM.");
        return new(index, address, size);
    }

    private static string ReadText(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end < 0) end = bytes.Length;
        foreach (byte value in bytes[..end])
            if (value is < 0x20 or > 0x7E) throw new InvalidDataException("T6 registry header is not printable ASCII.");
        foreach (byte value in bytes[end..])
            if (value is not (0 or 0x20)) throw new InvalidDataException("T6 registry header has invalid string padding.");
        string text = Encoding.ASCII.GetString(bytes[..end]).TrimEnd();
        if (text.Length == 0) throw new InvalidDataException("T6 registry header contains an empty identifier.");
        return text;
    }
}
