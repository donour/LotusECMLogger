using System.Diagnostics;

namespace LotusECMLogger.Services;

/// <summary>Bounded, testable read plan. No scanning, unlocking, memory access or programming.</summary>
internal sealed class AbsDiagnosticOperations(
    Func<byte[], CancellationToken, KwpResponse> request,
    CancellationToken cancellationToken = default)
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();

    internal static readonly byte[][] BaselineRequests =
    [
        [0x10, 0x89],
        [0x1a, 0x85], [0x1a, 0x86], [0x1a, 0x87], [0x1a, 0x93], [0x1a, 0x9c],
        [0x21, 0x01], [0x21, 0xbf], [0x21, 0x04],
    ];

    public AbsDiagnosticBaseline ReadBaseline(IProgress<string>? progress = null)
    {
        var capturedUtc = DateTimeOffset.UtcNow;
        var exchanges = new List<AbsDiagnosticExchange>();
        foreach (byte[] payload in BaselineRequests)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress?.Report($"Reading ABS {Convert.ToHexString(payload)}…");
            exchanges.Add(Exchange(payload));
        }
        // A refused session is retained; the application's initial state also permits these reads.
        // There is deliberately no fallback to a programming or security session.
        return AbsDiagnosticCapture.BuildBaseline(capturedUtc, exchanges);
    }

    public AbsDiagnosticSample ReadSample(AbsDiagnosticBaseline baseline) =>
        AbsDiagnosticCapture.BuildSample(Exchange([0x21, 0x04]), baseline);

    private AbsDiagnosticExchange Exchange(byte[] payload)
    {
        KwpResponse response;
        try
        {
            response = cancellationToken.IsCancellationRequested
                ? KwpResponse.Failure("Cancelled before transmission.")
                : request(payload.ToArray(), cancellationToken);
        }
        catch (OperationCanceledException) { response = KwpResponse.Failure("Diagnostic exchange cancelled."); }
        catch (Exception error) { response = KwpResponse.Failure(error.Message); }
        return new AbsDiagnosticExchange(DateTimeOffset.UtcNow, elapsed.Elapsed.TotalMilliseconds,
            Convert.ToHexString(payload), Convert.ToHexString(response.RawResponse), response.Ok,
            response.Ok ? "" : response.DetailedError);
    }
}
