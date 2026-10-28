using System.Buffers.Binary;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Services;

/// <summary>One outstanding default-session read. A failure stops ABS polling for this log.</summary>
internal sealed class AbsPressureProtocol(long startMs)
{
    internal const uint RequestId = 0x6F4, ResponseId = 0x6F5;
    internal const int IntervalMs = 200, DeadlineMs = 750, FreshMs = 1000;
    internal const string ExperimentalNotice = "Experimental: ABS/traction-control operation during pressure logging has not been validated.";
    internal const string Description = "Experimental ABS pressure: direct 21 04 at up to 5 Hz; " +
        "signed LE reply[14:16] * 0.3255 bar (OEM definition). " + ExperimentalNotice;
    internal static readonly string[] Columns = ["ABS pressure (bar)", "ABS pressure raw", "ABS pressure valid", "ABS pressure age ms"];
    internal const string StatusColumn = "ABS pressure status";

    private long nextAt = startMs, requestedAt, receivedAt;
    private readonly byte[] buffer = new byte[22];
    private int used, nextSequence;
    private short? counts;
    internal bool Pending { get; private set; }
    internal bool Polling { get; private set; } = true;
    internal string Status { get; private set; } = "Waiting for pressure";

    internal byte[]? Tick(long now)
    {
        if (Pending && now - requestedAt >= DeadlineMs) Fail("21 04 response timeout");
        if (!Polling || Pending || now < nextAt) return null;
        requestedAt = now;
        nextAt = now + IntervalMs;
        used = 0;
        nextSequence = 1;
        Pending = true;
        Status = "Awaiting 61 04";
        return [0x21, 0x04];
    }

    /// <returns>CTS only for a valid first frame of our pending 22-byte response.</returns>
    internal byte[]? ReceiveCan(ReadOnlySpan<byte> frame, long now)
    {
        if (!Pending || Expired(now)) return null;
        if (frame.Length is < 2 or > 8) { Fail("Invalid ISO-TP CAN length"); return null; }
        switch (frame[0] >> 4)
        {
            case 0:
                int length = frame[0] & 15;
                if (used != 0 || length == 0 || length > frame.Length - 1) Fail("Malformed ISO-TP single frame");
                else ReceivePayload(frame.Slice(1, length), now);
                break;
            case 1:
                int total = ((frame[0] & 15) << 8) | frame[1];
                if (used != 0 || total != 22 || frame.Length != 8 || frame[2] != 0x61 || frame[3] != 0x04)
                    Fail("Expected a 22-byte 61 04 first frame");
                else
                {
                    frame[2..].CopyTo(buffer);
                    used = 6;
                    return [0x30, 0, 0, 0, 0, 0, 0, 0];
                }
                break;
            case 2:
                int take = Math.Min(7, buffer.Length - used);
                if (used == 0 || (frame[0] & 15) != nextSequence || frame.Length < take + 1)
                    Fail("ISO-TP sequence/length error");
                else
                {
                    frame.Slice(1, take).CopyTo(buffer.AsSpan(used));
                    used += take;
                    nextSequence = (nextSequence + 1) & 15;
                    if (used == buffer.Length) ReceivePayload(buffer, now);
                }
                break;
            default:
                Fail("Unexpected ISO-TP frame type");
                break;
        }
        return null;
    }

    internal void ReceivePayload(ReadOnlySpan<byte> payload, long now)
    {
        if (!Pending || Expired(now)) return;
        if (payload.Length == 3 && payload[0] == 0x7F && payload[1] == 0x21)
        {
            if (payload[2] == 0x78) Status = "Response pending"; // Original deadline still applies.
            else Fail($"21 04 rejected: NRC 0x{payload[2]:X2}");
            return;
        }
        if (payload.Length != 22 || payload[0] != 0x61 || payload[1] != 0x04)
        {
            Fail("Expected exactly 61 04 plus 20 data bytes");
            return;
        }
        counts = BinaryPrimitives.ReadInt16LittleEndian(payload[14..16]);
        receivedAt = now;
        Pending = false;
        Status = "Pressure received";
    }

    private bool Expired(long now)
    {
        if (now - requestedAt < DeadlineMs) return false;
        Fail("21 04 response timeout");
        return true;
    }

    internal void Fail(string reason)
    {
        if (!Polling) return;
        Polling = Pending = false;
        counts = null;
        Status = reason + "; ABS polling stopped";
    }

    internal AbsPressureSnapshot Snapshot(long now)
    {
        long? age = counts.HasValue ? Math.Max(0, now - receivedAt) : null;
        bool valid = counts.HasValue && age <= FreshMs;
        return new(valid ? counts : null, valid ? counts * 0.3255 : null, valid, age, Status);
    }
}

internal sealed record AbsPressureSnapshot(short? Raw, double? Bar, bool Valid, long? AgeMs, string Status)
{
    internal double[] Values => [Bar ?? double.NaN, Raw ?? double.NaN, Valid ? 1 : 0, AgeMs ?? double.NaN];

    internal void WriteTo(ISampleSink sink)
    {
        double[] values = Values;
        for (int i = 0; i < values.Length; i++) sink.Set(AbsPressureProtocol.Columns[i], values[i]);
        sink.SetText(AbsPressureProtocol.StatusColumn, Status);
    }
}
