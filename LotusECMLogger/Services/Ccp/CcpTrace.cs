using System.Diagnostics;

namespace LotusECMLogger.Services.Ccp;

public enum CcpTraceKind
{
    Command, Reply, ErrorReply, StrayReply, Timeout, Cancelled, Event, Malformed, State,
}

/// <summary>One traced event. <see cref="Bytes"/> is the 8-byte CRO/DTO where there is one.</summary>
public readonly record struct CcpTraceEntry(long Sequence, DateTime Time, CcpTraceKind Kind, byte[]? Bytes,
    double? LatencyMs, string? Note);

/// <summary>
/// The master's recent command traffic: every CRO, its CRM (with round-trip latency), timeouts, stray replies,
/// event packets, malformed frames and state changes. DAQ frames are counted, not traced, since they arrive at
/// hundreds per second. A fixed-size ring, so tracing is always on and costs little.
/// </summary>
public sealed class CcpTrace
{
    private readonly object _lock = new();
    private readonly CcpTraceEntry[] _ring;
    private long _next;

    public CcpTrace(int capacity = 2048)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ring = new CcpTraceEntry[capacity];
    }

    public int Capacity => _ring.Length;

    public void Add(CcpTraceKind kind, ReadOnlySpan<byte> bytes, double? latencyMs = null, string? note = null)
    {
        var copy = bytes.IsEmpty ? null : bytes.ToArray();
        lock (_lock)
        {
            long sequence = ++_next;
            _ring[sequence % _ring.Length] = new CcpTraceEntry(sequence, DateTime.Now, kind, copy, latencyMs, note);
        }
    }

    /// <summary>Entries newer than <paramref name="afterSequence"/>, oldest first (at most the ring's capacity).</summary>
    public IReadOnlyList<CcpTraceEntry> Snapshot(long afterSequence = 0)
    {
        lock (_lock)
        {
            long first = Math.Max(afterSequence + 1, _next - _ring.Length + 1);
            var entries = new List<CcpTraceEntry>((int)Math.Max(0, _next - first + 1));
            for (long sequence = Math.Max(first, 1); sequence <= _next; sequence++) entries.Add(_ring[sequence % _ring.Length]);
            return entries;
        }
    }

    /// <summary>A one-line description, e.g. "SET_MTA 0x40008654" or "CRM #12 error 0x31 (command syntax)".</summary>
    public static string Describe(in CcpTraceEntry entry)
    {
        var b = entry.Bytes;
        return entry.Kind switch
        {
            CcpTraceKind.Command when b is { Length: 8 } => DescribeCommand(b),
            CcpTraceKind.Reply when b is { Length: 8 } => $"CRM #{b[2]} ok  {Convert.ToHexString(b, 3, 5)}",
            CcpTraceKind.ErrorReply when b is { Length: 8 } => $"CRM #{b[2]} error 0x{b[1]:X2} ({ErrorName(b[1])})",
            CcpTraceKind.StrayReply when b is { Length: 8 } => $"stray CRM #{b[2]} (no command waiting for it)",
            CcpTraceKind.Event when b is not null => $"event packet {Convert.ToHexString(b)}",
            CcpTraceKind.Malformed when b is not null => $"malformed DTO {Convert.ToHexString(b)}",
            _ => entry.Note ?? entry.Kind.ToString(),
        };
    }

    private static string DescribeCommand(byte[] b)
    {
        uint Address(int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        string ctr = $"#{b[1]}";
        return (CcpCommandCode)b[0] switch
        {
            CcpCommandCode.Connect => $"{ctr} CONNECT station {b[2] | b[3] << 8}",
            CcpCommandCode.Test => $"{ctr} TEST station {b[2] | b[3] << 8}",
            CcpCommandCode.Disconnect => $"{ctr} DISCONNECT {(b[2] == 1 ? "end session" : "temporary")}",
            CcpCommandCode.GetCcpVersion => $"{ctr} GET_CCP_VERSION {b[2]}.{b[3]}",
            CcpCommandCode.ExchangeId => $"{ctr} EXCHANGE_ID",
            CcpCommandCode.SetMta => $"{ctr} SET_MTA{b[2]} 0x{Address(4):X8}",
            CcpCommandCode.Upload => $"{ctr} UPLOAD {b[2]}",
            CcpCommandCode.ShortUpload => $"{ctr} SHORT_UP {b[2]} @ 0x{Address(4):X8}",
            CcpCommandCode.Download => $"{ctr} DNLOAD {b[2]}: {Convert.ToHexString(b, 3, Math.Min((int)b[2], 5))}",
            CcpCommandCode.Download6 => $"{ctr} DNLOAD_6: {Convert.ToHexString(b, 2, 6)}",
            CcpCommandCode.BuildChecksum => $"{ctr} BUILD_CHKSUM {b[4] << 8 | b[5]} bytes",
            CcpCommandCode.GetDaqSize => $"{ctr} GET_DAQ_SIZE list {b[2]}",
            CcpCommandCode.SetDaqPointer => $"{ctr} SET_DAQ_PTR list {b[2]} odt {b[3]} element {b[4]}",
            CcpCommandCode.WriteDaq => $"{ctr} WRITE_DAQ {b[2]} @ 0x{Address(4):X8}",
            CcpCommandCode.StartStop => $"{ctr} START_STOP {(CcpStartStopMode)b[2]} list {b[3]} last odt {b[4]} event {b[5]} prescaler {b[6] << 8 | b[7]}",
            CcpCommandCode.StartStopAll => $"{ctr} START_STOP_ALL {(b[2] == 1 ? "start" : "stop")}",
            _ => $"{ctr} command 0x{b[0]:X2} {Convert.ToHexString(b, 2, 6)}",
        };
    }

    public static string ErrorName(byte code) => (CcpError)code switch
    {
        CcpError.DaqInitializationRequired => "DAQ list not initialised",
        CcpError.UnknownCommand => "unknown command",
        CcpError.CommandSyntax => "command syntax",
        CcpError.ParameterOutOfRange => "parameter out of range",
        CcpError.AccessDenied => "access denied",
        _ => "unrecognised error",
    };
}

