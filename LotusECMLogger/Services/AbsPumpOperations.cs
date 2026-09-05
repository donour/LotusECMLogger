using System.Diagnostics;

namespace LotusECMLogger.Services;

/// <summary>BB68638 OEM MRA relay test. A finished routine leaves the relay latched;
/// OFF and StopRoutine are separate operations, neither is inferred from a finished ON result.</summary>
internal sealed class AbsPumpOperations
{
    private readonly Func<byte[], CancellationToken, KwpResponse> request;
    private readonly Func<double> clock;
    private readonly Action<int, CancellationToken> wait;
    private readonly Action<AbsDiagnosticExchange>? journal;
    private readonly List<AbsDiagnosticExchange> exchanges = [];
    private readonly List<string> errors = [];
    private bool used;

    // Exact OEM MatchIx100/PID6 MRA payloads. Zero durations do NOT automatically turn the relay off.
    internal static byte[] PumpOn => [0x31, 0x06, 0xff, 0x22, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    internal static byte[] PumpOff => [0x31, 0x06, 0x00, 0x22, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    internal const int MaximumSeconds = 5; // New host restriction, not an OEM thermal rating.

    public AbsPumpOperations(Func<byte[], CancellationToken, KwpResponse> request,
        Action<AbsDiagnosticExchange>? journal = null, Func<double>? clock = null,
        Action<int, CancellationToken>? wait = null)
    {
        this.request = request;
        this.journal = journal;
        var elapsed = Stopwatch.StartNew();
        this.clock = clock ?? (() => elapsed.Elapsed.TotalMilliseconds);
        this.wait = wait ?? ((ms, token) =>
        {
            if (token.WaitHandle.WaitOne(ms)) token.ThrowIfCancellationRequested();
        });
    }

    public AbsRoutineResult Run(int seconds, bool operatorConfirmed,
        IProgress<AbsRoutineProgress>? progress, CancellationToken token)
    {
        if (used) throw new InvalidOperationException("A pump operation instance cannot be reused.");
        used = true;
        bool sessionAttempted = false, activationAttempted = false, mayHaveActivated = false;
        bool offCompleted = false, stopConfirmed = false, sessionRestored = false, cancelled = false;
        bool pulseFinished = false;
        try
        {
            if (seconds is < 1 or > MaximumSeconds)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Choose 1–5 seconds for the pump test.");
            if (!operatorConfirmed)
                throw new InvalidOperationException("Confirm vehicle stationary, engine off and ignition on before the pump test.");
            token.ThrowIfCancellationRequested();
            Report("Checking firmware identity");
            var build = Exchange([0x1a, 0x85], token);
            var part = Exchange([0x1a, 0x87], token);
            if (!build.Ok || !part.Ok || !AbsDiagnosticDecoder.MatchesBb68638Identity(build.RawResponse, part.RawResponse))
                throw new IOException("The reported firmware identity does not match the verified BB68638 V0201 pump test.");

            token.ThrowIfCancellationRequested();
            sessionAttempted = true;
            RequireExact(Exchange([0x10, 0x89], token), [0x50, 0x89], "Tester session");
            var live = Exchange([0x21, 0x04], token);
            if (!live.Ok) throw new IOException($"Live-data check failed: {live.DetailedError}");
            var values = AbsDiagnosticDecoder.DecodeLiveRecord(live.RawResponse);
            if (values.Wheels.Any(w => w.Raw != 0))
                throw new IOException("Every wheel must report a non-fault zero before this test. Zero alone does not prove the vehicle is stationary.");

            token.ThrowIfCancellationRequested();
            Report("Starting pump motor test");
            double start = clock(), end = start + seconds * 1000;
            // Arm cleanup BEFORE the potentially transmitted command, including a lost positive reply.
            activationAttempted = mayHaveActivated = true;
            var on = Exchange(PumpOn, token, received: response =>
            {
                // Preserve a definite refusal even if writing its journal entry subsequently fails.
                if (!response.Ok && response.Nrc is not (0 or 0x78)) mayHaveActivated = false;
            });
            RequireExact(on, [0x71, 0x06], "Pump ON");
            WaitForResult(2, Math.Min(end, clock() + 1500), token, cleanup: false);
            double lastPoll = clock();
            while (clock() < end)
            {
                token.ThrowIfCancellationRequested();
                Report("Pump test running — Stop sends OFF and cancels the routine", (clock() - start) / 1000);
                wait((int)Math.Clamp(end - clock(), 1, 100), token);
                // Polling also maintains the session. Avoid starting an 800 ms read at the end of the hold.
                if (end - clock() >= 800 && clock() - lastPoll >= 1000)
                {
                    RequireResult(Exchange([0x33, 0x06], token), 2);
                    lastPoll = clock();
                }
            }
            token.ThrowIfCancellationRequested();
            pulseFinished = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        catch (Exception error)
        {
            cancelled = token.IsCancellationRequested;
            errors.Add(error.Message);
        }
        finally
        {
            if (mayHaveActivated)
            {
                // Each cleanup stage gets an independent live token. A failed OFF must not suppress Stop.
                Cleanup("Sending pump OFF", cleanupToken =>
                {
                    RequireExact(Exchange(PumpOff, cleanupToken, true), [0x71, 0x06], "Pump OFF");
                    WaitForResult(2, clock() + 1500, cleanupToken, true);
                    offCompleted = true;
                });
                Cleanup("Stopping actuator routine", cleanupToken =>
                {
                    RequireExact(Exchange([0x32, 0x06], cleanupToken, true), [0x72, 0x06], "Stop routine");
                    WaitForResult(7, clock() + 1500, cleanupToken, true);
                    stopConfirmed = true;
                });
            }
            if (sessionAttempted)
                Cleanup("Restoring default diagnostic session", cleanupToken =>
                {
                    RequireExact(Exchange([0x10, 0x81], cleanupToken, true), [0x50, 0x81], "Default session");
                    sessionRestored = true;
                });
        }

        var rows = new List<AbsReportRow>
        {
            new("Pump ON", activationAttempted ? "attempted" : "not attempted"),
            new("Pump OFF command", offCompleted ? "completed" : mayHaveActivated ? "unconfirmed" : "not required"),
            new("Stop routine", stopConfirmed ? "accepted" : mayHaveActivated ? "unconfirmed" : "not required",
                "Stop acknowledgement can precede deferred cleanup. The result is not physical motor feedback."),
            new("Default session", sessionRestored ? "restored" : sessionAttempted ? "unconfirmed" : "not changed"),
            new("Requested duration", $"{seconds} s", "Host timing target; driver latency can delay shutdown. Software limit is not a thermal rating."),
            new("Required firmware reference", "BB68638 V0201", "Activation requires matching reported identity; this does not verify the installed image hash."),
        };
        if (cancelled) rows.Add(new("Operation", "cancelled", "Cleanup still runs after cancellation."));
        rows.AddRange(errors.Select(e => new AbsReportRow("Error", e)));
        rows.AddRange(exchanges.Select(e => new AbsReportRow($"Request {e.RequestHex}", e.ResponseHex,
            e.Success ? $"{e.ElapsedMilliseconds:F0} ms" : e.Error)));
        return new AbsRoutineResult
        {
            Completed = pulseFinished && offCompleted && stopConfirmed && sessionRestored && errors.Count == 0 && !cancelled,
            Cancelled = cancelled, ActivationAttempted = activationAttempted, OffCommandCompleted = offCompleted,
            CleanupRequired = mayHaveActivated,
            StopConfirmed = stopConfirmed, SessionRestored = sessionRestored,
            Rows = rows.AsReadOnly(), Exchanges = exchanges.AsReadOnly(),
        };

        void Report(string phase, double elapsedSeconds = 0) => progress?.Report(new AbsRoutineProgress
            { Phase = phase, ElapsedSeconds = elapsedSeconds, TotalSeconds = seconds });
        void Cleanup(string phase, Action<CancellationToken> action)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                // Progress callbacks must never prevent a shutdown transmission.
                try { Report(phase); } catch { }
                action(cleanup.Token);
            }
            catch (Exception error) { errors.Add($"{phase}: {error.Message}"); }
        }
    }

    private KwpResponse Exchange(byte[] payload, CancellationToken token, bool cleanup = false,
        Action<KwpResponse>? received = null)
    {
        token.ThrowIfCancellationRequested();
        KwpResponse response;
        try { response = request(payload, token); }
        catch (Exception error) { response = KwpResponse.Failure(error.Message); }
        if (response.Ok)
        {
            var kind = AbsKwpResponseMatcher.Match(payload, response.RawResponse, out var matched);
            response = kind == AbsResponseKind.Complete ? matched
                : KwpResponse.Failure("Reply does not match the pump-test request.", response.RawResponse);
        }
        var exchange = new AbsDiagnosticExchange(DateTimeOffset.UtcNow, clock(), Convert.ToHexString(payload),
            Convert.ToHexString(response.RawResponse), response.Ok, response.Ok ? "" : response.DetailedError);
        exchanges.Add(exchange);
        received?.Invoke(response);
        try { journal?.Invoke(exchange); }
        catch (Exception error)
        {
            if (!cleanup) throw new IOException($"Pump journal failed: {error.Message}", error);
            errors.Add($"Pump journal failed during cleanup: {error.Message}");
        }
        return response;
    }

    private void WaitForResult(byte expected, double deadline, CancellationToken token, bool cleanup)
    {
        while (clock() < deadline)
        {
            token.ThrowIfCancellationRequested();
            var result = Exchange([0x33, 0x06], token, cleanup);
            if (result.Ok && result.RawResponse.AsSpan().SequenceEqual(new byte[] { 0x73, 0x06, 0x01 }))
            {
                wait(50, token);
                continue;
            }
            RequireResult(result, expected);
            return;
        }
        throw new IOException($"Routine 06 result 0x{expected:X2} was not confirmed before the deadline.");
    }

    private static void RequireExact(KwpResponse response, byte[] expected, string operation)
    {
        if (!response.Ok || !response.RawResponse.AsSpan().SequenceEqual(expected))
            throw new IOException($"{operation} not acknowledged: " + (response.Ok
                ? $"expected {Convert.ToHexString(expected)}, received {Convert.ToHexString(response.RawResponse)}"
                : response.DetailedError));
    }

    private static void RequireResult(KwpResponse response, byte expected)
    {
        if (!response.Ok || response.RawResponse.Length != 11 || response.RawResponse[0] != 0x73 ||
            response.RawResponse[1] != 6 || response.RawResponse[2] != expected)
            throw new IOException($"Expected complete routine 06 status 0x{expected:X2}; received " +
                (response.Ok ? Convert.ToHexString(response.RawResponse) : response.DetailedError));
    }
}
