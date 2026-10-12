namespace LotusECMLogger.Services.Ccp;

/// <summary>One DAQ element: 1, 2 or 4 bytes at a non-zero address (zero ends an ODT on the slave).</summary>
public readonly record struct CcpDaqElement(uint Address, int Size);

/// <summary>
/// One DAQ list as a consumer wants it: event channel, prescaler and its ODTs, each ODT up to seven
/// elements totalling at most seven bytes. The master picks which slave list carries it.
/// </summary>
public sealed record CcpDaqListRequest(int EventChannel, int Prescaler, IReadOnlyList<IReadOnlyList<CcpDaqElement>> Odts);

/// <summary>
/// One received ODT. <see cref="List"/> indexes the consumer's request, not the slave list. <see cref="Data"/>
/// holds the seven bytes after the PID; elements are packed in request order at their sizes.
/// </summary>
public readonly record struct CcpDaqSample(int List, int Odt, ReadOnlyMemory<byte> Data, bool Overrun,
    uint TimestampMicroseconds);

/// <summary>A consumer's lease on slave DAQ lists. Stop it to stop only its own lists.</summary>
public sealed class CcpDaqSubscription
{
    private readonly CcpMaster _master;
    private long _gaps;

    private readonly long[] _samples, _overruns;

    internal CcpDaqSubscription(CcpMaster master, string owner, IReadOnlyList<CcpDaqListRequest> requests, int[] lists,
        int capacity)
    {
        _master = master;
        Owner = owner;
        Requests = requests;
        SlaveLists = lists;
        Stream = new CcpStream<CcpDaqSample>(capacity, null);
        _samples = new long[lists.Length];
        _overruns = new long[lists.Length];
    }

    /// <summary>Who asked for these lists, for the DAQ allocation view.</summary>
    public string Owner { get; }
    public long SamplesFor(int list) => Interlocked.Read(ref _samples[list]);
    public long OverrunsFor(int list) => Interlocked.Read(ref _overruns[list]);

    internal void RecordSample(int list, bool overrun)
    {
        Interlocked.Increment(ref _samples[list]);
        if (overrun) Interlocked.Increment(ref _overruns[list]);
    }

    public IReadOnlyList<CcpDaqListRequest> Requests { get; }
    /// <summary>The slave DAQ list carrying each request.</summary>
    public IReadOnlyList<int> SlaveLists { get; }
    /// <summary>First PID of each list as reported by GET_DAQ_SIZE.</summary>
    public IReadOnlyList<byte> FirstPids { get; internal set; } = [];
    public CcpStream<CcpDaqSample> Stream { get; }
    /// <summary>
    /// Times another consumer started DAQ while this one streamed. GET_DAQ_SIZE resets the slave's shared
    /// transmit queue, so each start may discard frames already queued for this subscription.
    /// </summary>
    public long Gaps => Interlocked.Read(ref _gaps);
    public bool IsActive { get; internal set; }
    public string? EndReason { get; internal set; }

    internal void RecordGap() => Interlocked.Increment(ref _gaps);

    /// <summary>START_STOP stop on this subscription's lists only, then release them.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => _master.StopDaqAsync(this, cancellationToken);
}

public sealed partial class CcpMaster
{
    private readonly object _daqLock = new();
    private CcpDaqSubscription?[]? _listOwners;
    private readonly DaqRoute?[] _routes = new DaqRoute?[0x80];
    // Lists whose slave state is unknown (a configuration or stop failed). Re-initialised with GET_DAQ_SIZE
    // before the next START_STOP_ALL, which would otherwise start a half-configured prepared list.
    private readonly HashSet<int> _dirtyLists = [];
    private long _unroutedDaq, _daqFrames;

    private sealed record DaqRoute(CcpDaqSubscription Subscription, int List, int Odt);

    /// <summary>DAQ frames whose PID belongs to no active subscription.</summary>
    public long UnroutedDaqFrames => Interlocked.Read(ref _unroutedDaq);

    private CcpDaqSubscription?[] ListOwners => _listOwners ??= new CcpDaqSubscription?[Target.DaqListCount];

