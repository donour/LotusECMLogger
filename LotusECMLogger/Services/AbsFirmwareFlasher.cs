using System.Security.Cryptography;

namespace LotusECMLogger.Services;

public sealed record AbsFlashOptions
{
    public double MinimumBatteryVoltage { get; init; } = 12.0;
    public bool ConfirmUnresolvedIntegrity { get; init; }
    public string ExpectedImageSha256 { get; init; } = "";
    public string DriverFileName { get; init; } = "";
}

public sealed record AbsFlashProgress
{
    public string Phase { get; init; } = "";
    public int BlockNumber { get; init; }
    public int BlockCount { get; init; }
    public int BytesSent { get; init; }
    public int TotalBytes { get; init; }
    public string ImageSha256 { get; init; } = "";
}

public sealed record AbsFlashExchange(string RequestHex, string ResponseHex, bool Success, string Error,
    DateTimeOffset TimestampUtc);

public sealed record AbsFlashResult
{
    public bool Completed { get; init; }
    public bool Cancelled { get; init; }
    public int BlocksSent { get; init; }
    public int BytesSent { get; init; }
    public double BatteryVoltage { get; init; }
    public string ImageSha256 { get; init; } = "";
    public string AuditLogPath { get; init; } = "";
    public string IntegrityWarning { get; init; } = AbsFirmwareFlasher.IntegrityWarning;
    public IReadOnlyList<AbsFlashExchange> Exchanges { get; init; } = [];
    public IReadOnlyList<AbsReportRow> Rows { get; init; } = [];
}

/// <summary>Exact OEM ABS boot programming flow, separated from the diagnostic response matcher.</summary>
internal sealed class AbsFirmwareFlasher
{
    internal const int BlockSize = 256;
    internal const int SetupTimeoutMs = 6000;
    internal const int TransferTimeoutMs = 500;
    internal const int InterBlockDelayMs = 25;
    internal const string IntegrityWarning = "The captured simulator accepted positive replies only; the separate 128-byte trailer and recovery behavior remain unresolved. Checksum relationships do not prove real ECU acceptance.";

    private readonly Func<byte[], CancellationToken, int, KwpResponse> request;
    private readonly Func<(bool success, double volts, string error)> measureVoltage;
    private readonly Action<int> delay;
    private readonly Action<AbsFlashExchange>? exchangeAudit;
    private readonly bool enforceProductionGeometry;

