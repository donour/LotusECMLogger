using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LotusECMLogger.Services;

public sealed record AbsDiagnosticExchange(DateTimeOffset TimestampUtc, double ElapsedMilliseconds,
    string RequestHex, string ResponseHex, bool Success, string Error);

public sealed record AbsDiagnosticBaseline(DateTimeOffset CapturedUtc,
    IReadOnlyList<AbsDiagnosticExchange> Exchanges, string FirmwareReference, IReadOnlyList<AbsReportRow> Rows);

public sealed record AbsDiagnosticSample(DateTimeOffset TimestampUtc, double ElapsedMilliseconds,
    AbsDiagnosticExchange Exchange, AbsLiveRecord? Data, IReadOnlyList<AbsReportRow> Rows);

public sealed record AbsDiagnosticCaptureDocument
{
    public int SchemaVersion { get; init; } = 1;
    public AbsDiagnosticBaseline? Baseline { get; init; }
    public List<AbsDiagnosticSample> Samples { get; init; } = [];
    public string Notes { get; init; } = "";
}

/// <summary>Derives display data from full diagnostic payloads. Never sends a request.</summary>
public static class AbsDiagnosticCapture
{
    public const string ReferenceName = "BB68638_V0201";
    internal const int MaximumExchanges = 64;
    internal const int MaximumTextLength = 16_384;
    internal const int MaximumHexLength = 16_384;

    public static AbsDiagnosticBaseline BuildBaseline(DateTimeOffset capturedUtc,
        IEnumerable<AbsDiagnosticExchange> exchanges)
    {
        ArgumentNullException.ThrowIfNull(exchanges);
        var raw = exchanges.Take(MaximumExchanges + 1).Select(ValidateExchange).ToArray();
        if (raw.Length > MaximumExchanges)
            throw new InvalidDataException($"A baseline may contain at most {MaximumExchanges} exchanges.");
        bool matches = MatchesIdentity(raw);
        var rows = new List<AbsReportRow>
        {
            new("Firmware reference", matches ? ReferenceName : "unknown",
                matches ? "Reported build and part match the reference; this does not verify the stock image hash or active RAM profile."
                        : "Reported build and part do not establish the BB68638 reference. Live scales are withheld.")
        };
        foreach (var exchange in raw)
        {
            rows.Add(new($"Request {exchange.RequestHex}", exchange.ResponseHex,
                exchange.Success ? "Raw complete diagnostic reply" : $"Request failed: {exchange.Error}"));
            if (!exchange.Success) continue;
            byte[] request = ParseHex(exchange.RequestHex);
            byte[] response = ParseHex(exchange.ResponseHex);
            try
            {
                if (request.AsSpan().SequenceEqual(new byte[] { 0x21, 0x01 }))
                    rows.AddRange(AbsDiagnosticDecoder.DecodeCoding(response, matches).Rows);
                else if (request.AsSpan().SequenceEqual(new byte[] { 0x21, 0xBF }))
                    rows.AddRange(AbsDiagnosticDecoder.DecodeProcess(response).Rows);
            }
            catch (ArgumentException error)
            {
                rows.Add(new($"Decode {exchange.RequestHex}", "unavailable", error.Message));
            }
        }
        return new(capturedUtc.ToUniversalTime(), Array.AsReadOnly(raw),
            matches ? ReferenceName : "unknown", rows.AsReadOnly());
    }

