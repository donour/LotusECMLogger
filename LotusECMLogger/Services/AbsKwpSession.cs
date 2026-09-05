using System.Diagnostics;
using SAE.J2534;

namespace LotusECMLogger.Services;

/// <summary>Primary ABS KWP over J2534 ISO-TP. The driver supplies complete diagnostic payloads.</summary>
internal sealed class AbsKwpSession : IDisposable
{
    private static readonly ECUDefinition Abs = ECUDefinition.ABS;
    private readonly J2534Session session;
    private readonly J2534Channel channel;
    private long lastActivity = Stopwatch.GetTimestamp();
    private delegate AbsResponseKind ResponseMatcher(byte[] request, byte[] response, out KwpResponse result);

    private AbsKwpSession(J2534Session session, J2534Channel channel)
    {
        this.session = session;
        this.channel = channel;
    }

    public static AbsKwpSession Open(string? driverFileName = null)
    {
        var session = J2534Session.Open(driverFileName);
        try
        {
            var channel = session.OpenIso15765();
            channel.StartMessageFilter(Abs.CreateFlowControlFilter()).ThrowIfError();
            return new AbsKwpSession(session, channel);
        }
        catch { session.Dispose(); throw; }
    }

    public byte? ActiveSession { get; private set; }
    public bool IsUnlocked { get; private set; }

    public (bool success, double volts, string error) MeasureBatteryVoltage() => session.MeasureBatteryVoltage();

    public KwpResponse Request(params byte[] payload) => Request(payload, CancellationToken.None);

    public KwpResponse Request(byte[] payload, CancellationToken cancellationToken)
        => Request(payload, cancellationToken, 5000);

    public KwpResponse Request(byte[] payload, CancellationToken cancellationToken, int timeoutMs)
    {
        return RequestCore(payload, cancellationToken, timeoutMs, AbsKwpResponseMatcher.Match, false);
    }

    /// <summary>Runs one exchange through the programming response matcher.</summary>
    public KwpResponse RequestProgramming(byte[] payload, CancellationToken cancellationToken, int timeoutMs)
    {
        return RequestCore(payload, cancellationToken, timeoutMs, AbsProgrammingResponseMatcher.Match, true);
    }

    private KwpResponse RequestCore(byte[] payload, CancellationToken cancellationToken, int timeoutMs,
        ResponseMatcher matcher, bool programming)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (timeoutMs is < 100 or > 10000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        if (payload.Length == 0)
            throw new ArgumentException("A diagnostic request must contain a service byte.", nameof(payload));
        if (cancellationToken.IsCancellationRequested)
            return KwpResponse.Failure("Cancelled before transmission.");
        // A prior response or TesterPresent reply cannot satisfy the next exchange.
        channel.ClearRxBuffer().ThrowIfError();
        byte[] outgoingMessage = [.. Abs.GetRequestHeader(), .. payload];
        if (!programming)
            channel.SendMessage(outgoingMessage).ThrowIfError();
        else
            channel.SendMessage(outgoingMessage, TxFlag.NONE, outgoingMessage.Length <= 50 ? 400 : 40000).ThrowIfError();
        lastActivity = Stopwatch.GetTimestamp();
        long started = lastActivity;
        byte[] lastObserved = [];
        bool pending = false;
        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < timeoutMs)
        {
            if (cancellationToken.IsCancellationRequested)
                return KwpResponse.Failure("Diagnostic exchange cancelled.", lastObserved);
            GetMessagesResult received;
            try { received = channel.ReadMessages(16, 100); }
            catch (Exception error) { return KwpResponse.Failure(error.Message, lastObserved); }
            if (received.Status != ResultCode.STATUS_NOERROR && received.Status != ResultCode.TIMEOUT
                && received.Status != ResultCode.BUFFER_EMPTY)
                return KwpResponse.Failure($"J2534 receive failed: {received.Status}", lastObserved);
            foreach (var receivedMessage in received.Messages)
            {
                if ((receivedMessage.RxStatus & (RxFlag.TX_MSG_TYPE | RxFlag.TX_INDICATION | RxFlag.START_OF_MESSAGE)) != 0)
                    continue;
                byte[] data = receivedMessage.Data;
                if (data.Length < 5 || !Abs.MatchesResponse(data))
                    continue;
                lastObserved = data[4..];
                var kind = matcher(payload, lastObserved, out var result);
                if (kind == AbsResponseKind.Ignore)
                    continue;
                lastActivity = Stopwatch.GetTimestamp();
                if (kind == AbsResponseKind.Pending)
                {
                    pending = true;
                    continue; // The monotonic total deadline remains bounded.
                }
                return result;
            }
        }
        return KwpResponse.Failure(pending ? $"ABS responsePending did not finish within {timeoutMs} ms."
            : $"No matching ABS response within {timeoutMs} ms.", lastObserved);
    }

    public (bool ok, string detail, byte session) EnterSession(params byte[] candidates)
    {
        string error = "No session requested.";
        foreach (byte candidate in candidates)
        {
            var response = Request(0x10, candidate);
            if (response.Ok)
            {
                ActiveSession = candidate;
                IsUnlocked = false;
                return (true, $"Session 0x{candidate:X2} accepted", candidate);
            }
            error = response.DetailedError;
        }
        return (false, error, 0);
    }

    /// <summary>Application-only two-byte XOR5220 exchange. Ordinary baseline/live reads do not invoke it.</summary>
    public (bool ok, string detail) TryUnlock()
    {
        var seedReply = Request(0x27, 0x01);
        if (!seedReply.Ok)
            return (false, seedReply.DetailedError);
        if (seedReply.Payload.Length != 3 || seedReply.Payload[0] != 1)
            return (false, "Expected exactly 67 01 seedHigh seedLow.");
        byte[] seed = seedReply.Payload[1..];
        if (seed[0] == 0 && seed[1] == 0)
        {
            IsUnlocked = true;
            return (true, "Application already unlocked.");
        }
        byte[] key = AbsProtocol.ComputeKey(seed);
        var keyReply = Request(0x27, 0x02, key[0], key[1]);
        if (!keyReply.Ok)
            return (false, keyReply.DetailedError);
        if (!keyReply.Payload.AsSpan().SequenceEqual(new byte[] { 0x02, 0x34 }))
            return (false, "Expected exactly 67 02 34; unlock not confirmed.");
        IsUnlocked = true;
        return (true, "Application security accepted.");
    }

    public void KeepAlive(CancellationToken cancellationToken = default)
    {
        if (Stopwatch.GetElapsedTime(lastActivity).TotalSeconds < 2)
            return;
        var reply = Request([0x3e], cancellationToken); // OEM sends one byte, without UDS subfunction80.
        cancellationToken.ThrowIfCancellationRequested();
        if (!reply.Ok)
            throw new IOException($"ABS keep-alive failed: {reply.DetailedError}");
    }

    public void Dispose() => session.Dispose();
}