    /// <summary>
    /// Allocates free slave lists (lowest first) and programs them: per list GET_DAQ_SIZE, SET_DAQ_PTR/WRITE_DAQ
    /// per element (elements 0–6 only) and START_STOP prepare; then START_STOP_ALL starts them together.
    /// Lists of other subscriptions keep running but may lose queued frames (see <see cref="CcpDaqSubscription.Gaps"/>).
    /// </summary>
    public Task<CcpDaqSubscription> StartDaqAsync(IReadOnlyList<CcpDaqListRequest> lists, string? owner = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDaqRequest(lists);
        return RunConnectedAsync("start DAQ", async ct =>
        {
            CcpDaqSubscription subscription;
            List<CcpDaqSubscription> others;
            int[] dirty;
            lock (_daqLock)
            {
                var owners = ListOwners;
                var free = Enumerable.Range(0, owners.Length).Where(i => owners[i] is null).Take(lists.Count).ToArray();
                if (free.Length < lists.Count)
                    throw new InvalidOperationException(
                        $"{lists.Count} DAQ list(s) requested but only {free.Length} of {owners.Length} are free.");
                subscription = new CcpDaqSubscription(this, owner ?? "unnamed", lists, free, _options.StreamCapacity);
                foreach (int list in free) owners[list] = subscription;
                others = owners.Where(o => o is not null && o != subscription).Distinct().Cast<CcpDaqSubscription>().ToList();
                dirty = _dirtyLists.Except(free).Order().ToArray();
            }
            var firstPids = new byte[lists.Count];
            try
            {
                for (int i = 0; i < lists.Count; i++)
                {
                    int list = subscription.SlaveLists[i];
                    var request = lists[i];
                    var size = (await ExchangeAsync(c => CcpFrame.GetDaqSize(c, Target, list), ct).ConfigureAwait(false)).Data.Span;
                    if (i == 0) foreach (var other in others) other.RecordGap();
                    lock (_daqLock) _dirtyLists.Add(list); // configured but not yet started
                    int odtCount = size[0];
                    byte firstPid = size[1];
                    if (odtCount < request.Odts.Count || firstPid + request.Odts.Count > Target.DaqListCount * Target.OdtsPerList)
                        throw new CcpProtocolException($"GET_DAQ_SIZE for list {list} returned {odtCount} ODT(s) from PID {firstPid}.");
                    firstPids[i] = firstPid;
                    for (int odt = 0; odt < request.Odts.Count; odt++)
                    {
                        var elements = request.Odts[odt];
                        for (int element = 0; element < elements.Count; element++)
                        {
                            int o = odt, e = element;
                            await ExchangeAsync(c => CcpFrame.SetDaqPointer(c, Target, list, o, e), ct).ConfigureAwait(false);
                            await ExchangeAsync(c => CcpFrame.WriteDaq(c, elements[e].Address, elements[e].Size), ct).ConfigureAwait(false);
                        }
                    }
                    await ExchangeAsync(c => CcpFrame.StartStop(c, Target, CcpStartStopMode.Prepare, list,
                        request.Odts.Count - 1, request.EventChannel, request.Prescaler), ct).ConfigureAwait(false);
                }
                // A list left prepared by an earlier failure would start with START_STOP_ALL: clear it first.
                foreach (int list in dirty)
                {
                    await ExchangeAsync(c => CcpFrame.GetDaqSize(c, Target, list), ct).ConfigureAwait(false);
                    lock (_daqLock) _dirtyLists.Remove(list);
                }
                subscription.FirstPids = firstPids;
                lock (_daqLock)
                {
                    for (int i = 0; i < lists.Count; i++)
                        for (int odt = 0; odt < lists[i].Odts.Count; odt++)
                            _routes[firstPids[i] + odt] = new DaqRoute(subscription, i, odt);
                    subscription.IsActive = true;
                }
                await ExchangeAsync(c => CcpFrame.StartStopAll(c, true), ct).ConfigureAwait(false);
                lock (_daqLock) foreach (int list in subscription.SlaveLists) _dirtyLists.Remove(list);
                return subscription;
            }
            catch (Exception ex)
            {
                Release(subscription, $"DAQ start failed: {ex.Message}", null);
                throw;
            }
        }, cancellationToken);
    }