    public static AbsDiagnosticSample BuildSample(AbsDiagnosticExchange exchange,
        AbsDiagnosticBaseline? baseline)
    {
        exchange = ValidateExchange(exchange);
        var rows = new List<AbsReportRow>
        {
            new("Request", exchange.RequestHex), new("Response", exchange.ResponseHex),
            new("Communication", exchange.Success ? "success" : "failed", exchange.Error)
        };
        AbsLiveRecord? data = null;
        if (!exchange.Success)
            rows.Add(new("Live record", "unavailable", "The failed exchange is retained; no previous sample is carried forward."));
        else if (baseline is null || !MatchesIdentity(baseline.Exchanges.Select(ValidateExchange)))
            rows.Add(new("Live record", "unverified reference", "Raw reply retained. BB68638 scales require matching reported build and part; a serialized reference label is not sufficient."));
        else if (!ParseHex(exchange.RequestHex).AsSpan().SequenceEqual(new byte[] { 0x21, 0x04 }))
            rows.Add(new("Live record", "unsupported request", "Expected the complete 21 04 diagnostic request."));
        else
        {
            try
            {
                data = AbsDiagnosticDecoder.DecodeLiveRecord(ParseHex(exchange.ResponseHex));
                rows.AddRange(data.Rows);
            }
            catch (ArgumentException error)
            {
                rows.Add(new("Live record", "decode failed", error.Message));
            }
        }
        return new(exchange.TimestampUtc, exchange.ElapsedMilliseconds, exchange, data, rows.AsReadOnly());
    }

    private static bool MatchesIdentity(IEnumerable<AbsDiagnosticExchange> exchanges)
    {
        // Conflicting or failed repeated identity reads must not be hidden by choosing a favorable one.
        var builds = new List<byte[]>();
        var parts = new List<byte[]>();
        int count = 0;
        foreach (var exchange in exchanges)
        {
            if (++count > MaximumExchanges) throw new InvalidDataException("Too many baseline exchanges.");
            byte[] request = ParseHex(exchange.RequestHex);
            bool build = request.AsSpan().SequenceEqual(new byte[] { 0x1A, 0x85 });
            bool part = request.AsSpan().SequenceEqual(new byte[] { 0x1A, 0x87 });
            if (!build && !part) continue;
            if (!exchange.Success) return false;
            (build ? builds : parts).Add(ParseHex(exchange.ResponseHex));
        }
        return builds.Count > 0 && parts.Count > 0 &&
               builds.All(build => parts.All(part => AbsDiagnosticDecoder.MatchesBb68638Identity(build, part)));
    }

    internal static AbsDiagnosticExchange ValidateExchange(AbsDiagnosticExchange exchange)
    {
        if (exchange is null) throw new InvalidDataException("An exchange is missing.");
        if (!double.IsFinite(exchange.ElapsedMilliseconds) || exchange.ElapsedMilliseconds < 0)
            throw new InvalidDataException("Elapsed milliseconds must be finite and nonnegative.");
        ValidateText(exchange.Error, "Exchange error");
        if (exchange.RequestHex is null || exchange.ResponseHex is null ||
            exchange.RequestHex.Length > MaximumHexLength || exchange.ResponseHex.Length > MaximumHexLength)
            throw new InvalidDataException("Diagnostic hex is missing or too long.");
        if (ParseHex(exchange.RequestHex).Length == 0)
            throw new InvalidDataException("A complete diagnostic request is required.");
        ParseHex(exchange.ResponseHex); // An empty response is valid for a timeout.
        return exchange with { TimestampUtc = exchange.TimestampUtc.ToUniversalTime() };
    }

    internal static byte[] ParseHex(string text)
    {
        try
        {
            string compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            return Convert.FromHexString(compact);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException("Diagnostic payload contains malformed hex.", error);
        }
    }

    internal static void ValidateText(string text, string name)
    {
        if (text is null || text.Length > MaximumTextLength)
            throw new InvalidDataException($"{name} is missing or exceeds {MaximumTextLength} characters.");
    }
}

