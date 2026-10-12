using System.Diagnostics;

namespace LotusECMLogger.Services.Ccp.Simulation;

/// <summary>
/// Thread-safe <see cref="ICcpTransport"/> over a <see cref="FakeCcpSlave"/>, so the threaded CcpMaster can drive
/// the deterministic fake. Every fake access runs under one lock. While nothing is pending, Read advances
/// virtual time one millisecond at a time, capped at <c>speedFactor</c> × elapsed wall time so periodic DAQ
/// cannot flood consumers. Disposing the transport does not dispose the fake: the test owns it.
/// </summary>
public sealed class SimulatedCcpTransport : ICcpTransport
{
    private readonly object _lock = new();
    private readonly FakeCcpSlave _ecu;
    private readonly double _speedFactor;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private long _virtualMilliseconds;
    private bool _disposed;

    public SimulatedCcpTransport(FakeCcpSlave ecu, double speedFactor = 20)
    {
        ArgumentNullException.ThrowIfNull(ecu);
        if (speedFactor <= 0) throw new ArgumentOutOfRangeException(nameof(speedFactor));
        (_ecu, _speedFactor) = (ecu, speedFactor);
    }

    public CcpTarget Target => _ecu.Target;
    /// <summary>Stops automatic time advance; the test then calls <see cref="Advance"/> explicitly.</summary>
    public bool Paused { get; set; }
    public long VirtualMilliseconds { get { lock (_lock) return _virtualMilliseconds; } }

    public void Advance(int milliseconds) => With(ecu =>
    {
        ecu.AdvanceMilliseconds(milliseconds);
        _virtualMilliseconds += milliseconds;
    });

    /// <summary>Runs scenario code (Load, Peek, InjectFrame, Reboot…) without racing the receive loop.</summary>
    public void With(Action<FakeCcpSlave> action)
    {
        lock (_lock) { ObjectDisposedException.ThrowIf(_disposed, this); action(_ecu); }
    }

    public T With<T>(Func<FakeCcpSlave, T> function)
    {
        lock (_lock) { ObjectDisposedException.ThrowIf(_disposed, this); return function(_ecu); }
    }

    public void Send(ReadOnlySpan<byte> command)
    {
        var bytes = command.ToArray();
        With(ecu => ecu.Send(bytes));
    }

    public void AddReceiveId(uint canId) => With(ecu => ecu.AddReceiveId(canId));

    public void SendAuxiliary(uint canId, ReadOnlySpan<byte> payload)
    {
        var bytes = payload.ToArray();
        With(ecu => ecu.SendAuxiliary(canId, bytes));
    }

    public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        if (timeoutMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        long deadline = _wall.ElapsedMilliseconds + timeoutMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var frames = _ecu.Read(0);
                if (frames.Count > 0) return frames;
                if (!Paused && _virtualMilliseconds < _wall.Elapsed.TotalMilliseconds * _speedFactor)
                {
                    _ecu.AdvanceMilliseconds(1);
                    _virtualMilliseconds++;
                    continue;
                }
            }
            if (_wall.ElapsedMilliseconds >= deadline) return [];
            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
    }
}
