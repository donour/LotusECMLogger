using System.Globalization;
using System.Text;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>How a calibration file was laid out on disk; Save As writes the same layout back.</summary>
    public enum CalibrationFileFormat
    {
        /// <summary>Exactly the build's CAL RAM range.</summary>
        RawImage,
        /// <summary>The calibration chunk of a CRP, with up to two bytes past the range (the CVN on B132E0091).</summary>
        CrpChunk,
        /// <summary>A 64 KB Snapshots calibration download starting at CAL_base.</summary>
        SnapshotDownload,
        /// <summary>A .CRP file; the calibration chunk was extracted and is saved as <see cref="CrpChunk"/>.</summary>
        Crp,
    }

    public readonly record struct CalibrationSpan(uint Address, int Length);

    /// <summary>One cell to set: X along the first axis, Y along the second (0 for curves and values).</summary>
    public readonly record struct CellValue(int X, int Y, double Value);

    public sealed class CalibrationChangedEventArgs(IReadOnlyList<CalibrationSpan> spans, string description) : EventArgs
    {
        /// <summary>The byte ranges whose contents changed, in address order.</summary>
        public IReadOnlyList<CalibrationSpan> Spans { get; } = spans;
        public string Description { get; } = description;
    }

    public sealed record CalibrationSaveResult(string Path, string? BackupPath, CalibrationFileFormat Format);

    /// <summary>
    /// A calibration file being edited (Phase 6b). The image is a straight copy of CAL RAM: a byte's offset is its
    /// address minus CAL_base (<c>copyCAL2RAM</c> copies flash 0x20000 there 1:1). Edits go through A2L items so cells
    /// are found by <see cref="A2lCalibrationItem.CellAddress"/> and values by the item's conversion. Every edit is one
    /// undo step and raises <see cref="Changed"/> with the bytes it touched, which live sync sends to the ECU.
    /// The source file is never overwritten: <see cref="SaveAs"/> refuses its path.
    /// </summary>
    public sealed class CalibrationImage
    {
        public const int MaxUndoSteps = 500;

        private sealed record Patch(int Offset, byte[] Before, byte[] After);
        private sealed record Edit(string Description, IReadOnlyList<Patch> Patches);

        private readonly byte[] _container; // the file as loaded; the CAL region is its first Size bytes
        private readonly byte[] _original;
        private readonly byte[] _image;
        private byte[] _saved;
        private readonly LinkedList<Edit> _undo = new();
        private readonly Stack<Edit> _redo = new();

        public CcpBuildProfile Build { get; }
        public CalibrationFileFormat Format { get; }
        public string? SourcePath { get; }
        public uint BaseAddress => Build.CalibrationBase;
        public int Size => _image.Length;
        /// <summary>The 32-byte calibration ID at CAL_base, e.g. <c>B132E0091 03-09-2015 11:45</c>.</summary>
        public string CalibrationIdText { get; }

        public event EventHandler<CalibrationChangedEventArgs>? Changed;

        private CalibrationImage(CcpBuildProfile build, byte[] container, byte[] image, CalibrationFileFormat format, string? sourcePath)
        {
            (Build, _container, Format, SourcePath) = (build, container, format, sourcePath);
            _image = image;
            _original = (byte[])image.Clone();
            _saved = (byte[])image.Clone();
            CalibrationIdText = Encoding.ASCII.GetString(image, 0, Math.Min(32, image.Length)).TrimEnd('\0', ' ');
        }

        /// <summary>Opens a raw image, CRP calibration chunk, Snapshots download or .CRP. With no build given, the
        /// file's calibration ID picks one of <see cref="CcpBuildProfile.Known"/>.</summary>
        public static CalibrationImage Load(string path, CcpBuildProfile? build = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string fullPath = Path.GetFullPath(path);
            if (string.Equals(Path.GetExtension(path), ".crp", StringComparison.OrdinalIgnoreCase))
            {
                var chunks = CrpUnpacker.Unpack(fullPath).Chunks;
                var calibration = chunks.Where(c => c.RealAddress == 0x20000 && c.EcuId == "ECU T6").ToList();
                if (calibration.Count != 1)
                    throw new InvalidDataException($"{Path.GetFileName(path)} has {calibration.Count} T6 calibration chunks; expected one.");
                return FromBytes(calibration[0].Data, build, fullPath, CalibrationFileFormat.Crp);
            }
            return FromBytes(File.ReadAllBytes(fullPath), build, fullPath);
        }

        public static CalibrationImage FromBytes(byte[] file, CcpBuildProfile? build = null, string? sourcePath = null,
            CalibrationFileFormat? format = null)
        {
            ArgumentNullException.ThrowIfNull(file);
            var profile = build ?? CcpBuildProfile.Known.FirstOrDefault(p => p.Build == CcpCalibrationWriter.CalibrationId(file))
                ?? throw new InvalidDataException($"No known build has the calibration ID " +
                    $"{CcpCalibrationWriter.CalibrationId(file) ?? "(unreadable)"}; choose the build by hand.");
            var image = CcpCalibrationWriter.CalibrationImageFromFile(profile, file);
            var detected = file.Length == image.Length ? CalibrationFileFormat.RawImage
                : file.Length == CcpCalibrationWriter.SnapshotCalibrationSize ? CalibrationFileFormat.SnapshotDownload
                : CalibrationFileFormat.CrpChunk;
            if (format == CalibrationFileFormat.Crp && detected == CalibrationFileFormat.SnapshotDownload)
                throw new InvalidDataException("A CRP calibration chunk cannot be 64 KB.");
            return new CalibrationImage(profile, (byte[])file.Clone(), image, format ?? detected, sourcePath);
        }

        // ── Reading ──────────────────────────────────────────────────────────────────────────────────────────

        public bool Contains(uint address, int length) => length > 0 && address >= BaseAddress &&
            (ulong)address + (uint)length <= (ulong)BaseAddress + (uint)Size;

        public bool Contains(A2lCalibrationItem item) => item.Spans().All(s => Contains(s.Address, s.Length));

        public ReadOnlySpan<byte> Bytes(uint address, int length) => _image.AsSpan(Offset(address, length), length);

        public ReadOnlySpan<byte> OriginalBytes(uint address, int length) => _original.AsSpan(Offset(address, length), length);

        public double ReadRaw(A2lCalibrationItem item, int x, int y = 0) =>
            item.ValueType.ReadRaw(Bytes(item.CellAddress(x, y), item.ElementSize), item.ByteOrder);

        public double ReadPhysical(A2lCalibrationItem item, int x, int y = 0) => item.Conversion.ToPhysical(ReadRaw(item, x, y));

        public double ReadOriginalRaw(A2lCalibrationItem item, int x, int y = 0) =>
            item.ValueType.ReadRaw(OriginalBytes(item.CellAddress(x, y), item.ElementSize), item.ByteOrder);

        /// <summary>An axis's raw points: read from the image, or the fixed points of a FIX_AXIS.</summary>
        public double[] ReadAxisRaw(A2lAxisLayout axis, A2lByteOrder byteOrder)
        {
            if (!axis.IsStored) return axis.FixedRaw!.ToArray();
            var points = new double[axis.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = axis.Type.ReadRaw(Bytes(axis.PointAddress(i), axis.Type.Size()), byteOrder);
            return points;
        }

        public double[] ReadAxisPhysical(A2lAxisLayout axis, A2lByteOrder byteOrder) =>
            ReadAxisRaw(axis, byteOrder).Select(axis.Conversion.ToPhysical).ToArray();

        /// <summary>An ASCII item's text, up to its first NUL.</summary>
        public string ReadText(A2lCalibrationItem item)
        {
            var bytes = Bytes(item.Address, item.ElementCount);
            int end = bytes.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
        }

        // ── Comparing ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Runs of bytes that differ from the file as loaded, in address order.</summary>
        public IReadOnlyList<CalibrationSpan> ChangedFromOriginal()
        {
            var spans = new List<CalibrationSpan>();
            for (int i = 0; i < _image.Length;)
            {
                if (_image[i] == _original[i]) { i++; continue; }
                int start = i;
                while (i < _image.Length && _image[i] != _original[i]) i++;
                spans.Add(new CalibrationSpan(BaseAddress + (uint)start, i - start));
            }
            return spans;
        }

        public int ChangedByteCount => ChangedFromOriginal().Sum(s => s.Length);

        public bool IsChanged(uint address, int length) =>
            !Bytes(address, length).SequenceEqual(OriginalBytes(address, length));

        public bool IsChanged(A2lCalibrationItem item) => item.Spans().Any(s => IsChanged(s.Address, s.Length));

        public bool HasUnsavedChanges => !_image.AsSpan().SequenceEqual(_saved);

        /// <summary>
        /// Why this image does not suit the A2L, or empty: the EPK must be at ADDR_EPK, the A2L's calibration segment
        /// must be this build's CAL range, and every resolved item must lie inside the image.
        /// </summary>
        public IReadOnlyList<string> CheckAgainst(A2lResolvedModule a2l)
        {
            ArgumentNullException.ThrowIfNull(a2l);
            var problems = new List<string>();
            var module = a2l.Module;
            if (module.Epk is { } epk)
            {
                if (module.EpkAddress is not uint at) problems.Add("The A2L has an EPK but no ADDR_EPK.");
                else if (!Contains(at, Math.Max(1, epk.Length))) problems.Add($"ADDR_EPK 0x{at:X8} is outside the calibration image.");
                else
                {
                    var found = Bytes(at, epk.Length);
                    if (!found.SequenceEqual(Encoding.ASCII.GetBytes(epk)))
                    {
                        string shown = new(found.ToArray().Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.').ToArray());
                        problems.Add($"The A2L is for \"{epk}\", but this is a {Build.Build} calibration " +
                            $"(0x{at:X8} holds \"{shown}\").");
                    }
                }
            }
            else problems.Add("The A2L has no EPK, so it cannot be checked against the calibration.");
            if (module.CalibrationSegment is { } segment && (segment.Address != BaseAddress || segment.Size != (uint)Size))
                problems.Add($"The A2L's calibration segment is 0x{segment.Address:X8}+0x{segment.Size:X}; {Build.Build}'s is " +
                    $"0x{BaseAddress:X8}+0x{Size:X}.");
            var outside = a2l.Items.Values.Where(i => !Contains(i)).Select(i => i.Name).Order(StringComparer.Ordinal).ToList();
            if (outside.Count > 0)
                problems.Add($"{outside.Count} item(s) lie outside the image, e.g. {string.Join(", ", outside.Take(5))}.");
            return problems;
        }

        // ── Editing ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Sets cells to physical values, as one undo step. Nothing changes unless every cell is valid.</summary>
        public void SetPhysical(A2lCalibrationItem item, IReadOnlyList<CellValue> cells, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(cells);
            RequireEditable(item);
            var raw = new CellValue[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                double tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(item.LowerLimit), Math.Abs(item.UpperLimit)));
                if (double.IsNaN(cell.Value) || cell.Value < item.LowerLimit - tolerance || cell.Value > item.UpperLimit + tolerance)
                    throw new ArgumentOutOfRangeException(nameof(cells), string.Create(CultureInfo.InvariantCulture,
                        $"{item.Name}[{cell.X},{cell.Y}]: {cell.Value} is outside its limits {item.LowerLimit} to {item.UpperLimit}."));
                raw[i] = cell with { Value = item.Conversion.ToRaw(cell.Value) };
            }
            SetRawCore(item, raw, description ?? $"Edit {item.Name}");
        }

        /// <summary>Sets cells to raw values, as one undo step. Nothing changes unless every cell is valid.</summary>
        public void SetRaw(A2lCalibrationItem item, IReadOnlyList<CellValue> cells, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(cells);
            RequireEditable(item);
            SetRawCore(item, cells, description ?? $"Edit {item.Name}");
        }

        private void SetRawCore(A2lCalibrationItem item, IReadOnlyList<CellValue> cells, string description)
        {
            if (!Contains(item)) throw new InvalidOperationException($"{item.Name} lies outside the calibration image.");
            var start = item.Spans().First();
            var scratch = Bytes(start.Address, start.Length).ToArray();
            var edited = new HashSet<int>();
            foreach (var cell in cells)
            {
                int offset = (int)(item.CellAddress(cell.X, cell.Y) - item.Address);
                try { item.ValueType.WriteRaw(scratch.AsSpan(offset, item.ElementSize), cell.Value, item.ByteOrder); }
                catch (ArgumentOutOfRangeException e)
                {
                    throw new ArgumentOutOfRangeException(nameof(cells), $"{item.Name}[{cell.X},{cell.Y}]: {e.Message}");
                }
                edited.Add(offset / item.ElementSize);
            }
            if (item.Kind == A2lItemKind.AxisPts) RequireIncreasing(item, scratch, edited);
            Apply(description, [(start.Address, scratch)]);
        }

        // The lookups walk axes with "while (axis[i] < input) i++" and handle repeated points, so equal neighbours are
        // fine; a point below its predecessor would make every lookup past it wrong.
        private static void RequireIncreasing(A2lCalibrationItem axis, byte[] points, HashSet<int> edited)
        {
            double Point(int i) => axis.ValueType.ReadRaw(points.AsSpan(i * axis.ElementSize, axis.ElementSize), axis.ByteOrder);
            for (int i = 1; i < axis.CountX; i++)
            {
                if (!edited.Contains(i) && !edited.Contains(i - 1)) continue;
                if (Point(i) < Point(i - 1))
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                        $"{axis.Name}: point {i} ({axis.Conversion.ToPhysical(Point(i)):G6}) would be below point {i - 1} " +
                        $"({axis.Conversion.ToPhysical(Point(i - 1)):G6}). The ECU's lookups need axis points in increasing order."));
            }
        }

        /// <summary>Replaces bytes directly. One undo step.</summary>
        public void WriteBytes(uint address, ReadOnlySpan<byte> bytes, string description) =>
            WriteBytes([(address, bytes.ToArray())], description);

        /// <summary>Replaces several byte ranges as one undo step, e.g. to take the ECU's values into the editor.</summary>
        public void WriteBytes(IReadOnlyList<(uint Address, byte[] Bytes)> writes, string description)
        {
            ArgumentNullException.ThrowIfNull(writes);
            ArgumentException.ThrowIfNullOrWhiteSpace(description);
            foreach (var (address, bytes) in writes) Offset(address, bytes.Length);
            Apply(description, writes);
        }

        private static void RequireEditable(A2lCalibrationItem item)
        {
            if (!item.IsEditable) throw new InvalidOperationException($"{item.Name} is read-only: {item.ReadOnlyReason}");
        }

        private void Apply(string description, IEnumerable<(uint Address, byte[] Bytes)> writes)
        {
            var patches = new List<Patch>();
            foreach (var (address, bytes) in writes)
            {
                int offset = Offset(address, bytes.Length);
                for (int i = 0; i < bytes.Length;)
                {
                    if (_image[offset + i] == bytes[i]) { i++; continue; }
                    int run = i;
                    while (i < bytes.Length && _image[offset + i] != bytes[i]) i++;
                    patches.Add(new Patch(offset + run, _image.AsSpan(offset + run, i - run).ToArray(), bytes[run..i]));
                }
            }
            if (patches.Count == 0) return;
            if (Build.EnableByteAddress is uint enable && patches.Any(p => Covers(p, enable)))
                throw new InvalidOperationException($"This would change the CCP enable byte at 0x{enable:X8}, which disables or " +
                    "moves CCP. The ECU write would be refused, so the edit is too.");
            var edit = new Edit(description, patches);
            Do(edit, after: true);
            _undo.AddLast(edit);
            if (_undo.Count > MaxUndoSteps) _undo.RemoveFirst();
            _redo.Clear();
        }

        private bool Covers(Patch patch, uint address) =>
            address >= BaseAddress + (uint)patch.Offset && address < BaseAddress + (uint)(patch.Offset + patch.After.Length);

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string? UndoDescription => _undo.Last?.Value.Description;
        public string? RedoDescription => _redo.TryPeek(out var edit) ? edit.Description : null;

        public void Undo()
        {
            if (_undo.Last is not { } last) return;
            _undo.RemoveLast();
            Do(last.Value, after: false);
            _redo.Push(last.Value);
        }

        public void Redo()
        {
            if (!_redo.TryPop(out var edit)) return;
            Do(edit, after: true);
            _undo.AddLast(edit);
        }

        private void Do(Edit edit, bool after)
        {
            foreach (var patch in edit.Patches)
                (after ? patch.After : patch.Before).CopyTo(_image, patch.Offset);
            var spans = edit.Patches.OrderBy(p => p.Offset)
                .Select(p => new CalibrationSpan(BaseAddress + (uint)p.Offset, p.After.Length)).ToList();
            Changed?.Invoke(this, new CalibrationChangedEventArgs(spans, after ? edit.Description : $"Undo {edit.Description}"));
        }

        private int Offset(uint address, int length)
        {
            if (!Contains(address, length))
                throw new ArgumentOutOfRangeException(nameof(address),
                    $"0x{address:X8}+{length} is outside the calibration image 0x{BaseAddress:X8}+0x{Size:X}.");
            return (int)(address - BaseAddress);
        }

        // ── Saving ───────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the image in the layout it was loaded in (a CRP's chunk as a chunk). Refuses the source file. An existing
        /// file at <paramref name="path"/> is first copied to <paramref name="backupDirectory"/> (default: beside it);
        /// the new file is written to a temporary name and then moved into place.
        /// </summary>
        public CalibrationSaveResult SaveAs(string path, string? backupDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string target = Path.GetFullPath(path);
            if (SourcePath is not null && string.Equals(target, SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save As will not overwrite the file the calibration was loaded from.");

            var output = (byte[])_container.Clone();
            _image.CopyTo(output, 0);
            string directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            string? backup = null;
            if (File.Exists(target))
            {
                string backupDir = backupDirectory ?? directory;
                Directory.CreateDirectory(backupDir);
                backup = Path.Combine(backupDir, string.Create(CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(target)}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak"));
                File.Copy(target, backup);
            }
            string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, output);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            _saved = (byte[])_image.Clone();
            var format = Format == CalibrationFileFormat.Crp ? CalibrationFileFormat.CrpChunk : Format;
            return new CalibrationSaveResult(target, backup, format);
        }
    }
}