/// <summary>Append-only JSONL with a baseline header and one flushed record for every poll, including failures.</summary>
public sealed class AbsDiagnosticCaptureWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly AbsDiagnosticBaseline _baseline;
    private double _lastElapsed = -1;
    private int _count;
    private bool _disposed;
    private bool _faulted;

    public AbsDiagnosticCaptureWriter(string path, AbsDiagnosticBaseline baseline, string notes = "")
    {
        ArgumentNullException.ThrowIfNull(baseline);
        AbsDiagnosticCapture.ValidateText(notes, "Notes");
        _baseline = AbsDiagnosticCapture.BuildBaseline(baseline.CapturedUtc, baseline.Exchanges);
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        try
        {
            WriteRecord(new { schemaVersion = 1, kind = "baseline", baseline = _baseline, notes });
        }
        catch
        {
            try { _writer.Dispose(); }
            finally { _stream.Dispose(); }
            throw;
        }
    }

    public void Append(AbsDiagnosticSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted) throw new IOException("The capture writer failed previously; start a new capture file.");
            if (_count >= AbsDiagnosticCaptureFile.MaximumSamples)
                throw new InvalidDataException("Capture sample limit reached; start a new capture file.");
            var canonical = AbsDiagnosticCapture.BuildSample(sample.Exchange, _baseline);
            if (canonical.ElapsedMilliseconds < _lastElapsed)
                throw new InvalidDataException("Sample elapsed milliseconds must be monotonic.");
            try
            {
                WriteRecord(new { schemaVersion = 1, kind = "sample", sample = canonical });
                _lastElapsed = canonical.ElapsedMilliseconds;
                _count++;
            }
            catch (IOException) { _faulted = true; throw; }
        }
    }

    private void WriteRecord<T>(T record)
    {
        string json = JsonSerializer.Serialize(record, AbsDiagnosticCaptureFile.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > AbsDiagnosticCaptureFile.MaximumRecordBytes)
            throw new InvalidDataException("Capture record exceeds the size limit.");
        if (_stream.Position + Encoding.UTF8.GetByteCount(json) + 1 > AbsDiagnosticCaptureFile.MaximumFileBytes)
            throw new InvalidDataException("Capture file limit reached; start a new capture file.");
        _writer.Write(json);
        _writer.Write('\n');
        _writer.Flush();
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); }
            finally { _stream.Dispose(); }
        }
    }
}