/// <summary>A snapshot of the session's counters. Latencies are CRO-sent to CRM-routed, in milliseconds.</summary>
public sealed record CcpMasterStatistics(
    long Commands, long Replies, long ErrorReplies, long Timeouts, long Cancellations, long StrayReplies,
    long EventPackets, long MalformedFrames, long DaqFrames, long UnroutedDaqFrames, long UncertainTransitions,
    double? LastLatencyMs, double? MeanLatencyMs, double? P95LatencyMs, double? MaxLatencyMs);

/// <summary>One allocated slave DAQ list and who owns it.</summary>
public sealed record CcpDaqAllocation(int SlaveList, string Owner, int EventChannel, int Prescaler, int Odts,
    byte FirstPid, long Samples, long Overruns, long Dropped, long Gaps);

/// <summary>Command round-trip latencies: running mean/max plus a window for percentiles.</summary>
internal sealed class CcpLatencyStats
{
    private readonly object _lock = new();
    private readonly double[] _window = new double[256];
    private long _count;
    private double _sum, _max, _last;

    public static double Elapsed(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    public void Add(double milliseconds)
    {
        lock (_lock)
        {
            _window[_count % _window.Length] = milliseconds;
            _count++;
            _sum += milliseconds;
            _max = Math.Max(_max, milliseconds);
            _last = milliseconds;
        }
    }

    public (double? Last, double? Mean, double? P95, double? Max) Snapshot()
    {
        lock (_lock)
        {
            if (_count == 0) return (null, null, null, null);
            var window = _window.Take((int)Math.Min(_count, _window.Length)).Order().ToArray();
            double p95 = window[Math.Min(window.Length - 1, (int)Math.Ceiling(window.Length * 0.95) - 1)];
            return (_last, _sum / _count, p95, _max);
        }
    }
}
