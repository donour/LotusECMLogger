namespace LotusECMLogger.Services.Ccp;

// Auxiliary traffic: frames on non-CCP IDs that share the adapter (e.g. the AEM wideband's OBD poll).
// Independent of the CCP session state, so it works before CONNECT and while the session is uncertain.
public sealed partial class CcpMaster
{
    private readonly object _auxiliaryLock = new();
    private readonly Dictionary<uint, List<CcpStream<CcpReceivedFrame>>> _auxiliary = new();

    /// <summary>Admits <paramref name="canId"/> and delivers its frames, with hardware timestamps, to the stream.</summary>
    public CcpStream<CcpReceivedFrame> SubscribeAuxiliary(uint canId)
    {
        ThrowIfUnusable();
        _transport.AddReceiveId(canId);
        var stream = new CcpStream<CcpReceivedFrame>(_options.StreamCapacity, s =>
        {
            lock (_auxiliaryLock)
                if (_auxiliary.TryGetValue(canId, out var list)) list.Remove(s);
        });
        lock (_auxiliaryLock)
        {
            if (!_auxiliary.TryGetValue(canId, out var list)) _auxiliary[canId] = list = [];
            list.Add(stream);
        }
        return stream;
    }

    /// <summary>Sends a frame to a non-CCP ID. Not serialised with CCP commands: it cannot disturb slave state.</summary>
    public void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload)
    {
        ThrowIfUnusable();
        _transport.SendAuxiliary(canId, payload);
    }

    private void ThrowIfUnusable()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_state == CcpSessionState.Disposed, this);
            if (_state == CcpSessionState.Faulted)
                throw new InvalidOperationException($"The CCP transport failed ({_stateReason}).", _fault);
        }
    }

    private void RouteAuxiliary(in CcpReceivedFrame frame)
    {
        lock (_auxiliaryLock)
        {
            if (!_auxiliary.TryGetValue(frame.CanId, out var list)) return;
            foreach (var stream in list) stream.Write(frame);
        }
    }

    private void EndAllAuxiliary(Exception? error)
    {
        List<CcpStream<CcpReceivedFrame>> streams;
        lock (_auxiliaryLock)
        {
            streams = _auxiliary.Values.SelectMany(l => l).ToList();
            _auxiliary.Clear();
        }
        foreach (var stream in streams) stream.Complete(error);
    }
}
