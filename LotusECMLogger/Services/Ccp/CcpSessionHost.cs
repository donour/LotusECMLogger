using System.Diagnostics;

namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// Shares one <see cref="CcpMaster"/> (and so one J2534 device) between everything that talks CCP: the CCP
/// tab, the high-speed logger and the Vehicle Info probe. Two independent sessions would conflict over the
/// adapter. The first lease opens the transport and CONNECTs; later leases reuse the session, reconnecting it
/// if it became uncertain. The last release ends the CCP session (DISCONNECT, which also stops all DAQ) and
/// closes the transport, so other tabs can use the adapter again.
/// <para>
/// A master whose transport failed is replaced by the next acquisition. Leases on the failed master keep
/// pointing at it, see errors from it, and release without affecting the new session.
/// </para>
/// </summary>
public sealed class CcpSessionHost : IDisposable
{
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<ICcpTransport> _openTransport;
    private readonly CcpMasterOptions? _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CcpMaster? _master;
    private int _generation, _leases;
    private bool _disposed;

    /// <param name="openTransport">Opens the adapter for a new session; the master takes ownership.</param>
    public CcpSessionHost(Func<ICcpTransport> openTransport, CcpMasterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(openTransport);
        _openTransport = openTransport;
        _options = options;
    }

    /// <summary>The open session, if any. Hold a lease to use it.</summary>
    public CcpMaster? Master => Volatile.Read(ref _master);
    public int LeaseCount => Volatile.Read(ref _leases);

    /// <summary>
    /// Raised after a session opens or closes, a lease is taken or released, or the session's state changes.
    /// May be raised on any thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Opens (or joins) the session and makes sure it is connected.</summary>
    public async Task<CcpSessionLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_master is { State: CcpSessionState.Faulted or CcpSessionState.Disposed }) CloseMaster();
            if (_master is null)
            {
                _master = new CcpMaster(_openTransport(), _options);
                _master.StateChanged += OnMasterStateChanged;
                _generation++;
            }
            var master = _master;
            _leases++;
            try
            {
                // CONNECT is safe while connected (it does not stop DAQ) and recovers an uncertain session.
                if (master.State != CcpSessionState.Connected) await master.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (--_leases == 0) CloseMaster();
                throw;
            }
            return new CcpSessionLease(this, master, _generation);
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal async Task ReleaseAsync(CcpSessionLease lease)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A lease on a master that was already replaced or closed has nothing left to release.
            if (_disposed || _master is null || lease.Generation != _generation) return;
            if (--_leases > 0) return;
            await EndSessionAsync(_master).ConfigureAwait(false);
            CloseMaster();
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task EndSessionAsync(CcpMaster master)
    {
        if (master.State is not (CcpSessionState.Connected or CcpSessionState.Uncertain)) return;
        try
        {
            using var timeout = new CancellationTokenSource(DisconnectTimeout);
            await master.DisconnectAsync(endSession: true, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { Debug.WriteLine($"CCP: ending the session failed: {ex.Message}"); }
    }

    private void CloseMaster()
    {
        if (_master is null) return;
        _master.StateChanged -= OnMasterStateChanged;
        _master.Dispose();
        _master = null;
        _leases = 0;
    }

    private void OnMasterStateChanged(object? sender, CcpSessionState state) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Ends any open session (best effort) and closes the adapter, e.g. when the application exits.</summary>
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_master is { } master) Task.Run(() => EndSessionAsync(master)).GetAwaiter().GetResult();
            CloseMaster();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A consumer's hold on the shared session. Dispose it when done; the last one closes the session.</summary>
public sealed class CcpSessionLease : IAsyncDisposable, IDisposable
{
    private readonly CcpSessionHost _host;
    private int _released;

    internal CcpSessionLease(CcpSessionHost host, CcpMaster master, int generation)
        => (_host, Master, Generation) = (host, master, generation);

    public CcpMaster Master { get; }
    internal int Generation { get; }

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _released, 1) == 0 ? new ValueTask(_host.ReleaseAsync(this)) : ValueTask.CompletedTask;

    /// <summary>Blocking release. The host never resumes on a captured context, so this is safe on the UI thread.</summary>
    public void Dispose() => Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
}
