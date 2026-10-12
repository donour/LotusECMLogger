using System.Collections.Concurrent;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>Test decorator: records CROs, drops or answers chosen commands, injects frames, fails reads.</summary>
internal sealed class ScriptedTransport(ICcpTransport inner) : ICcpTransport
{
    private readonly ConcurrentQueue<CcpReceivedFrame> _injected = new();
    private readonly List<byte[]> _sent = [];

    public CcpTarget Target => inner.Target;
    /// <summary>Return true to swallow a CRO, as a lost frame or silent slave would.</summary>
    public Func<byte[], bool>? Drop { get; set; }
    /// <summary>Return a CRM to answer a CRO here instead of forwarding it.</summary>
    public Func<byte[], byte[]?>? Reply { get; set; }
    public Exception? ReadFailure { get; set; }
    public bool Disposed { get; private set; }

    public IReadOnlyList<byte[]> Sent { get { lock (_sent) return _sent.ToArray(); } }
    public IEnumerable<string> SentHex => Sent.Select(Convert.ToHexString);

    public void ClearSent() { lock (_sent) _sent.Clear(); }

    public void Inject(byte[] payload, uint? canId = null) =>
        _injected.Enqueue(new(payload, 0, canId ?? inner.Target.DtoId));

    public void Send(ReadOnlySpan<byte> command)
    {
        var bytes = command.ToArray();
        lock (_sent) _sent.Add(bytes);
        if (Drop?.Invoke(bytes) == true) return;
        if (Reply?.Invoke(bytes) is { } reply) { Inject(reply); return; }
        inner.Send(bytes);
    }

    public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        if (ReadFailure is { } failure) throw failure;
        var frames = new List<CcpReceivedFrame>();
        while (_injected.TryDequeue(out var frame)) frames.Add(frame);
        frames.AddRange(inner.Read(frames.Count > 0 ? 0 : timeoutMilliseconds, cancellationToken));
        return frames;
    }

    public void AddReceiveId(uint canId) => inner.AddReceiveId(canId);
    public void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload) => inner.SendAuxiliary(canId, payload);

    public void Dispose()
    {
        Disposed = true;
        inner.Dispose();
    }

    public static byte[] Crm(byte counter, byte error = 0, params byte[] data)
    {
        var bytes = new byte[8];
        bytes[0] = 0xFF; bytes[1] = error; bytes[2] = counter;
        data.CopyTo(bytes, 3);
        return bytes;
    }
}
