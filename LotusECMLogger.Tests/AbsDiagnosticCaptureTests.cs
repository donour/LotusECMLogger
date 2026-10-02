using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LotusECMLogger.Services;

namespace LotusECMLogger.Tests;

public sealed class AbsDiagnosticCaptureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AbsCaptureTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 16, 20, 30, TimeSpan.Zero);
    private const string Live = "61 04 EF 06 A0 00 30 00 F6 18 00 00 F6 FF 33 01 FB FF 96 AF 00 00";

    public AbsDiagnosticCaptureTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);
    private string FilePath(string name) => Path.Combine(_directory, name);

    private static AbsDiagnosticExchange Exchange(string request, string response, double elapsed = 0,
        bool success = true, string error = "") => new(Start.AddTicks((long)(elapsed * 10000)), elapsed, request, response, success, error);

    private static AbsDiagnosticBaseline Baseline()
    {
        byte[] build = [0x5A, 0x85, .. Encoding.ASCII.GetBytes("6863802010000"), .. new byte[13]];
        byte[] part = [0x5A, 0x87, .. Encoding.ASCII.GetBytes("A132J0314A ")];
        return AbsDiagnosticCapture.BuildBaseline(Start,
        [
            Exchange("1A 85", Convert.ToHexString(build)),
            Exchange("1A 87", Convert.ToHexString(part), 1),
            Exchange("21 01", "61 01 07 41", 2),
            Exchange("21 BF", "61 BF 99", 3),
        ]);
    }

    [Fact]
    public void StandaloneBaselineRecomputesFieldsAndRetainsRawExchanges()
    {
        string path = FilePath("baseline.json");
        var baseline = Baseline();
        AbsDiagnosticCaptureFile.SaveBaseline(path, baseline, "notes, with \"quotes\"\nand a newline");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["baseline"]!["firmwareReference"] = "forged reference";
        json["baseline"]!["rows"] = "not even the expected derived type";
        json["baseline"]!["selectedProfile"] = 999;
        File.WriteAllText(path, json.ToJsonString());

        var loaded = AbsDiagnosticCaptureFile.Load(path);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Empty(loaded.Samples);
        Assert.Equal(AbsDiagnosticCapture.ReferenceName, loaded.Baseline!.FirmwareReference);
        Assert.Equal(baseline.CapturedUtc, loaded.Baseline.CapturedUtc);
        Assert.Equal(baseline.Exchanges.ToArray(), loaded.Baseline.Exchanges.ToArray());
        Assert.Contains(loaded.Baseline.Rows, row => row.Field == "Coding word" && row.Value == "0x4107");
        Assert.DoesNotContain(loaded.Baseline.Rows, row => row.Value.Contains("999"));
        Assert.Equal("notes, with \"quotes\"\nand a newline", loaded.Notes);
    }

    [Fact]
    public void JournalFlushesBaselineAndEverySampleIncludingFailures()
    {
        string path = FilePath("capture.jsonl");
        var baseline = Baseline();
        using (var writer = new AbsDiagnosticCaptureWriter(path, baseline, "monitor"))
        {
            Assert.Empty(AbsDiagnosticCaptureFile.Load(path).Samples);
            var good = AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 10.125), baseline);
            writer.Append(good);
            var first = AbsDiagnosticCaptureFile.Load(path);
            Assert.Single(first.Samples);
            Assert.Equal(99.84375, first.Samples[0].Data!.Wheels[0].Kph);
            var failed = AbsDiagnosticCapture.BuildSample(Exchange("21 04", "7F 21 22", 12.5,
                false, "lost, \"reply\"\nretry"), baseline);
            writer.Append(failed);
            var loaded = AbsDiagnosticCaptureFile.Load(path);
            Assert.Equal(2, loaded.Samples.Count);
            Assert.Equal(failed.Exchange, loaded.Samples[1].Exchange);
            Assert.Equal(failed.TimestampUtc, loaded.Samples[1].TimestampUtc);
            Assert.Equal(12.5, loaded.Samples[1].ElapsedMilliseconds);
            Assert.Null(loaded.Samples[1].Data);
            Assert.Contains(loaded.Samples[1].Rows, row => row.Detail.Contains("no previous sample"));
        }
        Assert.EndsWith("\n", File.ReadAllText(path));
    }

    [Fact]
    public void ImportedSampleMetadataAndScaledValuesCannotOverrideRawExchange()
    {
        string path = FilePath("tampered.jsonl");
        var baseline = Baseline();
        using (var writer = new AbsDiagnosticCaptureWriter(path, baseline))
            writer.Append(AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 8.25), baseline));
        string[] lines = File.ReadAllLines(path);
        var sample = JsonNode.Parse(lines[1])!;
        sample["sample"]!["data"] = "arbitrary serialized derived values";
        sample["sample"]!["rows"] = null;
        sample["sample"]!["elapsedMilliseconds"] = -5000;
        sample["sample"]!["timestampUtc"] = "1900-01-01T00:00:00Z";
        File.WriteAllText(path, lines[0] + "\n" + sample.ToJsonString() + "\n");
        var rebuilt = AbsDiagnosticCaptureFile.Load(path).Samples[0];
        Assert.Equal(8.25, rebuilt.ElapsedMilliseconds);
        Assert.Equal(Start.AddMilliseconds(8.25), rebuilt.TimestampUtc);
        Assert.Equal(99.84375, rebuilt.Data!.Wheels[0].Kph);

        var header = JsonNode.Parse(lines[0])!;
        header["baseline"]!["exchanges"]![0]!["responseHex"] = "5A 85 00";
        header["baseline"]!["firmwareReference"] = AbsDiagnosticCapture.ReferenceName;
        File.WriteAllText(path, header.ToJsonString() + "\n" + sample.ToJsonString() + "\n");
        var unknown = AbsDiagnosticCaptureFile.Load(path);
        Assert.Equal("unknown", unknown.Baseline!.FirmwareReference);
        Assert.Null(unknown.Samples[0].Data);
        Assert.True(unknown.Samples[0].Exchange.Success);
        Assert.Equal(Live, unknown.Samples[0].Exchange.ResponseHex);
    }

    [Fact]
    public void ConflictingOrFailedRepeatedIdentityReadPreventsReferenceMatch()
    {
        var original = Baseline();
        var conflicting = AbsDiagnosticCapture.BuildBaseline(Start,
            original.Exchanges.Append(Exchange("1A 85", "5A 85 00", 4)));
        Assert.Equal("unknown", conflicting.FirmwareReference);
        var failed = AbsDiagnosticCapture.BuildBaseline(Start,
            original.Exchanges.Append(Exchange("1A 87", "", 4, false, "timeout")));
        Assert.Equal("unknown", failed.FirmwareReference);
        var forged = failed with { FirmwareReference = AbsDiagnosticCapture.ReferenceName };
        Assert.Null(AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live), forged).Data);
    }

    [Fact]
    public void FailedOrMalformedReplyNeverCarriesForwardData()
    {
        var baseline = Baseline();
        foreach (var exchange in new[]
        {
            Exchange("21 04", "", 0, false, "timeout"),
            Exchange("21 04", "7F 21 31"),
            Exchange("21 04", "61 04 00"),
            Exchange("21 01", Live),
        })
        {
            var result = AbsDiagnosticCapture.BuildSample(exchange, baseline);
            Assert.Null(result.Data);
            Assert.Equal(exchange.ResponseHex, result.Exchange.ResponseHex);
            Assert.Equal(exchange.Success, result.Exchange.Success);
        }
    }

    [Fact]
    public void CsvUsesInvariantNumbersQuotesTextAndLeavesFaultValueBlank()
    {
        var baseline = Baseline();
        string fault = Live.Replace("EF 06", "FF 3F");
        var document = new AbsDiagnosticCaptureDocument
        {
            Baseline = baseline, Notes = "note, \"quoted\"\nsecond line",
            Samples =
            [
                AbsDiagnosticCapture.BuildSample(Exchange("21 04", fault, 1.25), baseline),
                AbsDiagnosticCapture.BuildSample(Exchange("21 04", "", 2.5, false, "lost, \"reply\"\nretry"), baseline)
            ]
        };
        string path = FilePath("capture.csv");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            AbsDiagnosticCaptureFile.ExportCsv(path, document);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var rows = ParseCsv(File.ReadAllText(path));
        Assert.All(rows, row => Assert.Equal(14, row.Length));
        Assert.Equal(document.Notes, rows.Single(row => row[0] == "notes")[13]);
        var wheel = rows.Single(row => row[0] == "sample" && row[8] == "front_left");
        Assert.Equal("1.25", wheel[2]);
        Assert.Equal(fault, wheel[4]);
        Assert.Equal("16383", wheel[9]);
        Assert.Equal("", wheel[10]);
        Assert.Equal("fault_sentinel", wheel[12]);
        var pressure = rows.Single(row => row[0] == "sample" && row[8] == "pressure");
        Assert.Equal("99.9285", pressure[10]);
        var failure = rows.Single(row => row[0] == "sample" && row[12] == "transport_failed");
        Assert.Equal("lost, \"reply\"\nretry", failure[6]);
        Assert.Equal("", failure[10]);
    }

    [Fact]
    public void WriterRejectsBackwardTimeAndIsIdempotentlyDisposable()
    {
        string path = FilePath("monotonic.jsonl");
        var baseline = Baseline();
        var writer = new AbsDiagnosticCaptureWriter(path, baseline);
        writer.Append(AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 10), baseline));
        Assert.Throws<InvalidDataException>(() => writer.Append(
            AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 9), baseline)));
        Assert.Single(AbsDiagnosticCaptureFile.Load(path).Samples);
        writer.Dispose();
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.Append(
            AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 20), baseline)));
        foreach (double elapsed in new[] { double.NaN, double.PositiveInfinity, -1d })
            Assert.Throws<InvalidDataException>(() => AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live) with
                { ElapsedMilliseconds = elapsed }, baseline));
    }

    [Fact]
    public void JournalRejectsTruncatedMalformedAndBackwardRecords()
    {
        string path = FilePath("journal.jsonl");
        var baseline = Baseline();
        using (var writer = new AbsDiagnosticCaptureWriter(path, baseline))
        {
            writer.Append(AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 10), baseline));
            writer.Append(AbsDiagnosticCapture.BuildSample(Exchange("21 04", Live, 20), baseline));
        }
        string original = File.ReadAllText(path);
        File.WriteAllText(path, original[..^1]);
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path)).Message);
        File.WriteAllText(path, original + "{broken}\n");
        Assert.Contains("line 4", Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path)).Message);
        File.WriteAllText(path, original + "\n");
        Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path));
        string[] lines = original.Split('\n');
        File.WriteAllText(path, lines[0] + "\n" + lines[2] + "\n" + lines[1] + "\n");
        Assert.Contains("monotonic", Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path)).Message);
    }

    [Fact]
    public void NewFilesNeverOverwriteExistingContent()
    {
        string path = FilePath("existing");
        File.WriteAllText(path, "keep this");
        Assert.Throws<IOException>(() => AbsDiagnosticCaptureFile.SaveBaseline(path, Baseline()));
        Assert.Throws<IOException>(() => new AbsDiagnosticCaptureWriter(path, Baseline()));
        Assert.Throws<IOException>(() => AbsDiagnosticCaptureFile.ExportCsv(path,
            new AbsDiagnosticCaptureDocument { Baseline = Baseline() }));
        Assert.Equal("keep this", File.ReadAllText(path));
    }

    [Fact]
    public void ImportRejectsUnsupportedSchemaDuplicateRawFieldsAndOversizedFile()
    {
        string path = FilePath("limits.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"baseline\":null,\"samples\":[],\"notes\":\"\"}");
        Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path));
        AbsDiagnosticCaptureFile.SaveBaseline(FilePath("valid.json"), Baseline());
        string valid = File.ReadAllText(FilePath("valid.json"));
        File.WriteAllText(path, valid.Replace("\"requestHex\":", "\"requestHex\":\"1A85\",\"requestHex\":"));
        Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path));
        using (var stream = new FileStream(path, FileMode.Create))
            stream.SetLength(AbsDiagnosticCaptureFile.MaximumFileBytes + 1L);
        Assert.Contains("64 MiB", Assert.Throws<InvalidDataException>(() => AbsDiagnosticCaptureFile.Load(path)).Message);
    }

    // Small independent reader makes the CSV test check escaped field content, not just writer substrings.
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else if (!quoted && c == '\n')
            {
                cells.Add(cell.ToString().TrimEnd('\r')); cell.Clear();
                rows.Add(cells.ToArray()); cells.Clear();
            }
            else cell.Append(c);
        }
        Assert.False(quoted);
        Assert.Empty(cells);
        Assert.Equal(0, cell.Length);
        return rows;
    }
}
