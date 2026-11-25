using System.Buffers.Binary;
using LotusECMLogger.Services.Ccp;
using SAE.J2534;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>One received raw CAN frame: its 11-bit ID, data bytes and the adapter's microsecond timestamp.</summary>
    public readonly record struct BrokerCanFrame(uint CanId, ReadOnlyMemory<byte> Data, uint TimestampMicroseconds);

    /// <summary>
    /// The broker's raw CAN channel, shared by CCP, ABS telemetry and the coding write. One reader polls with
    /// zero-timeout reads (as the former <c>J2534CcpTransport</c> did), every 1 ms while someone holds fast polling and every
    /// 5 ms otherwise, and routes frames by CAN ID to bounded subscriber streams. Frames with no subscriber are
    /// dropped; that includes the ISO 15765 traffic (0x7E0/0x7E8) this channel also receives when its filters pass it.
    /// </summary>
    internal sealed class SharedCanChannel : IDisposable
    {
        private const int MaxBatch = 32;
        private const uint MaxStandardId = 0x7FF;

        private readonly IJ2534Channel _channel;
        private readonly BrokerOptions _options;
        private readonly DllCallers _dll;
        // Sends and zero-timeout reads take this, so a send waits for at most one poll and never for a sleep:
        // letting sends bypass it made CCP slower on the OpenPort (59 ms against 20, 2026-10-24): its DLL serialises calls.
        private readonly object _io = new();
        private readonly object _sync = new();
        private readonly Dictionary<uint, Route> _routes = [];
        private readonly Dictionary<CcpStream<BrokerCanFrame>, List<uint>> _streamIds = [];
        private readonly Thread _reader;
        private int _fastDemand;
        private Exception? _fault;
        private volatile bool _stopping;

        public SharedCanChannel(IJ2534Channel channel, BrokerOptions options, DllCallers dll)
        {
            _channel = channel;
            _options = options;
            _dll = dll;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "J2534 broker CAN reader" };
            _reader.Start();
        }

        /// <summary>
        /// Receives frames with this ID, in arrival order, until the stream is disposed. Subscribers of one ID share
        /// its filter. <see cref="AddId"/> adds more IDs to the same stream.
        /// </summary>
        public CcpStream<BrokerCanFrame> Subscribe(uint canId)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(canId, MaxStandardId);
            lock (_sync)
            {
                ThrowIfFaulted();
                var stream = new CcpStream<BrokerCanFrame>(_options.StreamCapacity, Unsubscribe);
                _streamIds[stream] = [];
                try
                {
                    AddRoute(stream, canId);
                }
                catch
                {
                    _streamIds.Remove(stream);
                    throw;
                }
                return stream;
            }
        }

        /// <summary>Adds an ID to an existing subscription; frames of all its IDs share the one ordered stream.</summary>
        public void AddId(CcpStream<BrokerCanFrame> stream, uint canId)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(canId, MaxStandardId);
            lock (_sync)
            {
                ThrowIfFaulted();
                if (!_streamIds.TryGetValue(stream, out var ids))
                    throw new ObjectDisposedException("The CAN subscription has ended.");
                if (!ids.Contains(canId)) AddRoute(stream, canId);
            }
        }

        // Under _sync.
        private void AddRoute(CcpStream<BrokerCanFrame> stream, uint canId)
        {
            if (!_routes.TryGetValue(canId, out var route))
            {
                int filterId;
                try
                {
                    lock (_io) filterId = _channel.StartFilter(PassFilter(canId));
                }
                catch (FilterLimitException)
                {
                    throw new IOException(
                        $"The adapter cannot listen for one more CAN ID (0x{canId:X3}): its {_routes.Count} CAN filters are in use.");
                }
                _routes[canId] = route = new Route(filterId);
            }
            route.Streams = [.. route.Streams, stream];
            _streamIds[stream].Add(canId);
        }

        private void Unsubscribe(CcpStream<BrokerCanFrame> stream)
        {
            lock (_sync)
            {
                if (!_streamIds.Remove(stream, out var ids)) return;
                foreach (uint canId in ids)
                {
                    if (!_routes.TryGetValue(canId, out var route)) continue;
                    route.Streams = [.. route.Streams.Where(s => s != stream)];
                    if (route.Streams.Length > 0) continue;
                    _routes.Remove(canId);
                    if (_fault is null && !_stopping)
                    {
                        try { lock (_io) _channel.StopFilter(route.FilterId); }
                        catch (Exception) { /* the channel closes with the device; a stale filter only costs a slot */ }
                    }
                }
            }
        }

        /// <summary>The message handed to the raw CAN channel: the 11-bit ID as four bytes, then the data.</summary>
        internal static byte[] Frame(uint canId, byte[] data)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(canId, MaxStandardId);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, 8);
            return [0x00, 0x00, (byte)(canId >> 8), (byte)canId, .. data];
        }

        public void Send(uint canId, byte[] data)
        {
            byte[] message = Frame(canId, data);
            ThrowIfFaulted();
            using var counted = _dll.Enter();
            lock (_io) _channel.Send(message);
        }

        /// <summary>Polls every 1 ms instead of every 5 until disposed, for whoever is waiting on a reply.</summary>
        public IDisposable PollFast()
        {
            Interlocked.Increment(ref _fastDemand);
            return new FastPolling(this);
        }

        private void ReadLoop()
        {
            var batch = new List<ReceivedMessage>();
            while (!_stopping)
            {
                batch.Clear();
                try
                {
                    using var counted = _dll.Enter();
                    lock (_io)
                    {
                        while (true)
                        {
                            var read = _channel.Read(MaxBatch, 0);
                            batch.AddRange(read);
                            if (read.Count < MaxBatch) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_stopping) Fault(ex);
                    return;
                }

                foreach (var message in batch) RouteFrame(message);
                if (batch.Count == 0)
                    Thread.Sleep(Volatile.Read(ref _fastDemand) > 0 ? _options.CanFastPollMs : _options.CanIdlePollMs);
            }
        }

        private void RouteFrame(ReceivedMessage message)
        {
            // Our own transmissions, and 29-bit frames, are never anyone's reply.
            if ((message.RxStatus & (RxFlag.TX_MSG_TYPE | RxFlag.TX_INDICATION | RxFlag.CAN_29BIT_ID)) != 0) return;
            if (message.Data is not { Length: >= 4 and <= 12 } data) return;
            uint canId = BinaryPrimitives.ReadUInt32BigEndian(data);

            CcpStream<BrokerCanFrame>[] streams;
            lock (_sync) streams = _routes.TryGetValue(canId, out var route) ? route.Streams : [];
            if (streams.Length == 0) return; // no subscriber for this ID
            var frame = new BrokerCanFrame(canId, data.AsMemory(4).ToArray(), message.TimestampMicroseconds);
            foreach (var stream in streams) stream.Write(frame);
        }

        private void Fault(Exception ex)
        {
            CcpStream<BrokerCanFrame>[] streams;
            lock (_sync)
            {
                _fault ??= ex;
                streams = [.. _streamIds.Keys];
            }
            var error = ex is ObjectDisposedException ? null : new IOException($"The adapter stopped responding: {ex.Message}", ex);
            foreach (var stream in streams) stream.Complete(error);
        }

        private void ThrowIfFaulted()
        {
            var fault = Volatile.Read(ref _fault);
            if (fault is not null)
                throw new IOException(
                    $"The adapter stopped responding ({fault.Message}). Finish or stop what is using it so it can be reopened.", fault);
        }

        private static MessageFilter PassFilter(uint canId) => new()
        {
            FilterType = Filter.PASS_FILTER,
            Mask = [0x00, 0x00, 0x07, 0xFF],
            Pattern = [0x00, 0x00, (byte)(canId >> 8), (byte)canId],
        };

        public void Dispose()
        {
            _stopping = true;
            if (Thread.CurrentThread != _reader) _reader.Join(TimeSpan.FromSeconds(2));
            Fault(new ObjectDisposedException("The adapter was closed."));
            _channel.Dispose();
        }

        private sealed class Route(int filterId)
        {
            public int FilterId { get; } = filterId;
            // Replaced, never mutated, so the reader can use a snapshot outside the lock.
            public CcpStream<BrokerCanFrame>[] Streams { get; set; } = [];
        }

        private sealed class FastPolling(SharedCanChannel owner) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner._fastDemand);
            }
        }
    }
}
