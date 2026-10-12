using System.Diagnostics;
using System.Threading.Channels;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    /// <summary>One decoded AEM reply, timestamped by the adapter's clock (shared with the CCP stream).</summary>
    public readonly record struct AemWidebandSample(double Lambda, double Afr, uint TimestampMicroseconds);

    /// <summary>
    /// AEM X-Series wideband, OBDII variant (e.g. 30-0334). The gauge answers OBD Mode 01 PID 0x24 at
    /// 0x7E1/0x7E9 and has no AEMnet broadcast, so it is polled. It is not an ECU input: it only shares the
    /// adapter and the CCP session's receive loop, as auxiliary traffic. Single CAN frame each way, so no
    /// ISO-TP; frames are padded to 8 bytes per ISO 15765-4. A silent or absent gauge produces no samples
    /// and never disturbs the CCP session.
    /// </summary>
    public sealed class AemWidebandSource : IDisposable
    {
        public const uint RequestId = 0x7E1;
        public const uint ResponseId = 0x7E9;
        public const string LambdaChannelName = "AEM Lambda";
        public const string AfrChannelName = "AEM AFR";
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50); // 20 Hz nominal

        private static readonly byte[] PollFrame = [0x02, 0x01, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00];

        private readonly CcpMaster _master;
        private readonly CcpStream<CcpReceivedFrame> _replies;
        private readonly Channel<AemWidebandSample> _samples;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _poller, _decoder;
        private long _dropped, _pollFailures;

        public AemWidebandSource(CcpMaster master, TimeSpan? pollInterval = null, int capacity = 256)
        {
            ArgumentNullException.ThrowIfNull(master);
            var interval = pollInterval ?? DefaultPollInterval;
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
            _master = master;
            PollInterval = interval;
            _samples = Channel.CreateBounded<AemWidebandSample>(new BoundedChannelOptions(capacity)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails (counted) rather than evicting
            });
            _replies = master.SubscribeAuxiliary(ResponseId);
            _decoder = Task.Run(DecodeAsync);
            _poller = Task.Run(PollAsync);
        }

        public TimeSpan PollInterval { get; }
        public ChannelReader<AemWidebandSample> Samples => _samples.Reader;
        /// <summary>Replies discarded because the consumer fell behind.</summary>
        public long Dropped => Interlocked.Read(ref _dropped) + _replies.Dropped;
        public long PollFailures => Interlocked.Read(ref _pollFailures);

        /// <summary>
        /// Decodes a raw-CAN reply payload [PCI, 0x41, 0x24, A, B, C, D, …]: lambda = (2/65536)·(A·256+B).
        /// Same decode as LiveDataReading.ParseAemUegoResponse, shifted by the PCI byte raw CAN keeps.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out double lambda)
        {
            lambda = 0;
            if (payload.Length < 7 || (payload[0] & 0xF0) != 0x00 || payload[1] != 0x41 || payload[2] != 0x24)
                return false;
            lambda = (2.0 / 65536.0) * ((payload[3] << 8) | payload[4]);
            return true;
        }

        private async Task PollAsync()
        {
            using var timer = new PeriodicTimer(PollInterval);
            try
            {
                do
                {
                    try { _master.SendAuxiliary(RequestId, PollFrame); }
                    catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                    {
                        return; // the session is gone; nothing more to poll
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: a send failure must not end the CCP session. Retry next interval.
                        Interlocked.Increment(ref _pollFailures);
                        Debug.WriteLine($"AEM: poll send failed: {ex.Message}");
                    }
                }
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
        }

        private async Task DecodeAsync()
        {
            try
            {
                await foreach (var frame in _replies.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
                    if (!TryDecode(frame.Payload.Span, out double lambda)) continue;
                    if (!_samples.Writer.TryWrite(new AemWidebandSample(lambda, lambda * 14.7, frame.TimestampMicroseconds)))
                        Interlocked.Increment(ref _dropped);
                }
                _samples.Writer.TryComplete();
            }
            catch (OperationCanceledException) { _samples.Writer.TryComplete(); }
            catch (Exception ex) { _samples.Writer.TryComplete(ex); }
        }

        public void Dispose()
        {
            if (_stop.IsCancellationRequested) return;
            _stop.Cancel();
            _replies.Dispose();
            try { Task.WaitAll([_poller, _decoder], TimeSpan.FromSeconds(1)); }
            catch (AggregateException) { }
            _samples.Writer.TryComplete();
        }
    }
}
