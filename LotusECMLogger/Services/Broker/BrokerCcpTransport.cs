using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Broker
{
    /// <summary>
    /// CCP over the broker's shared raw CAN channel (J2534-session-broker-plan.md, Phase 5). It replaced
    /// <c>J2534CcpTransport</c>, which opened a device of its own; its polling (zero-timeout reads, sends under the
    /// same lock, 1 ms timer) lives on in <see cref="SharedCanChannel"/>. The transport holds a Shared lease named
    /// <see cref="LeaseHolder"/> for its whole life, and its CAN link names the ECU CCP talks to, so operations that
    /// hold that ECU (a DTC clear, a VIN write) are refused while a session is open, and the session is refused while
    /// they run. ISO 15765 diagnostics on other or the same ECUs run alongside.
    /// <para>
    /// The broker's CAN reader polls every 5 ms, and every 1 ms while a command waits for its reply: <see cref="Send"/>
    /// counts each command and <see cref="Read"/> each reply (CRM, PID 0xFF). DAQ and the AEM wideband poll need no
    /// fast polling: their frames carry the adapter's timestamps.
    /// </para>
    /// </summary>
    public sealed class BrokerCcpTransport : ICcpTransport
    {
        /// <summary>How the session names itself when the broker refuses someone else ("... is running").</summary>
        public const string LeaseHolder = "the CCP session";

        /// <summary>A command whose reply never comes stops holding fast polling after this long.</summary>
        private const int FastPollingLimitMs = 1000;

        private const byte CrmPid = 0xFF;

        private readonly BrokerLease _lease;
        private readonly BrokerCanLink _link;
        private readonly CcpStream<BrokerCanFrame> _frames;
        private readonly HashSet<uint> _receiveIds;
        private readonly object _fastLock = new();
        private IDisposable? _fast;
        private int _outstanding;
        private long _lastCommandMs;
        private int _reading;
        private int _disposed;

        /// <exception cref="BrokerBusyException">The adapter or the target's ECU is held by someone else.</exception>
        /// <exception cref="IOException">The adapter could not be opened, or has no free CAN filter.</exception>
        public BrokerCcpTransport(J2534Broker broker, CcpTarget target)
        {
            ArgumentNullException.ThrowIfNull(broker);
            Target = target ?? throw new ArgumentNullException(nameof(target));
            _receiveIds = [target.DtoId];
            _lease = broker.Acquire(LeaseKind.Shared, LeaseHolder);
            try
            {
                _link = _lease.OpenCan(EcuOf(target));
                _frames = _link.Subscribe(target.DtoId);
            }
            catch
            {
                _lease.Dispose();
                throw;
            }
        }

        public CcpTarget Target { get; }

        /// <summary>
        /// The OBD module a CCP target is, for EcuExclusive: the IPS TCU is the transmission module (0x7E1), every
        /// engine target the ECM (0x7E0).
        /// </summary>
        internal static ECUDefinition EcuOf(CcpTarget target) =>
            target == CcpTarget.IpsTcu ? ECUDefinition.TCM : ECUDefinition.ECM;

        public void Send(ReadOnlySpan<byte> command)
        {
            if (command.Length != CcpFrame.Length) throw new ArgumentException("CRO must have DLC 8.", nameof(command));
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lock (_fastLock)
            {
                _outstanding++;
                _lastCommandMs = Environment.TickCount64;
                _fast ??= _link.PollFast();
            }
            _link.Send(Target.CroId, command.ToArray());
        }

        public void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload)
        {
            CcpTransportRules.ValidateAuxiliary(Target, canId, payload);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _link.Send(canId, payload.ToArray());
        }

        public void AddReceiveId(uint canId)
        {
            CcpTransportRules.ValidateReceiveId(Target, canId);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lock (_receiveIds)
            {
                if (_receiveIds.Contains(canId)) return;
                _link.AddId(_frames, canId);
                _receiveIds.Add(canId);
            }
        }

        public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
        {
            if (timeoutMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
                throw new InvalidOperationException("CCP requires a single receive loop.");
            try
            {
                var frames = new List<CcpReceivedFrame>();
                Drain(frames);
                if (frames.Count == 0 && timeoutMilliseconds > 0 && WaitForFrames(timeoutMilliseconds, cancellationToken))
                    Drain(frames);
                if (frames.Count == 0 && _frames.Reader.Completion.IsCompleted) ThrowCompleted();
                UpdateFastPolling(frames);
                return frames;
            }
            finally { Volatile.Write(ref _reading, 0); }
        }

        private void Drain(List<CcpReceivedFrame> frames)
        {
            while (_frames.Reader.TryRead(out var frame))
                frames.Add(new CcpReceivedFrame(frame.Data, frame.TimestampMicroseconds, frame.CanId));
        }

        /// <summary>True when frames are waiting; false on timeout, cancellation or the end of the stream.</summary>
        private bool WaitForFrames(int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            // A cancelled wait unregisters itself, so idle polling does not pile up waiters on the channel.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMilliseconds);
            try
            {
                var wait = _frames.Reader.WaitToReadAsync(timeout.Token);
                return wait.IsCompletedSuccessfully ? wait.Result : wait.AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false; // a faulted stream; ThrowCompleted reports it
            }
        }

        private void ThrowCompleted()
        {
            var completion = _frames.Reader.Completion;
            if (completion.IsFaulted)
                throw completion.Exception!.InnerException ?? new IOException("The CAN channel failed.");
            throw new ObjectDisposedException(nameof(BrokerCcpTransport), "The CAN channel was closed.");
        }

        private void UpdateFastPolling(List<CcpReceivedFrame> frames)
        {
            int replies = 0;
            foreach (var frame in frames)
                if (frame.CanId == Target.DtoId && frame.Payload.Length > 0 && frame.Payload.Span[0] == CrmPid) replies++;

            lock (_fastLock)
            {
                if (_fast is null) return;
                _outstanding = Math.Max(0, _outstanding - replies);
                if (_outstanding == 0 || Environment.TickCount64 - _lastCommandMs > FastPollingLimitMs)
                {
                    _outstanding = 0;
                    _fast.Dispose();
                    _fast = null;
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_fastLock)
            {
                _fast?.Dispose();
                _fast = null;
            }
            _lease.Dispose(); // closes the link and its subscription, and the device with the last lease
        }
    }
}
