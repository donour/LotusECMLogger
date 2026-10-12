using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpMasterSessionTests : IDisposable
{
    private readonly FakeCcpSlave _slave = new();
    private readonly SimulatedCcpTransport _simulation;
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;

    public CcpMasterSessionTests()
    {
        _simulation = new SimulatedCcpTransport(_slave, speedFactor: 50);
        _script = new ScriptedTransport(_simulation);
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
    }

    public void Dispose()
    {
        _master.Dispose();
        _slave.Dispose();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task ConnectSendsConnectThenVersionAndExchangeIdUploadsTheIdString()
    {
        await _master.ConnectAsync();
        Assert.Equal(["0100000000000000", "1B01020100000000"], _script.SentHex);
        Assert.Equal(CcpSessionState.Connected, _master.State);

        var identity = await _master.ExchangeIdAsync();
        Assert.Equal("Ccp_T6", identity.SlaveId);
        Assert.Equal((byte)3, identity.Fields.AvailableResources);
        Assert.Equal((byte)0, identity.Fields.ProtectedResources);
        Assert.Equal((2, 1), (identity.VersionMajor, identity.VersionMinor));
        // EXCHANGE_ID, two UPLOADs (5 + 1 bytes of "Ccp_T6"), GET_CCP_VERSION.
        Assert.Equal(["1702000000000000", "0403050000000000", "0404010000000000", "1B05020100000000"],
            _script.SentHex.Skip(2));
    }

    [Fact]
    public async Task CommandsBeforeConnectAreRefusedWithoutReachingTheBus()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.ExchangeIdAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.DisconnectAsync());
        Assert.Empty(_script.Sent);
        await _master.TestAsync(); // TEST is answered without a session
        Assert.Equal(CcpSessionState.Disconnected, _master.State);
    }

    [Fact]
    public async Task SilentBusTimesOutAndLeavesTheSessionDisconnected()
    {
        _simulation.With(s => s.Load(CcpBuildProfile.Known.Single(p => p.Build == "C132E0278").EnableByteAddress!.Value, [2]));
        var error = await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.ConnectAsync());
        Assert.Equal(CcpCommandCode.Connect, error.Command);
        Assert.Equal(CcpSessionState.Disconnected, _master.State);
    }

    [Fact]
    public async Task LostReplyMakesTheSessionUncertainUntilReconnected()
    {
        await _master.ConnectAsync();
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.ExchangeId;
        await Assert.ThrowsAsync<CcpTimeoutException>(() => _master.ExchangeIdAsync());
        Assert.Equal(CcpSessionState.Uncertain, _master.State);
        Assert.Contains("ExchangeId", _master.StateReason);

        int sent = _script.Sent.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.GetVersionAsync());
        Assert.Equal(sent, _script.Sent.Count); // refused locally: nothing is retried blindly

        _script.Drop = null;
        await _master.ConnectAsync();
        Assert.Equal(CcpSessionState.Connected, _master.State);
        Assert.Equal("Ccp_T6", (await _master.ExchangeIdAsync()).SlaveId);
    }

    [Fact]
    public async Task StrayReplyMakesTheSessionUncertain()
    {
        await _master.ConnectAsync();
        _script.Inject(ScriptedTransport.Crm(0x77));
        await Eventually(() => _master.State == CcpSessionState.Uncertain);
        Assert.Equal(1, _master.StrayReplies);
        await _master.ConnectAsync();
        Assert.Equal(CcpSessionState.Connected, _master.State);
    }

    [Fact]
    public async Task StrayReplyDuringReconnectKeepsTheSessionUncertain()
    {
        await _master.ConnectAsync();
        _script.Inject(ScriptedTransport.Crm(0x77));
        await Eventually(() => _master.State == CcpSessionState.Uncertain);
        // A late checksum completion, for example, arrives in the middle of the recovery exchange.
        _script.Reply = cro =>
        {
            if (cro[0] == (byte)CcpCommandCode.Connect) _script.Inject(ScriptedTransport.Crm(0x55));
            return null;
        };
        await Assert.ThrowsAsync<CcpProtocolException>(() => _master.ConnectAsync());
        Assert.Equal(CcpSessionState.Uncertain, _master.State);
    }

    [Fact]
    public async Task ErrorReplyThrowsWithTheRawCodeAndKeepsTheSession()
    {
        await _master.ConnectAsync();
        _script.Reply = cro => cro[0] == (byte)CcpCommandCode.GetCcpVersion ? ScriptedTransport.Crm(cro[1], 0x30) : null;
        var error = await Assert.ThrowsAsync<CcpException>(() => _master.GetVersionAsync());
        Assert.Equal((byte)0x30, error.ErrorCode);
        Assert.Equal(CcpSessionState.Connected, _master.State);
    }

    [Fact]
    public async Task OtherCcpVersionsAreRejected()
    {
        _script.Reply = cro => cro[0] == (byte)CcpCommandCode.GetCcpVersion ? ScriptedTransport.Crm(cro[1], 0, 2, 0) : null;
        await Assert.ThrowsAsync<CcpProtocolException>(() => _master.ConnectAsync());
        Assert.Equal(CcpSessionState.Disconnected, _master.State);
    }

    [Fact]
    public async Task CancellingWhileAwaitingAReplyMakesTheSessionUncertain()
    {
        await _master.ConnectAsync();
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.ExchangeId;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _master.ExchangeIdAsync(cancel.Token));
        Assert.Equal(CcpSessionState.Uncertain, _master.State);
    }

    [Fact]
    public async Task ConcurrentOperationsAreSerialisedAsWholeOperations()
    {
        await _master.ConnectAsync();
        // Interleaved EXCHANGE_ID/UPLOAD pairs would read through each other's MTA0.
        var identities = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _master.ExchangeIdAsync()));
        Assert.All(identities, identity => Assert.Equal("Ccp_T6", identity.SlaveId));
    }

    [Fact]
    public async Task TransportFailureFaultsTheSessionAndFailsTheWaitingCommand()
    {
        await _master.ConnectAsync();
        var stream = _master.SubscribeAuxiliary(0x7E9);
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.ExchangeId;
        var waiting = _master.ExchangeIdAsync();
        _script.ReadFailure = new IOException("adapter unplugged");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => waiting);
        Assert.True(error is IOException or CcpTimeoutException);
        await Eventually(() => _master.State == CcpSessionState.Faulted);
        Assert.Equal("adapter unplugged", _master.StateReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.ConnectAsync());
        await Assert.ThrowsAnyAsync<IOException>(async () => await stream.Reader.Completion);
    }

    [Fact]
    public async Task AuxiliaryFramesAreRoutedByIdWithoutACcpSession()
    {
        byte[] lambda = [0x06, 0x41, 0x24, 0x80, 0x00, 0x80, 0x00, 0x00];
        _slave.AuxiliarySent += (canId, _) => { if (canId == 0x7E1) _slave.InjectFrame(0x7E9, lambda); };
        using var stream = _master.SubscribeAuxiliary(0x7E9);
        _master.SendAuxiliary(0x7E1, [0x02, 0x01, 0x24, 0, 0, 0, 0, 0]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var frame = await stream.Reader.ReadAsync(timeout.Token);
        Assert.Equal(0x7E9u, frame.CanId);
        Assert.Equal(lambda, frame.Payload.ToArray());
        Assert.Equal(CcpSessionState.Disconnected, _master.State);
        Assert.Throws<ArgumentException>(() => _master.SendAuxiliary(CcpTarget.T6.CroId, [0]));
    }

    [Fact]
    public async Task DisconnectEndsTheSession()
    {
        await _master.ConnectAsync();
        await _master.DisconnectAsync();
        Assert.Equal("0702010000000000", _script.SentHex.Last());
        Assert.Equal(CcpSessionState.Disconnected, _master.State);
    }

    [Fact]
    public async Task DisposeStopsTheReceiveLoopAndDisposesTheTransport()
    {
        var states = new List<CcpSessionState>();
        _master.StateChanged += (_, state) => states.Add(state);
        await _master.ConnectAsync();
        _master.Dispose();
        Assert.True(_script.Disposed);
        Assert.Equal(CcpSessionState.Disposed, _master.State);
        Assert.Equal([CcpSessionState.Connected, CcpSessionState.Disposed], states);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _master.ConnectAsync());
    }
}
