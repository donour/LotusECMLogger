using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services.Ccp;

/// <summary>Where the build profile used for a write came from.</summary>
public enum CcpBuildSource
{
    /// <summary>The ECU's calibration ID matched the profile.</summary>
    Identified,
    /// <summary>The ECU was not recognised and the user chose the profile, accepting the risk.</summary>
    UserOverride,
}

/// <summary>A contiguous run of bytes that differ from what the ECU holds now.</summary>
public readonly record struct CcpWriteRun(uint Address, int Length);

/// <summary>
/// A validated, not yet executed write: the region, the bytes that will be there afterwards, and the ECU's
/// current bytes (which become the backup). Produced by <see cref="CcpCalibrationWriter.PrepareAsync"/>.
/// </summary>
public sealed class CcpWritePlan
{
    internal CcpWritePlan(CcpBuildProfile build, CcpBuildSource source, uint address, byte[] current, byte[] target,
        IReadOnlyList<CcpWriteRun> runs, string? enableBytePreserved, double? engineSpeedRpm)
    {
        Build = build;
        Source = source;
        Address = address;
        Current = current;
        Target = target;
        Runs = runs;
        EnableBytePreserved = enableBytePreserved;
        EngineSpeedRpm = engineSpeedRpm;
    }

    public CcpBuildProfile Build { get; }
    public CcpBuildSource Source { get; }
    public uint Address { get; }
    public int Length => Target.Count;
    /// <summary>The ECU's bytes when the plan was made; saved as the backup before anything is written.</summary>
    public IReadOnlyList<byte> Current { get; }
    public IReadOnlyList<byte> Target { get; }
    public IReadOnlyList<CcpWriteRun> Runs { get; }
    public int ChangedBytes => Runs.Sum(r => r.Length);
    /// <summary>Set when the source differed at the CCP enable byte and the ECU's value was kept instead.</summary>
    public string? EnableBytePreserved { get; }
    /// <summary>The engine speed read while planning, or null when this build has no known speed symbol.</summary>
    public double? EngineSpeedRpm { get; }
}

/// <summary>Outcome of an executed plan.</summary>
public sealed record CcpWriteResult(CcpWritePlan Plan, string? BackupPath, int RunsWritten);

/// <summary>
/// A guarded write stopped part-way. <see cref="RunsWritten"/> runs were written and verified before the failure;
/// the failing run's bytes are unknown. The backup holds the region as it was, for restoring.
/// </summary>
public sealed class CcpCalibrationWriteException(string message, CcpWritePlan plan, string? backupPath, int runsWritten,
    Exception inner) : IOException(message, inner)
{
    public CcpWritePlan Plan { get; } = plan;
    public string? BackupPath { get; } = backupPath;
    public int RunsWritten { get; } = runsWritten;
}

/// <summary>Where the engine speed lives for a build, so writes can be refused while the engine turns.</summary>
public readonly record struct CcpEngineSpeedSymbol(uint Address, int Size, double Scale);

/// <summary>
/// Guarded writes into calibration RAM (Phase 5). Every write is limited to the build's CAL range, refused while
/// the engine turns (when the build's engine-speed symbol is known), backed up to a <c>.bin</c> before anything is
/// sent, written as runs of changed bytes only, and verified by read-back. The CCP enable byte is never changed:
/// a whole-image write keeps the ECU's value, and a value write that would change it is refused, because changing
/// it disables or moves CCP mid-write. Changes take effect on the next lookup and are lost at power-off.
/// The caller confirms with the user (engine off, live effect) between <see cref="PrepareAsync"/> and
/// <see cref="ExecuteAsync"/>.
/// </summary>
public sealed class CcpCalibrationWriter
{
    public const int SnapshotCalibrationSize = 0x10000; // a Snapshots calibration download: CAL_base onwards

    private readonly CcpMaster _master;
    private readonly CcpBuildProfile _build;
    private readonly CcpBuildSource _source;
    private readonly string _backupDirectory;
    private readonly CcpEngineSpeedSymbol? _engineSpeed;

