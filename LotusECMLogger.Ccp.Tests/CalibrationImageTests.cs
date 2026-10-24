using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CalibrationImageTests : IDisposable
{
    private const string Table = "CAL_injtip_transient_throttle_response_time"; // u8_time_5ms, 8 × 8 at 0x40008A9A
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static readonly CcpBuildProfile EvoraNa = CcpBuildProfile.Known.Single(p => p.Build == "B132E0091");
    private static readonly Lazy<A2lResolvedModule> Gt430A2l = new(() => Resolve(Gt430));
    private static readonly Lazy<A2lResolvedModule> EvoraNaA2l = new(() => Resolve(EvoraNa));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LotusECMLogger.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static A2lResolvedModule Resolve(CcpBuildProfile build) => A2lLayout.Resolve(A2lParser.Parse(
        A2lGenerator.Generate(SymbolCatalogLoader.LoadByName(build.Catalog)!, build).Text));

    /// <summary>A CAL image with the build's calibration ID and a recognisable fill.</summary>
    private static byte[] Image(CcpBuildProfile build, int extra = 0)
    {
        var bytes = new byte[build.CalibrationSize + extra];
        for (int i = 32; i < bytes.Length; i++) bytes[i] = (byte)(i * 7);
        Encoding.ASCII.GetBytes($"{build.Build} 01-01-2020 00:00").CopyTo(bytes, 0);
        return bytes;
    }

    private static CalibrationImage Gt430Image(out A2lCalibrationItem table)
    {
        table = Gt430A2l.Value.Items[Table];
        return CalibrationImage.FromBytes(Image(Gt430));
    }

    private string PathIn(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name);
    }

    [Fact]
    public void PicksTheBuildFromTheCalibrationId()
    {
        Assert.Equal("C132E0278", CalibrationImage.FromBytes(Image(Gt430)).Build.Build);
        Assert.Equal("B132E0091", CalibrationImage.FromBytes(Image(EvoraNa)).Build.Build);
        var unknown = Image(Gt430);
        "X999".Select(c => (byte)c).ToArray().CopyTo(unknown, 0);
        Assert.Contains("choose the build", Assert.Throws<InvalidDataException>(() => CalibrationImage.FromBytes(unknown)).Message);
        Assert.Throws<InvalidDataException>(() => CalibrationImage.FromBytes(Image(Gt430)[..^1]));
    }

    [Fact]
    public void DetectsTheFileLayoutAndSavesItBack()
    {
        var snapshot = Image(Gt430, CcpCalibrationWriter.SnapshotCalibrationSize - (int)Gt430.CalibrationSize);
        snapshot[^1] = 0xA5;
        var crpChunk = Image(EvoraNa, CcpCalibrationWriter.CrpTrailingBytes);
        crpChunk[^2..].AsSpan().Clear();
        crpChunk[^2] = 0xF7;
        crpChunk[^1] = 0xCA;

        foreach (var (file, format) in new[] { (Image(Gt430), CalibrationFileFormat.RawImage),
            (snapshot, CalibrationFileFormat.SnapshotDownload), (crpChunk, CalibrationFileFormat.CrpChunk) })
        {
            var image = CalibrationImage.FromBytes(file);
            Assert.Equal(format, image.Format);
            Assert.Equal((int)image.Build.CalibrationSize, image.Size);
            image.WriteBytes(image.BaseAddress + 0x100, [1, 2, 3], "poke");
            var saved = image.SaveAs(PathIn($"{format}.bin"));
            var written = File.ReadAllBytes(saved.Path);
            Assert.Equal(file.Length, written.Length);
            Assert.Equal(new byte[] { 1, 2, 3 }, written[0x100..0x103]);
            Assert.Equal(file[image.Size..], written[image.Size..]); // the snapshot tail and the CRP's CVN are kept
            Assert.Equal(format, saved.Format);
        }
    }

    [Fact]
    public void OpensTheCalibrationChunkOfACrpAndSavesItAsAChunk()
    {
        var chunk = Image(EvoraNa, CcpCalibrationWriter.CrpTrailingBytes);
        string cal = PathIn("cal.bin"), crp = PathIn("tune.CRP");
        File.WriteAllBytes(cal, chunk);
        File.WriteAllBytes(crp, CrpCreator.Build(CrpCreator.EcuType.T6, cal, null));

        var image = CalibrationImage.Load(crp);
        Assert.Equal((CalibrationFileFormat.Crp, "B132E0091"), (image.Format, image.Build.Build));
        var saved = image.SaveAs(PathIn("tune.bin"));
        Assert.Equal(CalibrationFileFormat.CrpChunk, saved.Format);
        Assert.Equal(chunk, File.ReadAllBytes(saved.Path));
    }

    [Fact]
    public void EditsCellsThroughTheA2lLayoutAndConversion()
    {
        var image = Gt430Image(out var table);
        var changes = new List<CalibrationChangedEventArgs>();
        image.Changed += (_, e) => changes.Add(e);

        image.SetPhysical(table, [new CellValue(3, 5, 50)]); // 50 ms = raw 10 at lut[5 * 8 + 3]

        Assert.Equal(10, image.Bytes(0x40008A9A + 5 * 8 + 3, 1)[0]);
        Assert.Equal(50.0, image.ReadPhysical(table, 3, 5), 9);
        var change = Assert.Single(changes);
        Assert.Equal([new CalibrationSpan(0x40008A9A + 43, 1)], change.Spans);
        Assert.Equal([new CalibrationSpan(0x40008A9A + 43, 1)], image.ChangedFromOriginal());
        Assert.True(image.IsChanged(table));
        Assert.False(image.IsChanged(Gt430A2l.Value.Items[Table + "_X_coolant_tmp"]));
        Assert.True(image.HasUnsavedChanges);
    }

    [Fact]
    public void UndoAndRedoRestoreBytesAndReportThem()
    {
        var image = Gt430Image(out var table);
        double before = image.ReadRaw(table, 0, 0);
        var descriptions = new List<string>();
        image.Changed += (_, e) => descriptions.Add(e.Description);

        image.SetRaw(table, [new CellValue(0, 0, 200), new CellValue(7, 7, 201)], "two cells");
        image.Undo();
        Assert.Equal(before, image.ReadRaw(table, 0, 0));
        Assert.Empty(image.ChangedFromOriginal());
        Assert.Equal("two cells", image.RedoDescription);
        image.Redo();
        Assert.Equal((200.0, 201.0), (image.ReadRaw(table, 0, 0), image.ReadRaw(table, 7, 7)));
        Assert.Equal(["two cells", "Undo two cells", "two cells"], descriptions);

        image.Undo();
        image.SetRaw(table, [new CellValue(1, 1, 9)]); // a new edit discards the redo step
        Assert.False(image.CanRedo);

        image.SetRaw(table, [new CellValue(1, 1, 9)]); // unchanged bytes are not an edit
        Assert.Equal("Edit " + Table, image.UndoDescription);
        image.Undo();
        Assert.False(image.CanUndo);
    }

    [Fact]
    public void AnInvalidCellLeavesTheWholeEditUndone()
    {
        var image = Gt430Image(out var table);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            image.SetPhysical(table, [new CellValue(0, 0, 50), new CellValue(1, 0, 5000)])); // limit 1275 ms
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            image.SetRaw(table, [new CellValue(0, 0, 10), new CellValue(1, 0, 256)]));     // not a byte
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetRaw(table, [new CellValue(8, 0, 1)]));
        Assert.Empty(image.ChangedFromOriginal());
        Assert.False(image.CanUndo);
    }

    [Fact]
    public void AxisPointsMayRepeatButNotDecrease()
    {
        var image = Gt430Image(out _);
        var axis = Gt430A2l.Value.Items[Table + "_X_coolant_tmp"];
        image.SetRaw(axis, Enumerable.Range(0, 8).Select(i => new CellValue(i, 0, 20 * i)).ToList());

        image.SetRaw(axis, [new CellValue(2, 0, 20)]); // equal to point 1: the lookups allow repeats
        var error = Assert.Throws<InvalidOperationException>(() => image.SetRaw(axis, [new CellValue(4, 0, 30)]));
        Assert.Contains("point 4", error.Message);
        Assert.Contains("increasing", error.Message);
        Assert.Equal(80.0, image.ReadRaw(axis, 4));
    }

    [Fact]
    public void RefusesReadOnlyItemsAndTheCcpEnableByte()
    {
        var image = CalibrationImage.FromBytes(Image(EvoraNa));
        var readOnly = EvoraNaA2l.Value.Items.Values.First(i => !i.IsEditable);
        Assert.Contains("read-only", Assert.Throws<InvalidOperationException>(() =>
            image.SetRaw(readOnly, [new CellValue(0, 0, 1)])).Message);

        uint enable = EvoraNa.EnableByteAddress!.Value;
        Assert.Contains("enable byte", Assert.Throws<InvalidOperationException>(() =>
            image.WriteBytes(enable - 1, [0x11, (byte)~image.Bytes(enable, 1)[0]], "toggle CCP")).Message);
        image.WriteBytes(enable, image.Bytes(enable, 1).ToArray(), "same value"); // not a change, so allowed
        Assert.Empty(image.ChangedFromOriginal());
    }

    [Fact]
    public void ReadsAxesAndText()
    {
        var image = Gt430Image(out var table);
        var x = table.Axes[0];
        image.SetRaw(Gt430A2l.Value.Items[x.AxisPtsName!], Enumerable.Range(0, 8).Select(i => new CellValue(i, 0, 64 + i)).ToList());
        Assert.Equal(Enumerable.Range(0, 8).Select(i => (64 + i) * 0.625 - 40).ToArray(), image.ReadAxisPhysical(x, table.ByteOrder));
        Assert.StartsWith("C132E0278 01-01-2020", image.ReadText(Gt430A2l.Value.Items["CAL_base"]));
        Assert.Equal("C132E0278 01-01-2020 00:00", image.CalibrationIdText);
    }

    [Fact]
    public void ChecksTheA2lMatchesTheImage()
    {
        var image = Gt430Image(out _);
        Assert.Empty(image.CheckAgainst(Gt430A2l.Value));
        var problems = image.CheckAgainst(EvoraNaA2l.Value);
        Assert.Contains(problems, p => p.Contains("\"B132E0091\"") && p.Contains("a C132E0278 calibration"));
        Assert.Contains(problems, p => p.Contains("calibration segment"));
    }

    [Fact]
    public void SaveAsNeverOverwritesTheSourceAndBacksUpWhatItReplaces()
    {
        string source = PathIn("source.bin"), copy = PathIn("copy.bin");
        File.WriteAllBytes(source, Image(Gt430));
        var image = CalibrationImage.Load(source);
        image.WriteBytes(image.BaseAddress + 0x200, [9], "poke");

        Assert.Throws<InvalidOperationException>(() => image.SaveAs(source));
        Assert.Equal(Image(Gt430), File.ReadAllBytes(source));

        Assert.Null(image.SaveAs(copy).BackupPath);
        Assert.False(image.HasUnsavedChanges);
        image.WriteBytes(image.BaseAddress + 0x200, [10], "poke again");
        var second = image.SaveAs(copy, backupDirectory: PathIn("backups"));
        Assert.Equal(9, File.ReadAllBytes(second.BackupPath!)[0x200]);
        Assert.Equal(10, File.ReadAllBytes(copy)[0x200]);
        Assert.Equal(10, CalibrationImage.Load(copy).Bytes(image.BaseAddress + 0x200, 1)[0]);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}
