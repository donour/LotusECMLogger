using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class T6SymbolRegistryTests
{
    private static byte[] Table(int count = 753)
    {
        var bytes = new byte[T6SymbolRegistry.HeaderSize + count * 6];
        Encoding.ASCII.GetBytes("T6-V00BU 18/12/2014 Lotus Eng").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("V0001").CopyTo(bytes, 32);
        int[] sizes = [1, 2, 4, 5, 6, 8, 10, 32];
        for (int i = 0; i < count; i++)
        {
            int offset = 44 + i * 6;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), 0x40000000 + (uint)i * 32);
            bytes[offset + 4] = (byte)sizes[i % sizes.Length];
        }
        return bytes;
    }

    [Fact]
    public void Parses753PackedSixByteRecordsAndPreservesWholeObjectSizes()
    {
        byte[] bytes = Table();
        Assert.Equal(4562, bytes.Length);
        var registry = T6SymbolRegistry.ParseTable(bytes, 753);
        Assert.Equal("T6-V00BU 18/12/2014 Lotus Eng", registry.BuildId);
        Assert.Equal("V0001", registry.Version);
        Assert.Equal(753, registry.Entries.Count);
        Assert.Equal(new T6SymbolEntry(0, 0x40000000, 1), registry.Entries[0]);
        Assert.Equal(new T6SymbolEntry(1, 0x40000020, 2), registry.Entries[1]);
        Assert.Equal(new T6SymbolEntry(7, 0x400000E0, 32), registry.Entries[7]);
        Assert.Equal((uint)(0x40000000 + 752 * 32), registry.Entries[^1].Address);
        bytes[48] = 99;
        Assert.Equal((byte)1, registry.Entries[0].Size);
    }

    [Fact]
    public void AcceptsFlashVersionStringAndDuplicateAddressAliases()
    {
        var bytes = Table(3);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), 0x000C02B8);
        bytes[48] = 32;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(50), 0x40000040);
        var registry = T6SymbolRegistry.ParseTable(bytes, 3);
        Assert.False(registry.Entries[0].IsRam);
        Assert.Equal(2, registry.FindByAddress(0x40000040).Count());
        Assert.Empty(registry.FindByAddress(0x40000001));
    }

    [Fact]
    public void RawQueryReturnsEntriesAddressRatherThanHeaderAddress()
    {
        uint entries = T6SymbolRegistry.ParseEntriesAddress([0, 0x0C, 0x02, 0xE4]);
        Assert.Equal((uint)0xC02E4, entries);
        Assert.Equal((uint)0xC02B8, entries - T6SymbolRegistry.HeaderSize);
        Assert.Equal((uint)0xCE984, T6SymbolRegistry.ParseEntriesAddress([0, 0x0C, 0xE9, 0x84]));
    }

    [Fact]
    public void VerifiedProfilePointersAgreeWithRawMemoryDispatchers()
    {
        Assert.Equal((uint)0xC02E4, CcpBuildProfile.Known.Single(p => p.Build == "B13200091").SymbolEntriesAddress);
        uint entries = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278").SymbolEntriesAddress!.Value;
        Assert.Equal((uint)0xCE958, entries - T6SymbolRegistry.HeaderSize);
        Assert.Equal((uint)0xD2EF4, CcpBuildProfile.Known.Single(p => p.Build == "E132E0288").SymbolEntriesAddress);
        Assert.Null(CcpBuildProfile.Known.Single(p => p.Build == "C132F0395").SymbolEntriesAddress);
    }

    [Theory]
    [InlineData(0)] [InlineData(0x100000)] [InlineData(0x40000000)] [InlineData(0xFFFFFFFF)]
    public void RejectsInvalidDiscoveryPointers(uint address)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, address);
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseEntriesAddress(bytes));
    }

    [Fact]
    public void ParsesTerminatedListAndIgnoresFollowingBatchData()
    {
        var bytes = Table(3)[44..].Concat(new byte[6]).Concat(new byte[] { 0xFF, 0xFF }).ToArray();
        var entries = T6SymbolRegistry.ParseTerminatedEntries(bytes);
        Assert.Equal(3, entries.Count);
        Assert.Equal(new T6SymbolEntry(2, 0x40000040, 4), entries[2]);
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTerminatedEntries(Table(3)[44..]));
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTerminatedEntries(bytes[..^9]));
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTerminatedEntries(new byte[6]));
    }

    [Theory]
    [InlineData(0x4000FFFF, 2)] [InlineData(0xFFFFF, 2)] [InlineData(0xFFFFFFFE, 4)]
    [InlineData(0x3F8000, 1)] [InlineData(0xFFF00000, 1)]
    public void RejectsRangesCrossingMemoryBoundariesAndOtherEcuAddresses(uint address, byte size)
    {
        var bytes = Table(1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), address);
        bytes[48] = size;
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
    }

    [Fact]
    public void AcceptsLastByteOfRamAndFlash()
    {
        var bytes = Table(2);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(44), 0x4000FFFF); bytes[48] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(50), 0xFFFFF); bytes[54] = 1;
        Assert.Equal(2, T6SymbolRegistry.ParseTable(bytes, 2).Entries.Count);
    }

    [Fact]
    public void RejectsUnknownReservedByteAndZeroSize()
    {
        var bytes = Table(1); bytes[49] = 1;
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
        bytes[49] = 0; bytes[48] = 0;
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
    }

    [Fact]
    public void BoundsCountAndRequiresExactPackedLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => T6SymbolRegistry.ParseTable(Table(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => T6SymbolRegistry.ParseTable(Table(1), 1025));
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(Table(1)[..^1], 1));
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(Table(1).Concat(new byte[2]).ToArray(), 1));
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTerminatedEntries(
            Table(1025)[44..].Concat(new byte[6]).ToArray()));
    }

    [Fact]
    public void RejectsCorruptHeaderInsteadOfAcceptingWrongFlashData()
    {
        var bytes = Table(1); bytes[0] = 0xFF;
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
        bytes = Table(1); bytes[31] = 1;
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
        bytes = Table(1); Array.Clear(bytes, 32, 12);
        Assert.Throws<InvalidDataException>(() => T6SymbolRegistry.ParseTable(bytes, 1));
    }
}