    internal AbsFirmwareFlasher(Func<byte[], CancellationToken, int, KwpResponse> request,
        Func<(bool success, double volts, string error)> measureVoltage,
        Action<int>? delay = null, Action<AbsFlashExchange>? exchangeAudit = null, bool enforceProductionGeometry = true)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.measureVoltage = measureVoltage ?? throw new ArgumentNullException(nameof(measureVoltage));
        this.delay = delay ?? Thread.Sleep;
        this.exchangeAudit = exchangeAudit;
        this.enforceProductionGeometry = enforceProductionGeometry;
    }

    internal AbsFlashResult Flash(AbsFirmwareImage image, AbsFlashOptions options,
        IProgress<AbsFlashProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        var exchanges = new List<AbsFlashExchange>();
        int blocks = 0;
        int bytes = 0;
        double batteryVoltage = 0;
        AbsFirmwareImage? snapshotImage = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] snapshot = image.Bytes.ToArray();
            snapshotImage = image with { Bytes = snapshot };
            if ((ulong)snapshot.Length != (ulong)image.EndAddressExclusive - image.StartAddress)
                return Failed("The image byte count does not equal its manifest address span.", snapshotImage, exchanges);
            string actualHash = Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
            image.Manifest.ValidateAgainst(actualHash, image.StartAddress, image.EndAddressExclusive);
            if (!options.ConfirmUnresolvedIntegrity)
                return Failed("Flashing requires explicit acknowledgement that image acceptance and recovery remain unresolved.", snapshotImage, exchanges);
            if (enforceProductionGeometry && (image.StartAddress != 0x8000 || image.EndAddressExclusive != 0xBFFF0))
                return Failed("The ABS production image must cover exactly 0x00008000–0x000BFFEF; the image was not sent.", snapshotImage, exchanges);
            if (options.MinimumBatteryVoltage is < 12 or > 16 || !double.IsFinite(options.MinimumBatteryVoltage))
                return Failed("Battery-voltage minimum must be a finite value between 12 and 16 V.", snapshotImage, exchanges);
            if (!string.Equals(actualHash, image.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(options.ExpectedImageSha256) && !string.Equals(options.ExpectedImageSha256, actualHash, StringComparison.OrdinalIgnoreCase))
                return Failed("The selected firmware changed after preview; reload it before flashing.", snapshotImage, exchanges);
            var voltage = measureVoltage();
            if (!voltage.success)
                return Failed($"Battery-voltage preflight failed: {voltage.error}", snapshotImage, exchanges);
            batteryVoltage = voltage.volts;
            if (!double.IsFinite(voltage.volts) || voltage.volts < options.MinimumBatteryVoltage)
                return Failed($"Battery voltage {voltage.volts:F2} V is below the {options.MinimumBatteryVoltage:F2} V programming minimum.", snapshotImage, exchanges, voltage.volts);

            progress?.Report(new AbsFlashProgress { Phase = "Entering ABS programming session", ImageSha256 = snapshotImage.Sha256, TotalBytes = snapshotImage.Bytes.Count });
            Expect([0x10, 0x85], [0x50, 0x85], exchanges, cancellationToken);
            Expect([0x22, 0xf1, 0x86], [0x62, 0xf1, 0x86, 0x02], exchanges, cancellationToken);

            var seedReply = ExpectPrefix([0x27, 0x11], [0x67, 0x11], 6, exchanges, cancellationToken);
            byte[] seed = seedReply.RawResponse[2..6];
            if (seed.Any(value => value != 0))
            {
                byte[] key = AbsProtocol.ComputeBootloaderKey(seed);
                Expect([0x27, 0x12, .. key], [0x67, 0x12], exchanges, cancellationToken);
            }
            Expect([0x2e, 0xf1, 0x5a, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], [0x6e, 0xf1, 0x5a], exchanges, cancellationToken);
            Expect([0x31, 0x01, 0xff, 0x00, 0x01, 0x01], [0x71, 0x01, 0xff, 0x00, 0x01, 0x01], exchanges, cancellationToken);
            Expect([0x34, 0x00, 0x01, 0x01], [0x74, 0x20, 0x01, 0x00], exchanges, cancellationToken);

            int totalBlocks = snapshotImage.BlockCount;
            byte counter = 1;
            foreach (ReadOnlyMemory<byte> block in snapshotImage.Blocks())
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte expectedCounter = counter;
                byte[] payload = [0x36, expectedCounter, .. block.ToArray()];
                KwpResponse response = Send(payload, exchanges, cancellationToken, TransferTimeoutMs);
                if (!response.Ok || response.RawResponse.Length != 2 || response.RawResponse[0] != 0x76 || response.RawResponse[1] != expectedCounter)
                {
                    MarkLastFailure(exchanges, $"Expected exactly 76 {expectedCounter:X2}; got {Convert.ToHexString(response.RawResponse)} ({response.DetailedError}).");
                    AuditLast(exchanges);
                    throw new InvalidDataException($"Block {blocks + 1} counter 0x{expectedCounter:X2} was not acknowledged with exactly 76 {expectedCounter:X2}.");
                }
                AuditLast(exchanges);
                blocks++;
                bytes += block.Length;
                progress?.Report(new AbsFlashProgress { Phase = "Transferring ABS firmware", BlockNumber = blocks, BlockCount = totalBlocks, BytesSent = bytes, TotalBytes = snapshotImage.Bytes.Count, ImageSha256 = snapshotImage.Sha256 });
                counter = unchecked((byte)(counter + 1));
                delay(InterBlockDelayMs);
            }
            Expect([0x37], [0x77], exchanges, cancellationToken);
            Expect([0x31, 0x01, 0xff, 0x01], [0x71, 0x01, 0xff, 0x01], exchanges, cancellationToken);
            Expect([0x31, 0x01, 0x02, 0x02], [0x71, 0x01, 0x02, 0x02], exchanges, cancellationToken);
            return new AbsFlashResult { Completed = true, BlocksSent = blocks, BytesSent = bytes, BatteryVoltage = voltage.volts, ImageSha256 = snapshotImage.Sha256, Exchanges = exchanges, IntegrityWarning = IntegrityWarning, Rows = BuildRows(snapshotImage, voltage.volts, blocks, bytes) };
        }
        catch (OperationCanceledException)
        {
            return Failed("Flashing cancelled; recovery behavior is not established.", snapshotImage ?? image, exchanges, batteryVoltage, blocks, bytes) with { Cancelled = true, BlocksSent = blocks, BytesSent = bytes };
        }
        catch (Exception error)
        {
            return Failed(error.Message, snapshotImage ?? image, exchanges, batteryVoltage, blocks, bytes) with { BlocksSent = blocks, BytesSent = bytes };
        }
    }

    private KwpResponse Expect(byte[] payload, byte[] expected, List<AbsFlashExchange> exchanges, CancellationToken token) {
        KwpResponse response = Send(payload, exchanges, token, SetupTimeoutMs);
        if (!response.Ok || !response.RawResponse.AsSpan().SequenceEqual(expected)) { MarkLastFailure(exchanges, $"Expected {Convert.ToHexString(expected)}; got {Convert.ToHexString(response.RawResponse)} ({response.DetailedError})."); AuditLast(exchanges); throw new InvalidDataException($"ABS rejected {Convert.ToHexString(payload)}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(response.RawResponse)} ({response.DetailedError})."); }
        AuditLast(exchanges);
        return response;
    }

    private KwpResponse ExpectPrefix(byte[] payload, byte[] prefix, int exactLength, List<AbsFlashExchange> exchanges, CancellationToken token) {
        KwpResponse response = Send(payload, exchanges, token, SetupTimeoutMs);
        if (!response.Ok || response.RawResponse.Length != exactLength || response.RawResponse.Length < prefix.Length || !response.RawResponse.AsSpan(0, prefix.Length).SequenceEqual(prefix)) { MarkLastFailure(exchanges, $"Expected {Convert.ToHexString(prefix)} with length {exactLength}; got {Convert.ToHexString(response.RawResponse)} ({response.DetailedError})."); AuditLast(exchanges); throw new InvalidDataException($"ABS returned malformed bootloader seed response: {Convert.ToHexString(response.RawResponse)}."); }
        AuditLast(exchanges);
        return response;
    }

    private KwpResponse Send(byte[] payload, List<AbsFlashExchange> exchanges, CancellationToken token, int timeout) {
        token.ThrowIfCancellationRequested();
        KwpResponse response;
        try { response = request(payload, token, timeout); }
        catch (Exception error) { response = KwpResponse.Failure(error.Message); }
        var exchange = new AbsFlashExchange(Convert.ToHexString(payload), Convert.ToHexString(response.RawResponse), response.Ok, response.Ok ? "" : response.DetailedError, DateTimeOffset.UtcNow);
        exchanges.Add(exchange);
        return response;
    }

    private void AuditLast(List<AbsFlashExchange> exchanges)
    {
        if (exchanges.Count > 0) exchangeAudit?.Invoke(exchanges[^1]);
    }

    private static void MarkLastFailure(List<AbsFlashExchange> exchanges, string error)
    {
        if (exchanges.Count == 0) return;
        exchanges[^1] = exchanges[^1] with { Success = false, Error = error };
    }

    private static AbsFlashResult Failed(string error, AbsFirmwareImage image, List<AbsFlashExchange> exchanges, double voltage = 0, int blocks = 0, int bytes = 0) =>
        new() { ImageSha256 = image.Sha256, BatteryVoltage = voltage, BlocksSent = blocks, BytesSent = bytes, Exchanges = exchanges, IntegrityWarning = IntegrityWarning, Rows = BuildRows(image, voltage, blocks, bytes).Append(new AbsReportRow("Error", error)).ToArray() };

    private static IReadOnlyList<AbsReportRow> BuildRows(AbsFirmwareImage image, double voltage, int blocks, int bytes) =>
        [new("Image SHA-256", image.Sha256), new("Address range", $"0x{image.StartAddress:X8}–0x{image.EndAddressExclusive - 1:X8}"), new("Image bytes", image.Bytes.Count.ToString()), new("Bytes sent", bytes.ToString()), new("Blocks sent", blocks.ToString()), new("Battery voltage", voltage == 0 ? "unmeasured" : $"{voltage:F2} V"), new("Integrity", "UNRESOLVED — " + IntegrityWarning)];

}
