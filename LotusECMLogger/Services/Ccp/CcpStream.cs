using System.Threading.Channels;

namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// A bounded queue from the CCP receive loop to one consumer. A full queue drops (and counts) items rather
/// than stalling the receive loop, so slow UI or disk work cannot delay command replies or other consumers.
/// The reader completes when the subscription ends; it completes with an exception if the transport failed.
/// </summary>
public sealed class CcpStream<T> : IDisposable
{
    private readonly Channel<T> _channel;
    private readonly Action<CcpStream<T>>? _release;
    private long _dropped;
    private int _disposed;

    internal CcpStream(int capacity, Action<CcpStream<T>>? release)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then fails instead of evicting silently
        });
        _release = release;
    }

    public ChannelReader<T> Reader => _channel.Reader;
    /// <summary>Items discarded because the consumer fell behind.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    internal void Write(T item)
    {
        if (!_channel.Writer.TryWrite(item) && !_channel.Reader.Completion.IsCompleted)
            Interlocked.Increment(ref _dropped);
    }

    internal void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);

    /// <summary>Unsubscribes locally. Owners of DAQ subscriptions should prefer their StopAsync.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release?.Invoke(this);
        Complete();
    }
}