public static class AbsDiagnosticCaptureFile
{
    public const int MaximumSamples = 100_000;
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    public const int MaximumRecordBytes = 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32
    };

    public static void SaveBaseline(string path, AbsDiagnosticBaseline baseline, string notes = "")
    {
        ArgumentNullException.ThrowIfNull(baseline);
        AbsDiagnosticCapture.ValidateText(notes, "Notes");
        var canonical = AbsDiagnosticCapture.BuildBaseline(baseline.CapturedUtc, baseline.Exchanges);
        string json = JsonSerializer.Serialize(new AbsDiagnosticCaptureDocument
            { Baseline = canonical, Notes = notes }, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumRecordBytes)
            throw new InvalidDataException("Baseline exceeds the size limit.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(json);
    }

    /// <summary>Loads baseline JSON or a complete JSONL journal. Serialized derived fields are ignored.</summary>
    public static AbsDiagnosticCaptureDocument Load(string path)
    {
        string text = ReadBounded(path);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Capture file is empty.");
        JsonDocument? whole = null;
        try { whole = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { /* More than one JSON value can be a journal; validate every line below. */ }
        if (whole is not null)
        {
            using (whole)
            {
                if (whole.RootElement.ValueKind == JsonValueKind.Object &&
                    !whole.RootElement.TryGetProperty("kind", out _))
                    return ReadDocument(whole.RootElement);
            }
        }
        if (!text.EndsWith('\n'))
            throw new InvalidDataException("Capture journal is truncated: the final record lacks its newline terminator.");
        using var lines = new StringReader(text);
        AbsDiagnosticCaptureDocument? result = null;
        int lineNumber = 0;
        double lastElapsed = -1;
        string? line;
        while ((line = lines.ReadLine()) is not null)
        {
            lineNumber++;
            if (Encoding.UTF8.GetByteCount(line) > MaximumRecordBytes)
                throw new InvalidDataException($"Capture journal line {lineNumber} exceeds the record limit.");
            try
            {
                using var parsed = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
                var root = parsed.RootElement;
                CheckSchema(root);
                string kind = RequiredString(root, "kind");
                if (lineNumber == 1)
                {
                    if (kind != "baseline") throw new InvalidDataException("The first journal record must be a baseline.");
                    result = new() { Baseline = ReadBaseline(Required(root, "baseline")), Notes = ReadNotes(root) };
                }
                else
                {
                    if (kind != "sample") throw new InvalidDataException("Only sample records may follow the baseline.");
                    AddSample(result!, ReadSample(Required(root, "sample"), result!.Baseline), ref lastElapsed);
                }
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or FormatException)
            {
                throw new InvalidDataException($"Capture journal line {lineNumber}: {error.Message}", error);
            }
        }
        return result ?? throw new InvalidDataException("Capture journal has no baseline.");
    }

    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Capture file exceeds the 64 MiB limit.");
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int count;
        while ((count = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + count > MaximumFileBytes) throw new InvalidDataException("Capture file exceeds the 64 MiB limit.");
            buffer.Write(chunk, 0, count);
        }
        try
        {
            string text = new UTF8Encoding(false, true).GetString(buffer.ToArray());
            return text.StartsWith('\uFEFF') ? text[1..] : text;
        }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Capture file is not valid UTF-8.", error); }
    }

    private static AbsDiagnosticCaptureDocument ReadDocument(JsonElement root)
    {
        try
        {
            CheckSchema(root);
            var baseline = Required(root, "baseline");
            var result = new AbsDiagnosticCaptureDocument
            {
                Baseline = baseline.ValueKind == JsonValueKind.Null ? null : ReadBaseline(baseline),
                Notes = ReadNotes(root)
            };
            var samples = Required(root, "samples");
            if (samples.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Samples must be an array.");
            double lastElapsed = -1;
            foreach (var sample in samples.EnumerateArray())
                AddSample(result, ReadSample(sample, result.Baseline), ref lastElapsed);
            return result;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Invalid capture document: {error.Message}", error);
        }
    }

    private static void CheckSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !Required(root, "schemaVersion").TryGetInt32(out int version) || version != 1)
            throw new InvalidDataException("Unsupported capture schema; expected schemaVersion 1.");
        // Reject ambiguous raw values rather than relying on JSON's last-property-wins behavior.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate property '{property.Name}'.");
    }

    private static JsonElement Required(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
            throw new InvalidDataException($"Required property '{name}' is missing.");
        int count = 0;
        foreach (var property in root.EnumerateObject())
            if (property.Name == name && ++count > 1) throw new InvalidDataException($"Duplicate property '{name}'.");
        return value;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"'{name}' must be a string.");
        return value.GetString()!;
    }

    private static string ReadNotes(JsonElement root)
    {
        string notes = RequiredString(root, "notes");
        AbsDiagnosticCapture.ValidateText(notes, "Notes");
        return notes;
    }

    private static AbsDiagnosticBaseline ReadBaseline(JsonElement baseline)
    {
        DateTimeOffset captured = Required(baseline, "capturedUtc").GetDateTimeOffset();
        var array = Required(baseline, "exchanges");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > AbsDiagnosticCapture.MaximumExchanges)
            throw new InvalidDataException("Baseline exchanges must be an array within the exchange limit.");
        return AbsDiagnosticCapture.BuildBaseline(captured, array.EnumerateArray().Select(ReadExchange));
    }

    private static AbsDiagnosticExchange ReadExchange(JsonElement exchange) => AbsDiagnosticCapture.ValidateExchange(new(
        Required(exchange, "timestampUtc").GetDateTimeOffset(), Required(exchange, "elapsedMilliseconds").GetDouble(),
        RequiredString(exchange, "requestHex"), RequiredString(exchange, "responseHex"),
        Required(exchange, "success").GetBoolean(), RequiredString(exchange, "error")));

    private static AbsDiagnosticSample ReadSample(JsonElement sample, AbsDiagnosticBaseline? baseline) =>
        AbsDiagnosticCapture.BuildSample(ReadExchange(Required(sample, "exchange")), baseline);

    private static void AddSample(AbsDiagnosticCaptureDocument document, AbsDiagnosticSample sample, ref double lastElapsed)
    {
        if (document.Samples.Count >= MaximumSamples) throw new InvalidDataException("Capture sample limit exceeded.");
        if (sample.ElapsedMilliseconds < lastElapsed) throw new InvalidDataException("Sample elapsed milliseconds are not monotonic.");
        lastElapsed = sample.ElapsedMilliseconds;
        document.Samples.Add(sample);
    }

    /// <summary>Long CSV: raw exchange plus one field per row, with unavailable numeric values left blank.</summary>
    public static void ExportCsv(string path, AbsDiagnosticCaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1 || document.Samples is null || document.Samples.Count > MaximumSamples)
            throw new InvalidDataException("Unsupported capture document or sample count.");
        AbsDiagnosticCapture.ValidateText(document.Notes, "Notes");
        var baseline = document.Baseline is null ? null :
            AbsDiagnosticCapture.BuildBaseline(document.Baseline.CapturedUtc, document.Baseline.Exchanges);
        // Validate before creating an export. Rebuild derived fields even for an in-memory document.
        var samples = new List<AbsDiagnosticSample>();
        double previous = -1;
        foreach (var sample in document.Samples)
        {
            if (sample is null) throw new InvalidDataException("A sample is missing.");
            var rebuilt = AbsDiagnosticCapture.BuildSample(sample.Exchange, baseline);
            if (rebuilt.ElapsedMilliseconds < previous) throw new InvalidDataException("Sample elapsed milliseconds are not monotonic.");
            previous = rebuilt.ElapsedMilliseconds;
            samples.Add(rebuilt);
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        WriteCsv(writer, "RecordType", "TimestampUtc", "ElapsedMilliseconds", "RequestHex", "ResponseHex", "Success", "Error", "FirmwareReference", "Field", "Raw", "Value", "Unit", "Status", "Detail");
        string reference = baseline?.FirmwareReference ?? "unknown";
        WriteCsv(writer, "notes", "", "", "", "", "", "", reference, "Notes", "", "", "", "", document.Notes);
        if (baseline is not null)
        {
            foreach (var exchange in baseline.Exchanges)
                WriteExchangeRow(writer, "baseline_exchange", exchange, reference, "exchange", exchange.ResponseHex, "", "",
                    exchange.Success ? "communication_success" : "transport_failed", exchange.Error);
            foreach (var row in baseline.Rows)
                WriteCsv(writer, "baseline_field", Timestamp(baseline.CapturedUtc), "", "", "", "", "", reference,
                    row.Field, "", row.Value, "", "decoded_baseline", row.Detail);
        }
        foreach (var sample in samples)
        {
            var exchange = sample.Exchange;
            if (sample.Data is not { } data)
            {
                WriteExchangeRow(writer, "sample", exchange, reference, "live_record", exchange.ResponseHex, "", "",
                    exchange.Success ? "not_decoded" : "transport_failed", string.Join("; ", sample.Rows.Select(r => $"{r.Field}: {r.Value} {r.Detail}")));
                continue;
            }
            foreach (var wheel in data.Wheels)
                WriteExchangeRow(writer, "sample", exchange, reference, wheel.Name, Number(wheel.Raw),
                    wheel.Kph.HasValue ? Number(wheel.Kph.Value) : "", "km/h", wheel.Status, "");
            foreach (var channel in new[] { data.YawRate, data.Pressure, data.LongitudinalAcceleration })
                WriteExchangeRow(writer, "sample", exchange, reference, channel.Name, Number(channel.Raw), Number(channel.Value),
                    channel.Unit, channel.SourceCounts is null ? "outside_verified_conversion" : "numeric_reply", "");
            foreach (var channel in new[] { data.BrakeLightSwitch, data.Battery })
                WriteExchangeRow(writer, "sample", exchange, reference, channel.Name, Number(channel.Raw), Number(channel.Volts), "V", "numeric_reply", "");
            foreach (string observation in data.Observations)
                WriteExchangeRow(writer, "sample", exchange, reference, "observation", "", "", "", "consistency_observation", observation);
        }
    }

    private static void WriteExchangeRow(StreamWriter writer, string type, AbsDiagnosticExchange exchange,
        string reference, string field, string raw, string value, string unit, string status, string detail) =>
        WriteCsv(writer, type, Timestamp(exchange.TimestampUtc), Number(exchange.ElapsedMilliseconds), exchange.RequestHex,
            exchange.ResponseHex, exchange.Success ? "true" : "false", exchange.Error, reference, field, raw, value, unit, status, detail);

    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void WriteCsv(StreamWriter writer, params string[] cells) => writer.WriteLine(string.Join(",", cells.Select(cell =>
        cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell)));
}
