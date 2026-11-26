using System.Collections.Concurrent;
using System.Globalization;
using LotusECMLogger.Services.Broker;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>One frame seen on the bus.</summary>
    /// <param name="TimeMs">Milliseconds since the monitor started, from the adapter's hardware timestamps.</param>
    public readonly record struct CanBusFrame(double TimeMs, uint Id, bool Extended, byte[] Data)
    {
        public string IdText => Extended ? $"{Id:X8}" : $"{Id:X3}";
        public string DataText => string.Join(" ", Data.Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Passive capture of every frame on the CAN bus, 11- and 29-bit, for the CAN Monitor tab. It holds the whole
    /// adapter (a pass-all filter on a channel of its own), as the ABS bus sniff does, so nothing else can use the
    /// adapter while it runs. It never transmits.
    /// </summary>
    public sealed class CanBusMonitor : IDisposable
    {
        internal const string Holder = "the CAN bus monitor";

        private readonly J2534Broker _broker;
        private readonly object _lock = new();
        private readonly ConcurrentQueue<CanBusFrame> _frames = new();
        private BrokerLease? _lease;
        private Thread? _thread;
        private CancellationTokenSource? _cts;
        private long _dropped;

        /// <summary>Most frames held between drains; beyond this new frames are dropped and counted.</summary>
        public int QueueCapacity { get; init; } = 200_000;

        /// <summary>Raised on the reader thread when the adapter fails; the monitor has stopped.</summary>
        public event EventHandler<string>? Failed;

        public CanBusMonitor() : this(J2534Broker.Shared) { }

        public CanBusMonitor(J2534Broker broker) => _broker = broker ?? throw new ArgumentNullException(nameof(broker));

        public bool IsRunning { get { lock (_lock) return _thread is not null; } }

        /// <summary>Frames dropped because nobody drained the queue in time.</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        /// <summary>Starts capturing. Throws <see cref="BrokerBusyException"/> while anything else uses the adapter.</summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_thread is not null) return;
                var lease = _broker.Acquire(LeaseKind.DeviceExclusive, Holder);
                try
                {
                    var channel = lease.OpenExclusiveChannel(BrokerProtocol.CanWithExtendedIds);
                    channel.StartFilter(new MessageFilter
                    {
                        FilterType = Filter.PASS_FILTER,
                        Mask = [0x00, 0x00, 0x00, 0x00],
                        Pattern = [0x00, 0x00, 0x00, 0x00],
                    });
                    _frames.Clear();
                    Interlocked.Exchange(ref _dropped, 0);
                    _lease = lease;
                    _cts = new CancellationTokenSource();
                    var token = _cts.Token;
                    _thread = new Thread(() => ReadLoop(channel, token)) { IsBackground = true, Name = "CAN bus monitor" };
                    _thread.Start();
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
        }

        /// <summary>Stops capturing and releases the adapter. Frames already captured stay available to <see cref="Drain"/>.</summary>
        public void Stop()
        {
            Thread? thread;
            lock (_lock)
            {
                thread = _thread;
                _cts?.Cancel();
            }
            if (thread is not null && thread != Thread.CurrentThread) thread.Join(2000);
            ReleaseLease();
        }

        /// <summary>Takes every frame captured since the last call, oldest first.</summary>
        public List<CanBusFrame> Drain()
        {
            var list = new List<CanBusFrame>();
            while (_frames.TryDequeue(out var frame)) list.Add(frame);
            return list;
        }

        private void ReadLoop(IJ2534Channel channel, CancellationToken token)
        {
            uint? first = null;
            uint last = 0;
            double elapsedUs = 0;
            string? error = null;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var m in channel.Read(IJ2534Channel.MaxReadMessages, 20))
                    {
                        if (m.Data is not { Length: >= 4 } data) continue;
                        // Unsigned difference survives the 32-bit microsecond counter wrapping (~71 minutes).
                        if (first is null) { first = m.TimestampMicroseconds; last = first.Value; }
                        elapsedUs += unchecked(m.TimestampMicroseconds - last);
                        last = m.TimestampMicroseconds;

                        uint id = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                        bool extended = (m.RxStatus & RxFlag.CAN_29BIT_ID) != 0;
                        if (_frames.Count >= QueueCapacity)
                        {
                            Interlocked.Increment(ref _dropped);
                            continue;
                        }
                        _frames.Enqueue(new CanBusFrame(elapsedUs / 1000.0, id, extended, data[4..]));
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                error = ex.Message;
            }
            catch
            {
                // Stopping closed the channel under the read.
            }
            finally
            {
                lock (_lock) _thread = null;
                if (error is not null)
                {
                    ReleaseLease();
                    Failed?.Invoke(this, error);
                }
            }
        }

        private void ReleaseLease()
        {
            BrokerLease? lease;
            lock (_lock)
            {
                lease = _lease;
                _lease = null;
                _cts?.Dispose();
                _cts = null;
            }
            lease?.Dispose();
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// The arbitration-ID filter of the CAN Monitor: hex IDs and ranges separated by commas or spaces
    /// ("7E0, 7E8-7EF, 0x100"), each optionally prefixed with ! to hide it. Empty shows everything; a filter of only
    /// exclusions shows everything else.
    /// </summary>
    public sealed class CanIdFilter
    {
        private readonly List<(uint Lo, uint Hi)> _include = [];
        private readonly List<(uint Lo, uint Hi)> _exclude = [];

        public static readonly CanIdFilter All = new();

        public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

        public bool Matches(uint id) =>
            (_include.Count == 0 || _include.Any(r => id >= r.Lo && id <= r.Hi)) &&
            !_exclude.Any(r => id >= r.Lo && id <= r.Hi);

        /// <summary>Parses <paramref name="text"/>; on failure <paramref name="error"/> names the bad term.</summary>
        public static bool TryParse(string? text, out CanIdFilter filter, out string? error)
        {
            filter = new CanIdFilter();
            error = null;
            foreach (var raw in (text ?? "").Split([',', ' ', ';', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                string term = raw;
                bool exclude = term.StartsWith('!');
                if (exclude) term = term[1..];

                string[] ends = term.Split('-');
                if (ends.Length is < 1 or > 2 || !TryHex(ends[0], out uint lo) || !TryHex(ends[^1], out uint hi) || hi < lo)
                {
                    error = $"'{raw}' is not a hex ID or range (e.g. 7E8, 700-7FF, !0A2)";
                    filter = All;
                    return false;
                }
                (exclude ? filter._exclude : filter._include).Add((lo, hi));
            }
            return true;
        }

        private static bool TryHex(string s, out uint value)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            return uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) &&
                   s.Length is > 0 and <= 8 && value <= 0x1FFFFFFF;
        }
    }
}
