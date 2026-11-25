using LotusECMLogger.Services.Links;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>
    /// The broker's ISO 15765 channel, shared by every diagnostic link. One reader thread routes replies to the
    /// waiting exchanges; one exchange per ECU runs at a time, so two operations on the ECM cannot take each other's
    /// replies. The reader blocks in the DLL only while a reply is awaited, and polls with zero timeout otherwise, so
    /// an idle channel never holds the DLL's lock (plan §1.3).
    /// </summary>
    internal sealed class SharedIsoChannel : IDisposable
    {
        private const int MaxBatch = 16;

        private readonly IJ2534Channel _channel;
        private readonly BrokerOptions _options;
        private readonly Func<bool> _canChannelOpen;
        private readonly DllCallers _dll;
        private readonly object _sync = new();
        private readonly Dictionary<uint, (int FilterId, int Users)> _filters = [];
        private readonly Dictionary<uint, SemaphoreSlim> _gates = [];
        private readonly List<PendingExchange> _pending = [];
        private readonly object _sendLock = new();
        private readonly ManualResetEventSlim _wake = new(false);
        private readonly Thread _reader;
        private long _lastSendMs = long.MinValue / 2;
        private Exception? _fault;
        private volatile bool _stopping;

        public SharedIsoChannel(IJ2534Channel channel, BrokerOptions options, Func<bool> canChannelOpen,
            DllCallers dll)
        {
            _channel = channel;
            _options = options;
            _canChannelOpen = canChannelOpen;
            _dll = dll;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "J2534 broker ISO 15765 reader" };
            _reader.Start();
        }

        /// <summary>Adds a user of <paramref name="ecu"/>'s flow-control filter, starting it for the first.</summary>
        public void AddEcu(ECUDefinition ecu)
        {
            lock (_sync)
            {
                ThrowIfFaulted();
                if (_filters.TryGetValue(ecu.ResponseId, out var entry))
                {
                    _filters[ecu.ResponseId] = (entry.FilterId, entry.Users + 1);
                    return;
                }
                int id;
                try
                {
                    id = _channel.StartFilter(ecu.CreateFlowControlFilter());
                }
                catch (FilterLimitException)
                {
                    throw new IOException(
                        $"The adapter cannot talk to one more module ({ecu.Name}): its {_filters.Count} ISO 15765 filters are in use.");
                }
                _filters[ecu.ResponseId] = (id, 1);
            }
        }

        /// <summary>Removes a user of <paramref name="ecu"/>'s filter, stopping it with the last.</summary>
        public void RemoveEcu(ECUDefinition ecu)
        {
            lock (_sync)
            {
                if (!_filters.TryGetValue(ecu.ResponseId, out var entry)) return;
                if (entry.Users > 1)
                {
                    _filters[ecu.ResponseId] = (entry.FilterId, entry.Users - 1);
                    return;
                }
                _filters.Remove(ecu.ResponseId);
                if (_fault is null && !_stopping)
                {
                    try { _channel.StopFilter(entry.FilterId); }
                    catch (Exception) { /* the channel closes with the device; a stale filter only costs a slot */ }
                }
            }
        }

        /// <summary>See <see cref="IDiagnosticLink.Exchange"/>.</summary>
        public DiagnosticReply Exchange(ECUDefinition ecu, byte[] request, int echoLength, int timeoutMs)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.Length, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);
            ThrowIfFaulted();

            SemaphoreSlim gate;
            lock (_sync)
            {
                if (!_gates.TryGetValue(ecu.ResponseId, out gate!))
                    _gates[ecu.ResponseId] = gate = new SemaphoreSlim(1, 1);
            }
            if (!gate.Wait(DiagnosticTiming.MaxExchangeMs))
                throw new IOException($"An earlier request to the {ecu.Name} has not finished.");
            try
            {
                // The deadline is set before the send, so a response-pending that arrives at once extends it rather
                // than being overwritten. The send's pacing wait (at most one interval) comes out of the timeout.
                long now = Environment.TickCount64;
                var pending = new PendingExchange(ecu.ResponseId, request, echoLength)
                {
                    Deadline = now + Math.Min(timeoutMs, DiagnosticTiming.MaxExchangeMs),
                    Cap = now + DiagnosticTiming.MaxExchangeMs,
                };
                lock (_sync) _pending.Add(pending);
                try
                {
                    SendPaced(ecu, request);
                    // Only now may the reader block for it: blocking earlier would hold the DLL lock this send needs.
                    lock (_sync) pending.Sent = true;
                    _wake.Set();

                    while (true)
                    {
                        long remaining;
                        lock (_sync)
                        {
                            if (pending.Done) break;
                            remaining = pending.Deadline - Environment.TickCount64;
                        }
                        if (remaining <= 0) return DiagnosticReply.Timeout;
                        pending.Signal.Wait((int)remaining);
                    }

                    if (pending.Fault is not null)
                        throw new IOException($"The adapter stopped responding: {pending.Fault.Message}", pending.Fault);
                    return pending.Reply;
                }
                finally
                {
                    lock (_sync) _pending.Remove(pending);
                    pending.Signal.Dispose();
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>See <see cref="IDiagnosticLink.Send"/>.</summary>
        public void Send(ECUDefinition ecu, byte[] request)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.Length, 1);
            ThrowIfFaulted();
            SendPaced(ecu, request);
        }

        /// <summary>The message handed to the ISO 15765 channel: the ECU's request CAN ID, then the request.</summary>
        internal static byte[] Frame(ECUDefinition ecu, byte[] request) => [.. ecu.GetRequestHeader(), .. request];

        private void SendPaced(ECUDefinition ecu, byte[] request)
        {
            byte[] message = Frame(ecu, request);
            using var counted = _dll.Enter();
            lock (_sendLock)
            {
                if (_canChannelOpen())
                {
                    long wait = _lastSendMs + _options.IsoSendIntervalWithCanMs - Environment.TickCount64;
                    if (wait > 0) Thread.Sleep((int)wait);
                }
                _channel.Send(message);
                _lastSendMs = Environment.TickCount64;
            }
        }

        private void ReadLoop()
        {
            while (!_stopping)
            {
                // Reset before looking, so an exchange that starts after the look still wakes the idle wait.
                _wake.Reset();
                bool awaitingReply;
                lock (_sync) awaitingReply = _pending.Any(p => p.Sent && !p.Done);
                bool block = awaitingReply && !_dll.AnyWaiting;

                IReadOnlyList<ReceivedMessage> messages;
                try
                {
                    if (block)
                    {
                        // Ask for one: J2534 returns early only once the asked-for count has arrived. Then drain.
                        messages = _channel.Read(1, _options.IsoReplySliceMs);
                        if (messages.Count > 0)
                        {
                            var more = _channel.Read(MaxBatch, 0);
                            if (more.Count > 0) messages = [.. messages, .. more];
                        }
                    }
                    else
                    {
                        messages = _channel.Read(MaxBatch, 0);
                    }
                }
                catch (Exception ex)
                {
                    if (!_stopping) Fault(ex);
                    return;
                }

                foreach (var message in messages) Route(message);
                if (messages.Count > 0) continue;
                if (!awaitingReply) _wake.Wait(_options.IsoIdlePollMs);
                else if (!block) Thread.Sleep(1); // outside the DLL, so the waiting send gets in

            }
        }

        private void Route(ReceivedMessage message)
        {
            lock (_sync)
            {
                foreach (var pending in _pending)
                {
                    if (pending.Done) continue;
                    switch (DiagnosticResponseMatcher.Match(pending.ResponseId, pending.Request, pending.EchoLength,
                        message.Data, out byte nrc))
                    {
                        case DiagnosticMatch.Positive:
                            pending.Complete(DiagnosticReply.Positive(message.Data));
                            return;
                        case DiagnosticMatch.Negative:
                            pending.Complete(DiagnosticReply.Negative(nrc));
                            return;
                        case DiagnosticMatch.Pending:
                            pending.Deadline = Math.Min(
                                Math.Max(pending.Deadline, Environment.TickCount64 + DiagnosticTiming.ResponsePendingExtensionMs),
                                pending.Cap);
                            return;
                    }
                }
            }
            // Anything else (a late reply, another module, an ISO-TP indication) answers no one and is dropped.
        }

        private void Fault(Exception ex)
        {
            lock (_sync)
            {
                _fault ??= ex;
                foreach (var pending in _pending) pending.Fail(ex);
            }
        }

        private void ThrowIfFaulted()
        {
            var fault = Volatile.Read(ref _fault);
            if (fault is not null)
                throw new IOException(
                    $"The adapter stopped responding ({fault.Message}). Finish or stop what is using it so it can be reopened.", fault);
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            if (Thread.CurrentThread != _reader) _reader.Join(TimeSpan.FromSeconds(2));
            Fault(new ObjectDisposedException("The adapter was closed."));
            _channel.Dispose();
        }

        private sealed class PendingExchange(uint responseId, byte[] request, int echoLength)
        {
            public uint ResponseId { get; } = responseId;
            public byte[] Request { get; } = request;
            public int EchoLength { get; } = echoLength;
            public ManualResetEventSlim Signal { get; } = new(false);

            // Guarded by the channel's _sync.
            public long Deadline { get; set; } = long.MaxValue;
            public long Cap { get; set; } = long.MaxValue;
            public bool Sent { get; set; }
            public bool Done { get; private set; }
            public DiagnosticReply Reply { get; private set; }
            public Exception? Fault { get; private set; }

            public void Complete(DiagnosticReply reply)
            {
                Reply = reply;
                Done = true;
                Signal.Set();
            }

            public void Fail(Exception ex)
            {
                if (Done) return;
                Fault = ex;
                Done = true;
                Signal.Set();
            }
        }
    }
}
