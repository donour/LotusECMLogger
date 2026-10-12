using System.Buffers.Binary;
using System.Text;

namespace LotusECMLogger.Services.Ccp;

/// <summary>An address range that does not wrap the 32-bit address space.</summary>
public readonly record struct CcpMemoryRange(uint Start, uint Length)
{
    public bool Contains(uint address, int length) => length > 0 && address >= Start &&
        (ulong)address + (uint)length <= (ulong)Start + Length;

    public static CcpMemoryRange Calibration(CcpBuildProfile build) => new(build.CalibrationBase, build.CalibrationSize);
}

/// <summary>
/// A write stopped part-way. The first <see cref="BytesAcknowledged"/> bytes were acknowledged; the
/// unacknowledged transfer may or may not have been applied, so everything after them is unknown.
/// </summary>
public sealed class CcpWriteException(uint address, int bytesAcknowledged, Exception inner)
    : IOException($"Write at 0x{address:X8} was interrupted after {bytesAcknowledged} acknowledged byte(s); " +
        "the rest of the region's contents are unknown.", inner)
{
    public uint Address { get; } = address;
    public int BytesAcknowledged { get; } = bytesAcknowledged;
}

/// <summary>Every byte was acknowledged, but the read-back differs (e.g. the address is not RAM).</summary>
public sealed class CcpWriteVerificationException(uint address, int mismatchedBytes)
    : IOException($"Read-back after writing differs at 0x{address:X8} ({mismatchedBytes} byte(s) mismatched).")
{
    public uint FirstMismatchAddress { get; } = address;
    public int MismatchedBytes { get; } = mismatchedBytes;
}

/// <summary>What the slave says it is, and which built-in build profile (if any) it matches.</summary>
public sealed record CcpBuildIdentification(CcpSlaveIdentity Slave, CcpBuildProfile? Build,
    string? CalibrationId, string? CalibrationDescription, string? RegistryBuildId, string? RegistryVersion)
{
    /// <summary>The registry header at the profile's entry pointer read as printable text.</summary>
    public bool RegistryHeaderReadable => RegistryBuildId is not null;
}

public sealed partial class CcpMaster
{
    // Well under the 64 KiB the firmware silently truncates to, and keeps each block's timeout bounded.
    private const int ChecksumBlockSize = 0x8000;
    private const int CalibrationIdLength = 32;
    private const int RegistryChunkEntries = 64;

    // ---- Reads ---------------------------------------------------------------------------------

    /// <summary>SET_MTA then five-byte UPLOADs. Progress reports bytes read.</summary>
    public Task<byte[]> ReadAsync(uint address, int length, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(address, length);
        return RunConnectedAsync("read memory", ct => ReadCoreAsync(address, length, progress, ct), cancellationToken);
    }

    /// <summary>SHORT_UP of 1–5 bytes. Uses MTA1, so MTA0 is left where it was.</summary>
    public Task<byte[]> ShortUploadAsync(uint address, int size, CancellationToken cancellationToken = default)
    {
        if (size is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(size));
        ValidateRange(address, size);
        return RunConnectedAsync("read memory", async ct =>
        {
            var reply = await ExchangeAsync(c => CcpFrame.ShortUpload(c, address, size), ct).ConfigureAwait(false);
            return reply.Data.Span[..size].ToArray();
        }, cancellationToken);
    }

    private async Task<byte[]> ReadCoreAsync(uint address, int length, IProgress<int>? progress, CancellationToken ct)
    {
        await ExchangeAsync(c => CcpFrame.SetMta(c, address), ct).ConfigureAwait(false);
        return await UploadSequentialAsync(length, progress, ct).ConfigureAwait(false);
    }

    // ---- Writes --------------------------------------------------------------------------------

