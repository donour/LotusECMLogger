namespace LotusECMLogger.Services.Ccp;

/// <summary>A region of T6 (2011+) flash that the Snapshots tab downloads.</summary>
/// <param name="Name">Lower-case, for messages: "learned data".</param>
/// <param name="FilePrefix">Starts the default file name: "LearnedData".</param>
public sealed record T6FlashRegion(string Name, string FilePrefix, uint Address, int Length)
{
    /// <summary>The persisted adaptive trims (flash block L2, 48 KB).</summary>
    public static T6FlashRegion LearnedData { get; } = new("learned data", "LearnedData", 0x00010000, 0xC000);

    /// <summary>The calibration as flashed (L4, 64 KB), which <c>copyCAL2RAM</c> copies to calibration RAM at boot.</summary>
    public static T6FlashRegion Calibration { get; } = new("calibration", "Calibration", 0x00020000, 0x10000);

    /// <summary>The program (M0–H3, 768 KB).</summary>
    public static T6FlashRegion Program { get; } = new("program", "Program", 0x00040000, 0xC0000);

    public static IReadOnlyList<T6FlashRegion> All { get; } = [LearnedData, Calibration, Program];
}

/// <summary>
/// Downloads a region of flash over CCP. UPLOAD reads any address on these ECUs (CCP-support.md §6), so no unlock is
/// needed. After reading, the ECU checksums the same region itself and the two must agree, so a garbled or
/// misaddressed transfer is caught instead of saved.
/// </summary>
public static class CcpFlashSnapshot
{
    /// <exception cref="InvalidDataException">The ECU's checksum of the region differs from the bytes read.</exception>
    public static async Task<byte[]> ReadAsync(CcpMaster master, T6FlashRegion region, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(region);
        var bytes = await master.ReadAsync(region.Address, region.Length, progress, cancellationToken).ConfigureAwait(false);
        ushort expected = await master.ChecksumAsync(region.Address, region.Length, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        ushort actual = CcpFrame.AdditiveChecksum(bytes);
        if (expected != actual)
            throw new InvalidDataException($"The {region.Name} read does not match the ECU's own checksum of it " +
                $"(0x{expected:X4} on the ECU, 0x{actual:X4} read). Nothing was saved; download it again.");
        return bytes;
    }
}
