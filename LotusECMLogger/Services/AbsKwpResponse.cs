namespace LotusECMLogger.Services;

/// <summary>One exchange. RawResponse includes the SID; Payload retains the legacy SID-stripped view.</summary>
internal readonly record struct KwpResponse(bool Ok, string Error, byte[] Payload, byte Nrc)
{
    public byte[] RawResponse { get; init; } = [];

    public static KwpResponse Positive(byte[] fullResponse) =>
        new(true, "", fullResponse[1..], 0) { RawResponse = fullResponse.ToArray() };

    public static KwpResponse Negative(byte[] fullResponse) =>
        new(false, $"NRC 0x{fullResponse[2]:X2} ({AbsProtocol.NrcName(fullResponse[2])})", [], fullResponse[2])
        { RawResponse = fullResponse.ToArray() };

    public static KwpResponse Failure(string error, byte[]? rawResponse = null) =>
        new(false, error, [], 0) { RawResponse = rawResponse?.ToArray() ?? [] };

    public string DetailedError => Nrc == 0 || AbsProtocol.NrcHint(Nrc).Length == 0
        ? Error : $"{Error} — {AbsProtocol.NrcHint(Nrc)}";
}

internal enum AbsResponseKind { Ignore, Pending, Complete }

/// <summary>Matches reassembled diagnostic payloads, never raw CAN/ISO-TP frames.</summary>
internal static class AbsKwpResponseMatcher
{
    public static AbsResponseKind Match(byte[] request, byte[] response, out KwpResponse result)
    {
        result = default;
        if (request.Length == 0 || response.Length == 0)
            return AbsResponseKind.Ignore;
        if (response[0] == 0x7f)
        {
            if (response.Length < 2 || response[1] != request[0])
                return AbsResponseKind.Ignore;
            if (response.Length != 3)
            {
                result = KwpResponse.Failure("Malformed negative response.", response);
                return AbsResponseKind.Complete;
            }
            result = KwpResponse.Negative(response);
            return response[2] == 0x78 ? AbsResponseKind.Pending : AbsResponseKind.Complete;
        }
        if (response[0] != (request[0] | 0x40))
            return AbsResponseKind.Ignore;
        if (request[0] == 0x17 && request.Length == 3 &&
            (response.Length < 4 || response[1] != request[1] || response[2] != request[2]))
            return AbsResponseKind.Ignore;
        if (request[0] is 0x10 or 0x1a or 0x21 or 0x27 or 0x31 or 0x32 or 0x33)
        {
            if (request.Length < 2 || response.Length < 2)
            {
                result = KwpResponse.Failure("Missing echoed diagnostic identifier.", response);
                return AbsResponseKind.Complete;
            }
            if (response[1] != request[1])
                return AbsResponseKind.Ignore;
        }
        if ((request[0] is 0x31 or 0x32 && response.Length != 2) ||
            (request[0] == 0x33 && response.Length < 3))
        {
            result = KwpResponse.Failure("Unexpected primary routine response length.", response);
            return AbsResponseKind.Complete;
        }
        result = KwpResponse.Positive(response);
        return AbsResponseKind.Complete;
    }
}

/// <summary>Programming matcher kept separate from the diagnostic matcher because 71 replies carry routine data.</summary>
internal static class AbsProgrammingResponseMatcher
{
    public static AbsResponseKind Match(byte[] request, byte[] response, out KwpResponse result)
    {
        result = default;
        if (request.Length == 0 || response.Length == 0) return AbsResponseKind.Ignore;
        if (response[0] == 0x7f)
        {
            if (response.Length < 2 || response[1] != request[0]) return AbsResponseKind.Ignore;
            if (response.Length != 3) { result = KwpResponse.Failure("Malformed negative response.", response); return AbsResponseKind.Complete; }
            result = KwpResponse.Negative(response);
            return response[2] == 0x78 ? AbsResponseKind.Pending : AbsResponseKind.Complete;
        }
        if (response[0] != (byte)(request[0] | 0x40)) return AbsResponseKind.Ignore;
        if (request.Length > 1 && request[0] is 0x10 or 0x22 or 0x27 or 0x2e or 0x31)
        {
            if (response.Length < 2 || response[1] != request[1]) return AbsResponseKind.Ignore;
        }
        result = KwpResponse.Positive(response);
        return AbsResponseKind.Complete;
    }
}