    /// <summary>
    /// DNLOAD_6/DNLOAD the bytes, then read them back. Refuses any byte outside <paramref name="allowed"/>.
    /// This is the mechanism only: engine-off confirmation, backups, build checks and enable-byte
    /// preservation belong to the calibration-write layer above it. The DNLOAD reply's MTA field is ignored
    /// (this firmware leaves it stale). An interrupted write throws <see cref="CcpWriteException"/> and is never
    /// retried, because DNLOAD post-increments MTA0.
    /// </summary>
    public Task WriteVerifiedAsync(uint address, ReadOnlyMemory<byte> data, CcpMemoryRange allowed,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, data.Length);
        if (!allowed.Contains(address, data.Length))
            throw new ArgumentOutOfRangeException(nameof(address),
                $"0x{address:X8}+{data.Length} is outside the allowed range 0x{allowed.Start:X8}+{allowed.Length}.");
        var bytes = data.ToArray();
        return RunConnectedAsync("write memory", async ct =>
        {
            await ExchangeAsync(c => CcpFrame.SetMta(c, address), ct).ConfigureAwait(false);
            int sent = 0;
            try
            {
                while (sent < bytes.Length)
                {
                    int count = Math.Min(6, bytes.Length - sent);
                    var chunk = bytes.AsMemory(sent, count);
                    await ExchangeAsync(c => count == 6 ? CcpFrame.Download6(c, chunk.Span) : CcpFrame.Download(c, chunk.Span),
                        ct).ConfigureAwait(false);
                    sent += count;
                    progress?.Report(sent);
                }
            }
            catch (Exception ex)
            {
                throw new CcpWriteException(address, sent, ex);
            }
            var readBack = await ReadCoreAsync(address, bytes.Length, null, ct).ConfigureAwait(false);
            int first = -1, mismatched = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (readBack[i] == bytes[i]) continue;
                if (first < 0) first = i;
                mismatched++;
            }
            if (mismatched > 0) throw new CcpWriteVerificationException(address + (uint)first, mismatched);
            return true;
        }, cancellationToken);
    }

    // ---- Checksum ------------------------------------------------------------------------------

    /// <summary>
    /// 16-bit additive checksum of the region, computed by the slave in blocks under 64 KiB and summed.
    /// Compare with <see cref="CcpFrame.AdditiveChecksum"/> over the expected bytes. Each block holds the
    /// command gate until its late reply arrives: the slave's checksum worker shares the CRM buffer and MTA1.
    /// </summary>
    public Task<ushort> ChecksumAsync(uint address, int length, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(address, length);
        return RunConnectedAsync("checksum memory", async ct =>
        {
            uint sum = 0;
            for (int offset = 0; offset < length; offset += ChecksumBlockSize)
            {
                int size = Math.Min(ChecksumBlockSize, length - offset);
                await ExchangeAsync(c => CcpFrame.SetMta(c, address + (uint)offset), ct).ConfigureAwait(false);
                var timeout = _options.ChecksumBaseTimeout + _options.ChecksumPassTimeout * ((size + 255) / 256);
                var data = (await ExchangeAsync(c => CcpFrame.BuildChecksum(c, size), ct, timeout).ConfigureAwait(false)).Data.Span;
                if (data[0] != 2) throw new CcpProtocolException($"BUILD_CHKSUM returned a {data[0]}-byte checksum; expected 2.");
                sum += BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
                progress?.Report(offset + size);
            }
            return (ushort)(sum & 0xFFFF);
        }, cancellationToken);
    }

    // ---- Identification ------------------------------------------------------------------------

    /// <summary>
    /// EXCHANGE_ID and the ID string give the target family. The calibration ID at each candidate CAL_base
    /// (copied 1:1 from flash 0x20000) then selects a build profile; CAL_prog_version and the registry header
    /// are read for display and as a cross-check. Build is null when nothing matches: the user picks the build.
    /// </summary>
    public Task<CcpBuildIdentification> IdentifyAsync(IEnumerable<CcpBuildProfile>? candidates = null,
        CancellationToken cancellationToken = default)
    {
        var profiles = (candidates ?? CcpBuildProfile.Known).ToList();
        return RunConnectedAsync("identify the slave", async ct =>
        {
            var slave = await ExchangeIdCoreAsync(ct).ConfigureAwait(false);
            if (slave.SlaveId != Target.SlaveId) return new CcpBuildIdentification(slave, null, null, null, null, null);

            CcpBuildProfile? build = null;
            string? calibrationId = null;
            foreach (var calibrationBase in profiles.Where(p => p.Target == Target).Select(p => p.CalibrationBase).Distinct())
            {
                var text = ReadText(await ReadCoreAsync(calibrationBase, CalibrationIdLength, null, ct).ConfigureAwait(false));
                string? id = text?.Split(' ')[0];
                var match = profiles.FirstOrDefault(p => p.Target == Target && p.CalibrationBase == calibrationBase && p.Build == id);
                if (match is null) continue;
                if (build is not null) return new CcpBuildIdentification(slave, null, null, null, null, null); // ambiguous
                (build, calibrationId) = (match, id);
            }
            if (build is null) return new CcpBuildIdentification(slave, null, null, null, null, null);

            string? description = null, registryBuild = null, registryVersion = null;
            if (build.ProgramVersionOffset is uint versionOffset)
                description = ReadText(await ReadCoreAsync(build.CalibrationBase + versionOffset, 32, null, ct).ConfigureAwait(false));
            if (build.SymbolEntriesAddress is uint entries)
            {
                var header = await ReadCoreAsync(entries - T6SymbolRegistry.HeaderSize, T6SymbolRegistry.HeaderSize, null, ct)
                    .ConfigureAwait(false);
                registryBuild = ReadText(header.AsSpan(0, 32).ToArray());
                registryVersion = registryBuild is null ? null : ReadText(header.AsSpan(32, 12).ToArray());
            }
            return new CcpBuildIdentification(slave, build, calibrationId, description, registryBuild, registryVersion);
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the T6 runtime variable registry over CCP (no RMA unlock): the 44-byte header, then six-byte
    /// records until the six-zero-byte terminator. The entry count is discovered, never assumed.
    /// </summary>
    public Task<T6SymbolRegistry> ReadSymbolRegistryAsync(CcpBuildProfile build, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.SymbolEntriesAddress is not uint entries)
            throw new ArgumentException($"No verified registry location for {build.Build}.", nameof(build));
        return RunConnectedAsync("read the variable registry", async ct =>
        {
            await ExchangeAsync(c => CcpFrame.SetMta(c, entries - T6SymbolRegistry.HeaderSize), ct).ConfigureAwait(false);
            var bytes = new List<byte>(await UploadSequentialAsync(T6SymbolRegistry.HeaderSize, null, ct).ConfigureAwait(false));
            int count = 0;
            while (true)
            {
                // MTA0 continues where the previous UPLOAD left it.
                var chunk = await UploadSequentialAsync(RegistryChunkEntries * T6SymbolRegistry.EntrySize, null, ct)
                    .ConfigureAwait(false);
                for (int i = 0; i < RegistryChunkEntries; i++)
                {
                    var record = chunk.AsSpan(i * T6SymbolRegistry.EntrySize, T6SymbolRegistry.EntrySize);
                    if (record.IndexOfAnyExcept((byte)0) < 0)
                        return T6SymbolRegistry.ParseTable(bytes.ToArray(), count);
                    if (count == T6SymbolRegistry.MaximumEntries)
                        throw new InvalidDataException("T6 registry exceeds the discovery limit or lacks its zero terminator.");
                    bytes.AddRange(record.ToArray());
                    count++;
                }
                progress?.Report(count);
            }
        }, cancellationToken);
    }

    private static string? ReadText(byte[] bytes)
    {
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = bytes.Length;
        if (end == 0 || bytes.Take(end).Any(b => b is < 0x20 or > 0x7E)) return null;
        return Encoding.ASCII.GetString(bytes, 0, end).TrimEnd();
    }

    private static void ValidateRange(uint address, int length)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        if ((ulong)address + (uint)length > 0x1_0000_0000UL)
            throw new ArgumentOutOfRangeException(nameof(length), "The range wraps the 32-bit address space.");
    }
}