    internal async Task StopDaqAsync(CcpDaqSubscription subscription, CancellationToken cancellationToken)
    {
        lock (_daqLock) if (!subscription.IsActive) return;
        if (State != CcpSessionState.Connected)
        {
            // Nothing can be sent. The slave may still stream these lists; they are re-initialised before reuse.
            Release(subscription, "Stopped locally; the session was not connected.", null, dirty: State == CcpSessionState.Uncertain);
            return;
        }
        try
        {
            await RunConnectedAsync("stop DAQ", async ct =>
            {
                foreach (int list in subscription.SlaveLists)
                    await ExchangeAsync(c => CcpFrame.StartStop(c, Target, CcpStartStopMode.Stop, list), ct).ConfigureAwait(false);
                Release(subscription, "Stopped.", null);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(subscription, "Stop failed; the slave may still stream these lists.", null, dirty: true);
            throw;
        }
    }

    private void Release(CcpDaqSubscription subscription, string reason, Exception? error, bool dirty = false)
    {
        lock (_daqLock)
        {
            var owners = ListOwners;
            for (int i = 0; i < owners.Length; i++)
            {
                if (owners[i] != subscription) continue;
                owners[i] = null;
                if (dirty) _dirtyLists.Add(i);
            }
            for (int pid = 0; pid < _routes.Length; pid++)
                if (_routes[pid]?.Subscription == subscription) _routes[pid] = null;
            subscription.IsActive = false;
            subscription.EndReason ??= reason;
        }
        subscription.Stream.Complete(error);
    }

    private void RouteDaq(CcpPacket packet, uint timestampMicroseconds)
    {
        var route = Volatile.Read(ref _routes[packet.Pid]);
        if (route is null)
        {
            Interlocked.Increment(ref _unroutedDaq);
            return;
        }
        Interlocked.Increment(ref _daqFrames);
        route.Subscription.RecordSample(route.List, packet.Overrun);
        route.Subscription.Stream.Write(new CcpDaqSample(route.List, route.Odt, packet.Data, packet.Overrun, timestampMicroseconds));
    }

    /// <summary>The slave lists currently allocated, with their owners and traffic.</summary>
    public IReadOnlyList<CcpDaqAllocation> GetDaqAllocations()
    {
        lock (_daqLock)
        {
            var owners = ListOwners;
            var allocations = new List<CcpDaqAllocation>();
            for (int list = 0; list < owners.Length; list++)
            {
                if (owners[list] is not { } subscription) continue;
                int index = Array.IndexOf(subscription.SlaveLists.ToArray(), list);
                var request = subscription.Requests[index];
                byte firstPid = index < subscription.FirstPids.Count ? subscription.FirstPids[index] : (byte)0;
                allocations.Add(new CcpDaqAllocation(list, subscription.Owner, request.EventChannel, request.Prescaler,
                    request.Odts.Count, firstPid, subscription.SamplesFor(index), subscription.OverrunsFor(index),
                    subscription.Stream.Dropped, subscription.Gaps));
            }
            return allocations;
        }
    }

    /// <summary>Ends every subscription: after DISCONNECT end-of-session (the slave stops all lists), a fault, or disposal.</summary>
    private void EndAllDaq(string reason, Exception? error = null)
    {
        List<CcpDaqSubscription> subscriptions;
        lock (_daqLock)
        {
            subscriptions = ListOwners.Where(o => o is not null).Distinct().Cast<CcpDaqSubscription>().ToList();
            _dirtyLists.Clear();
        }
        foreach (var subscription in subscriptions) Release(subscription, reason, error);
    }

    private void ValidateDaqRequest(IReadOnlyList<CcpDaqListRequest> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        if (lists.Count is < 1) throw new ArgumentException("Request at least one DAQ list.", nameof(lists));
        if (lists.Count > Target.DaqListCount)
            throw new ArgumentException($"{Target.Name} has only {Target.DaqListCount} DAQ list(s).", nameof(lists));
        foreach (var list in lists)
        {
            ArgumentNullException.ThrowIfNull(list);
            if (!Target.EventRatesHz.ContainsKey(list.EventChannel))
                throw new ArgumentException($"Event channel {list.EventChannel} does not exist on {Target.Name}.", nameof(lists));
            if (list.Prescaler is < 1 or > ushort.MaxValue)
                throw new ArgumentException("Prescaler must be 1–65535.", nameof(lists));
            if (list.Odts is null || list.Odts.Count < 1 || list.Odts.Count > Target.OdtsPerList)
                throw new ArgumentException($"A DAQ list holds 1–{Target.OdtsPerList} ODTs.", nameof(lists));
            foreach (var odt in list.Odts)
            {
                // Element 7 aliases the next ODT, and the slave silently truncates past seven payload bytes.
                if (odt is null || odt.Count is < 1 or > 7)
                    throw new ArgumentException("An ODT holds 1–7 elements.", nameof(lists));
                if (odt.Any(e => e.Size is not (1 or 2 or 4) || e.Address == 0))
                    throw new ArgumentException("DAQ elements are 1, 2 or 4 bytes at a non-zero address.", nameof(lists));
                if (odt.Sum(e => e.Size) > 7)
                    throw new ArgumentException("An ODT carries at most seven payload bytes.", nameof(lists));
            }
        }
    }
}
