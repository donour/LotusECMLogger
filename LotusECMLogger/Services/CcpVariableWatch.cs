using LotusECMLogger.Models;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    /// <summary>The latest sample of one watched variable.</summary>
    public readonly record struct CcpWatchValue(double Value, string RawHex, uint TimestampMicroseconds, long Updates);

    /// <summary>
    /// Watches variables live through a DAQ subscription on the shared session ("Variable watch" in the DAQ
    /// allocation view), packed by the high-speed logger's planner. It shares the slave's DAQ lists with the logger:
    /// starting it costs the logger a possible gap, and it fails cleanly when no list is free.
    /// </summary>
    public sealed class CcpVariableWatch : IAsyncDisposable
    {
        public const string OwnerName = "Variable watch";

        private readonly CcpDaqSubscription _subscription;
        private readonly LoggingPlan _plan;
        private readonly Dictionary<HighSpeedChannel, int> _index;
        private readonly CcpWatchValue?[] _values;
        private readonly Task _pump;
        private readonly object _lock = new();

        private CcpVariableWatch(CcpDaqSubscription subscription, LoggingPlan plan, Dictionary<HighSpeedChannel, int> index,
            IReadOnlyList<CcpVariable> variables)
        {
            (_subscription, _plan, _index, Variables) = (subscription, plan, index, variables);
            _values = new CcpWatchValue?[variables.Count];
            _pump = Task.Run(PumpAsync);
        }

        public IReadOnlyList<CcpVariable> Variables { get; }
        public int RateHz { get; private init; }
        public long Dropped => _subscription.Stream.Dropped;
        public bool IsActive => _subscription.IsActive;

        public static async Task<CcpVariableWatch> StartAsync(CcpMaster master, IReadOnlyList<CcpVariable> variables, int rateHz,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            if (variables is null || variables.Count == 0) throw new ArgumentException("Choose at least one variable to watch.", nameof(variables));
            if (variables.FirstOrDefault(v => !v.Watchable) is { } large)
                throw new ArgumentException($"{large.Name} is {large.Size} bytes; only 1, 2 and 4-byte values can be watched. Read it instead.");
            var channels = variables.Select(v => v.ToChannel()).ToList();
            var index = new Dictionary<HighSpeedChannel, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < channels.Count; i++) index[channels[i]] = i;
            var plan = HighSpeedLogPlanner.Plan(channels.Select(c => (c, rateHz)).ToList(), master.Target);
            var subscription = await master.StartDaqAsync(HighSpeedLogPlanner.ToDaqLists(plan), OwnerName, cancellationToken)
                .ConfigureAwait(false);
            return new CcpVariableWatch(subscription, plan, index, variables) { RateHz = rateHz };
        }

        /// <summary>The latest value of each variable, aligned with <see cref="Variables"/> (null until first sampled).</summary>
        public IReadOnlyList<CcpWatchValue?> Snapshot()
        {
            lock (_lock) return _values.ToArray();
        }

        private async Task PumpAsync()
        {
            await foreach (var sample in _subscription.Stream.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var frame = _plan.Groups[sample.List].Frames[sample.Odt];
                var payload = sample.Data.Span;
                lock (_lock)
                {
                    foreach (var planned in frame.Channels)
                    {
                        var channel = planned.Channel;
                        var raw = payload.Slice(planned.ByteOffset, channel.Size);
                        int i = _index[channel];
                        long updates = (_values[i]?.Updates ?? 0) + 1;
                        _values[i] = new CcpWatchValue(channel.Decode(raw), Convert.ToHexString(raw), sample.TimestampMicroseconds, updates);
                    }
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            try { await _subscription.StopAsync(cancellationToken).ConfigureAwait(false); }
            finally { await _pump.ConfigureAwait(false); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Variable watch: stop failed: {ex.Message}"); }
        }
    }
}
