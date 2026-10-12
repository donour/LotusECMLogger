using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpSessionHostTests : IDisposable
{
    private readonly FakeCcpSlave _slave = new();
    private readonly List<ScriptedTransport> _opened = [];
    private readonly CcpSessionHost _host;

    public CcpSessionHostTests()
    {
        _host = new CcpSessionHost(() =>
        {
            var transport = new ScriptedTransport(new SimulatedCcpTransport(_slave, speedFactor: 50));
            lock (_opened) _opened.Add(transport);
            return transport;
        }, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
    }

    public void Dispose()
    {
        _host.Dispose();
        _slave.Dispose();
    }

    private ScriptedTransport Latest { get { lock (_opened) return _opened[^1]; } }

    [Fact]
    public async Task LeasesShareOneConnectedSessionAndTheLastReleaseClosesIt()
    {
        var first = await _host.AcquireAsync();
        var second = await _host.AcquireAsync();
        Assert.Same(first.Master, second.Master);
        Assert.Equal(CcpSessionState.Connected, first.Master.State);
        Assert.Single(_opened);
        Assert.Equal(1, Latest.Sent.Count(c => c[0] == (byte)CcpCommandCode.Connect)); // joined, not reconnected

        await first.DisposeAsync();
        await first.DisposeAsync(); // releasing twice counts once
        Assert.Equal(1, _host.LeaseCount);
        Assert.False(Latest.Disposed);

        await second.DisposeAsync();
        Assert.Equal(0, _host.LeaseCount);
        Assert.Null(_host.Master);
        Assert.True(Latest.Disposed);
        Assert.Equal(((byte)CcpCommandCode.Disconnect, (byte)1), (Latest.Sent[^1][0], Latest.Sent[^1][2]));

        await using var third = await _host.AcquireAsync(); // a fresh session reopens the adapter
        Assert.Equal(2, _opened.Count);
        Assert.Equal(CcpSessionState.Connected, third.Master.State);
    }

    [Fact]
    public async Task FailedConnectClosesTheAdapterAgain()
    {
        _slave.Load(CcpBuildProfile.Known.Single(p => p.Build == "C132E0278").EnableByteAddress!.Value, [2]);
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _host.AcquireAsync());
        Assert.Equal(0, _host.LeaseCount);
        Assert.Null(_host.Master);
        Assert.True(Latest.Disposed);
    }

    [Fact]
    public async Task AcquiringRecoversAnUncertainSession()
    {
        await using var holder = await _host.AcquireAsync();
        Latest.Inject(ScriptedTransport.Crm(0x77));
        for (int i = 0; i < 200 && holder.Master.State != CcpSessionState.Uncertain; i++) await Task.Delay(10);
        Assert.Equal(CcpSessionState.Uncertain, holder.Master.State);

        await using var joiner = await _host.AcquireAsync();
        Assert.Same(holder.Master, joiner.Master);
        Assert.Equal(CcpSessionState.Connected, joiner.Master.State);
    }

    [Fact]
    public async Task AFaultedSessionIsReplacedAndItsOldLeasesReleaseHarmlessly()
    {
        var old = await _host.AcquireAsync();
        Latest.ReadFailure = new IOException("adapter unplugged");
        for (int i = 0; i < 200 && old.Master.State != CcpSessionState.Faulted; i++) await Task.Delay(10);
        Assert.Equal(CcpSessionState.Faulted, old.Master.State);

        var fresh = await _host.AcquireAsync();
        Assert.NotSame(old.Master, fresh.Master);
        Assert.Equal(CcpSessionState.Disposed, old.Master.State);
        await old.DisposeAsync();
        Assert.Equal(1, _host.LeaseCount);
        Assert.Equal(CcpSessionState.Connected, fresh.Master.State);
        await fresh.DisposeAsync();
        Assert.Null(_host.Master);
    }

    [Fact]
    public async Task DisposingTheHostEndsTheSession()
    {
        var lease = await _host.AcquireAsync();
        int changes = 0;
        _host.Changed += (_, _) => Interlocked.Increment(ref changes);
        _host.Dispose();
        Assert.True(Latest.Disposed);
        Assert.Equal((byte)CcpCommandCode.Disconnect, Latest.Sent[^1][0]);
        Assert.True(changes > 0);
        await lease.DisposeAsync(); // after the host is gone, releasing is a no-op
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _host.AcquireAsync());
    }
}