    public CcpCalibrationWriter(CcpMaster master, CcpBuildProfile build, CcpBuildSource source, string backupDirectory,
        CcpEngineSpeedSymbol? engineSpeed = null)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        if (build.Target != master.Target)
            throw new ArgumentException($"{build.Build} is a {build.Target.Name} build; the session is {master.Target.Name}.", nameof(build));
        (_master, _build, _source, _backupDirectory, _engineSpeed) = (master, build, source, backupDirectory, engineSpeed);
    }

    public CcpMemoryRange CalibrationRange => CcpMemoryRange.Calibration(_build);

    /// <summary>
    /// Takes a calibration file: a raw CAL-region image (exactly the CAL size) or a 64 KB Snapshots calibration
    /// download (its first CAL-size bytes). Its calibration ID must match the build.
    /// </summary>
    public byte[] CalibrationImageFromFile(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        int size = checked((int)_build.CalibrationSize);
        if (file.Length != size && file.Length != SnapshotCalibrationSize)
            throw new InvalidDataException($"A {_build.Build} calibration file has {size:N0} bytes (raw image) or " +
                $"{SnapshotCalibrationSize:N0} bytes (Snapshots download); this one has {file.Length:N0}.");
        var image = file.AsSpan(0, size).ToArray();
        string? id = CalibrationId(image);
        if (id != _build.Build)
            throw new InvalidDataException($"The file's calibration ID is {id ?? "unreadable"}; the ECU is {_build.Build}. " +
                "Writing another build's calibration would corrupt this one.");
        return image;
    }

    /// <summary>The leading token of the 32-byte calibration ID at the start of a CAL image, if printable.</summary>
    public static string? CalibrationId(ReadOnlySpan<byte> image)
    {
        var head = image[..Math.Min(32, image.Length)];
        int end = head.IndexOfAny((byte)0, (byte)' ');
        if (end <= 0) return null;
        foreach (byte b in head[..end]) if (b is < 0x21 or > 0x7E) return null;
        return Encoding.ASCII.GetString(head[..end]);
    }

    /// <summary>
    /// Validates the write and reads the region as it is now. Nothing is written. Throws when the range leaves
    /// calibration RAM, the engine is turning, or the CCP enable byte would change.
    /// </summary>
    public async Task<CcpWritePlan> PrepareAsync(uint address, ReadOnlyMemory<byte> data, bool wholeImage = false,
        CancellationToken cancellationToken = default)
    {
        var range = CalibrationRange;
        if (data.IsEmpty || !range.Contains(address, data.Length))
            throw new ArgumentOutOfRangeException(nameof(address),
                $"0x{address:X8}+{data.Length:N0} is outside {_build.Build}'s calibration RAM " +
                $"0x{range.Start:X8}–0x{range.Start + range.Length - 1:X8}.");

        double? rpm = await ReadEngineSpeedAsync(cancellationToken).ConfigureAwait(false);
        if (rpm > 0)
            throw new InvalidOperationException($"The engine is turning ({rpm:0} rpm). Calibration writes need the engine off.");

        var current = await _master.ReadAsync(address, data.Length, cancellationToken: cancellationToken).ConfigureAwait(false);
        var target = data.ToArray();
        string? preserved = null;
        if (_build.EnableByteAddress is uint enable && enable >= address && enable - address < (uint)target.Length)
        {
            int offset = (int)(enable - address);
            if (target[offset] != current[offset])
            {
                if (!wholeImage)
                    throw new InvalidOperationException($"This would change the CCP enable byte at 0x{enable:X8} " +
                        $"(0x{current[offset]:X2} → 0x{target[offset]:X2}), which disables or moves CCP mid-write. Refused.");
                preserved = $"0x{enable:X8}: kept the ECU's 0x{current[offset]:X2} instead of the file's 0x{target[offset]:X2}";
                target[offset] = current[offset];
            }
        }
        return new CcpWritePlan(_build, _source, address, current, target, ChangedRuns(address, current, target), preserved, rpm);
    }

    /// <summary>
    /// Saves the backup, then writes each changed run with read-back verification. Refuses if the region changed
    /// since the plan was made (compared by the ECU's checksum, without re-reading it). An interrupted or
    /// unverified run throws <see cref="CcpCalibrationWriteException"/>; nothing is retried.
    /// </summary>
    public async Task<CcpWriteResult> ExecuteAsync(CcpWritePlan plan, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Build != _build) throw new ArgumentException("The plan was made for another build.", nameof(plan));
        if (plan.Runs.Count == 0) return new CcpWriteResult(plan, null, 0);

        var current = plan.Current.ToArray();
        ushort now = await _master.ChecksumAsync(plan.Address, plan.Length, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (now != CcpFrame.AdditiveChecksum(current))
            throw new InvalidOperationException("The region changed on the ECU since the write was prepared. Prepare it again.");

        string backupPath = SaveBackup(plan.Address, current);
        int written = 0, bytes = 0;
        var target = plan.Target.ToArray();
        try
        {
            foreach (var run in plan.Runs)
            {
                var chunk = target.AsMemory((int)(run.Address - plan.Address), run.Length);
                await _master.WriteVerifiedAsync(run.Address, chunk, CalibrationRange, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                written++;
                bytes += run.Length;
                progress?.Report(bytes);
            }
        }
        catch (Exception ex)
        {
            throw new CcpCalibrationWriteException($"Calibration write stopped after {written} of {plan.Runs.Count} run(s): " +
                $"{ex.Message} The backup is {backupPath}.", plan, backupPath, written, ex);
        }
        return new CcpWriteResult(plan, backupPath, written);
    }

    private async Task<double?> ReadEngineSpeedAsync(CancellationToken ct)
    {
        if (_engineSpeed is not { } symbol) return null;
        var raw = await _master.ReadAsync(symbol.Address, symbol.Size, cancellationToken: ct).ConfigureAwait(false);
        long value = 0;
        foreach (byte b in raw) value = value << 8 | b;
        return value * symbol.Scale;
    }

    private string SaveBackup(uint address, byte[] bytes)
    {
        // Self-describing name: Restore reads the address back from it.
        Directory.CreateDirectory(_backupDirectory);
        string name = string.Create(CultureInfo.InvariantCulture,
            $"ccp-backup_{_build.Build}_{address:X8}_{bytes.Length}_{DateTime.Now:yyyyMMdd-HHmmss-fff}.bin");
        string path = Path.Combine(_backupDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Reads the address back out of a backup file name written by this class.</summary>
    public static bool TryParseBackupName(string path, out string build, out uint address, out int length)
    {
        (build, address, length) = ("", 0, 0);
        var parts = Path.GetFileNameWithoutExtension(path).Split('_');
        return parts.Length == 5 && parts[0] == "ccp-backup" && (build = parts[1]).Length > 0 &&
            uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) &&
            int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out length);
    }

    /// <summary>
    /// Runs of differing bytes, in address order. Runs separated by at most <see cref="MergeGap"/> unchanged bytes are
    /// merged: rewriting an identical byte is harmless, and fewer runs mean fewer commands and a shorter window in which
    /// a lookup can see a half-updated table.
    /// </summary>
    public static IReadOnlyList<CcpWriteRun> ChangedRuns(uint address, ReadOnlySpan<byte> current, ReadOnlySpan<byte> target)
    {
        if (current.Length != target.Length) throw new ArgumentException("Current and target lengths differ.");
        var runs = new List<CcpWriteRun>();
        for (int i = 0; i < target.Length;)
        {
            if (current[i] == target[i]) { i++; continue; }
            int start = i;
            while (i < target.Length && current[i] != target[i]) i++;
            if (runs.Count > 0 && address + (uint)start - (runs[^1].Address + (uint)runs[^1].Length) <= MergeGap)
                runs[^1] = runs[^1] with { Length = (int)(address + (uint)i - runs[^1].Address) };
            else runs.Add(new CcpWriteRun(address + (uint)start, i - start));
        }
        return runs;
    }

    public const int MergeGap = 4;
}
