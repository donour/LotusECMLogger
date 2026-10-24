using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

namespace LotusECMLogger.Ccp.Tests;

public sealed class LiveTuningSessionTests : IAsyncLifetime
{
    private const string TableName = "CAL_injtip_transient_throttle_response_time"; // u8, 8 × 8 at 0x40008A9A
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static readonly uint Cal = Gt430.CalibrationBase;
    private static readonly uint Enable = Gt430.EnableByteAddress!.Value;
    private static readonly CcpEngineSpeedSymbol Rpm = new(0x40001762, 2, 1);
    private static readonly Lazy<A2lResolvedModule> A2l = new(() => A2lLayout.Resolve(A2lParser.Parse(
        A2lGenerator.Generate(SymbolCatalogLoader.LoadByName(Gt430.Catalog)!, Gt430).Text)));

    private readonly FakeCcpSlave _slave = new();
    private readonly SimulatedCcpTransport _simulation;
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;
    private readonly string _backups = Path.Combine(Path.GetTempPath(), $"ccp-live-{Guid.NewGuid():N}");
    private LiveTuningSession? _session;

    public LiveTuningSessionTests()
    {
        _slave.Load(Cal, Encoding.ASCII.GetBytes("C132E0278 test calibration"));
        _slave.Load(Cal + 0x100, Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        _simulation = new SimulatedCcpTransport(_slave, speedFactor: 200);
        _script = new ScriptedTransport(_simulation);
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromSeconds(1) });
    }

    public Task InitializeAsync() => _master.ConnectAsync();

    public async Task DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
        _master.Dispose();
        _slave.Dispose();
        if (Directory.Exists(_backups)) Directory.Delete(_backups, recursive: true);
    }

    private static A2lCalibrationItem Table => A2l.Value.Items[TableName];

    private byte[] Peek(uint address, int length) =>
        _simulation.With(s => Enumerable.Range(0, length).Select(i => s.Peek(address + (uint)i)).ToArray());

    private byte[] Ecu() => Peek(Cal, (int)Gt430.CalibrationSize);

    private int Downloads => _script.Sent.Count(c => c[0] is (byte)CcpCommandCode.Download or (byte)CcpCommandCode.Download6);

    private async Task<LiveTuningSession> Start(byte[]? file = null, LiveTuningMode mode = LiveTuningMode.Live)
    {
        var image = CalibrationImage.FromBytes(file ?? Ecu());
        _session = await LiveTuningSession.StartAsync(_master, image,
            new LiveTuningOptions { BackupDirectory = _backups, EngineSpeed = Rpm, Mode = mode });
        _script.ClearSent();
        return _session;
    }

    private static uint CellAddress(int x, int y) => Table.CellAddress(x, y);

    [Fact]
    public async Task ALiveEditIsWrittenAndVerifiedAtOnce()
    {
        var session = await Start();
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);
        Assert.Equal(Ecu(), File.ReadAllBytes(session.BackupPath!));

        session.Image.SetPhysical(Table, [new CellValue(3, 5, 50)]); // raw 10
        await session.WhenIdleAsync();

        Assert.Equal([10], Peek(CellAddress(3, 5), 1));
        Assert.Equal(CalibrationSyncState.InSync, session.StateOf(Table, 3, 5));
        Assert.Equal(0, session.PendingBytes);
        Assert.Equal(1, Downloads);
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);
    }

    [Fact]
    public async Task RapidEditsEndWithTheEcuHoldingTheLastValues()
    {
        var session = await Start();
        for (int i = 0; i < 20; i++)
            session.Image.SetRaw(Table, [new CellValue(i % 8, i / 8, 100 + i), new CellValue(0, 7, i)]);
        await session.WhenIdleAsync();
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);
        Assert.Equal(0, session.PendingBytes);
        Assert.Equal(session.Image.Bytes(Table.Address, 64).ToArray(), Peek(Table.Address, 64));
        Assert.Equal([19], Peek(CellAddress(0, 7), 1));
    }

    [Fact]
    public async Task UndoIsWrittenBackLikeAnyEdit()
    {
        var session = await Start();
        byte before = Peek(CellAddress(0, 0), 1)[0];
        session.Image.SetRaw(Table, [new CellValue(0, 0, (byte)(before + 1))]);
        await session.WhenIdleAsync();
        session.Image.Undo();
        await session.WhenIdleAsync();
        Assert.Equal([before], Peek(CellAddress(0, 0), 1));
        Assert.Equal(2, Downloads);
    }

    [Fact]
    public async Task HoldStagesEditsUntilApply()
    {
        var session = await Start(mode: LiveTuningMode.Hold);
        session.Image.SetRaw(Table, [new CellValue(0, 0, 1), new CellValue(7, 7, 2)]);
        await session.WhenIdleAsync();
        Assert.Equal(0, Downloads);
        Assert.Equal(2, session.PendingBytes);
        Assert.Equal(CalibrationSyncState.Pending, session.StateOf(Table, 7, 7));

        await session.ApplyAsync();
        Assert.Equal([1], Peek(CellAddress(0, 0), 1));
        Assert.Equal([2], Peek(CellAddress(7, 7), 1));
        Assert.Equal(CalibrationSyncState.InSync, session.StateOf(Table, 7, 7));
    }

    [Fact]
    public async Task ADifferingFileWaitsForTheUserToChooseASide()
    {
        var file = Ecu();
        file[0x100] ^= 0xFF;
        var session = await Start(file);
        Assert.Equal(LiveTuningStatus.Comparing, session.Status);
        Assert.Equal([new CalibrationSpan(Cal + 0x100, 1)], session.Differences);

        session.Image.SetRaw(Table, [new CellValue(1, 1, 3)]); // edits are not written while comparing
        await session.WhenIdleAsync();
        Assert.Equal(0, Downloads);

        session.TakeEcuValues();
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);
        Assert.Equal(Ecu()[0x100], session.Image.Bytes(Cal + 0x100, 1)[0]);
        Assert.Equal("Take the ECU's values", session.Image.UndoDescription);
        Assert.Empty(session.Differences);
        await session.WhenIdleAsync();
        Assert.Equal(0, Downloads);
    }

    [Fact]
    public async Task SendingTheFileWritesItButKeepsTheEcusEnableByte()
    {
        var file = Ecu();
        file[0x100] ^= 0xFF;
        file[Enable - Cal] ^= 0xFF;
        var session = await Start(file);
        Assert.True(session.EnableByteDiffers);
        Assert.DoesNotContain(session.Differences, s => s.Address <= Enable && Enable < s.Address + s.Length);

        byte enableBefore = Peek(Enable, 1)[0];
        await session.SendImageAsync();
        Assert.Equal(LiveTuningStatus.Syncing, session.Status);
        Assert.Equal(file[0x100], Peek(Cal + 0x100, 1)[0]);
        Assert.Equal([enableBefore], Peek(Enable, 1));
    }

    [Fact]
    public async Task SendingTheFileIsRefusedWhileTheEngineTurns()
    {
        var file = Ecu();
        file[0x100] ^= 0xFF;
        var session = await Start(file);
        _simulation.With(s => s.Load(Rpm.Address, [0x03, 0x20])); // 800 rpm
        Assert.Contains("800 rpm", (await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendImageAsync())).Message);
        Assert.Equal(0, Downloads);
        Assert.Equal(LiveTuningStatus.Comparing, session.Status);
    }

    [Fact]
    public async Task AChangeMadeElsewhereStopsSyncInsteadOfBeingOverwritten()
    {
        var session = await Start();
        _simulation.With(s => s.Load(Cal + 0x300, [0xEE])); // another tool, or a reboot restoring flash
        session.Image.SetRaw(Table, [new CellValue(2, 2, 7)]);
        await session.WhenIdleAsync();

        Assert.Equal(LiveTuningStatus.Stopped, session.Status);
        Assert.Contains("outside this session", session.StopReason);
        Assert.Equal(0, Downloads);

        await session.RefreshAsync();
        Assert.Equal(LiveTuningStatus.Comparing, session.Status); // the cell edit and the foreign byte both differ
        Assert.Equal(2, session.Differences.Count);
    }

    [Fact]
    public async Task AFailedWriteStopsSyncAndMarksItsBytesFailed()
    {
        var session = await Start();
        _script.Drop = cro => cro[0] is (byte)CcpCommandCode.Download or (byte)CcpCommandCode.Download6;
        session.Image.SetRaw(Table, [new CellValue(4, 4, 99)]);
        await session.WhenIdleAsync();

        Assert.Equal(LiveTuningStatus.Stopped, session.Status);
        Assert.Equal(CalibrationSyncState.Failed, session.StateOf(Table, 4, 4));
        Assert.Equal(CcpSessionState.Uncertain, _master.State);

        _script.Drop = null;
        int downloads = Downloads;
        session.Image.SetRaw(Table, [new CellValue(5, 5, 98)]); // nothing more is written once stopped
        await session.WhenIdleAsync();
        Assert.Equal(downloads, Downloads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync());

        await _master.ConnectAsync();
        await session.RefreshAsync();
        Assert.Equal(CalibrationSyncState.Pending, session.StateOf(Table, 4, 4));
        Assert.Equal(LiveTuningStatus.Comparing, session.Status);
    }

    [Fact]
    public async Task RefusesAnotherBuildsFile()
    {
        var file = Ecu();
        Encoding.ASCII.GetBytes("C132E0271").CopyTo(file, 0);
        var image = CalibrationImage.FromBytes(file);
        Assert.Contains("C132E0278", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LiveTuningSession.StartAsync(_master, image))).Message);
    }

    [Fact]
    public async Task ClosingStopsListeningToTheImage()
    {
        var session = await Start();
        await session.DisposeAsync();
        session.Image.SetRaw(Table, [new CellValue(6, 6, 5)]);
        await session.WhenIdleAsync();
        Assert.Equal(0, Downloads);
        Assert.Equal(LiveTuningStatus.Closed, session.Status);
    }
}
