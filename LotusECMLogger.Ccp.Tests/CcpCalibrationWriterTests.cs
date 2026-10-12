using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpCalibrationWriterTests : IDisposable
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static readonly uint Cal = Gt430.CalibrationBase;
    private static readonly uint Enable = Gt430.EnableByteAddress!.Value;
    private static readonly CcpEngineSpeedSymbol Rpm = new(0x40001762, 2, 1);

    private readonly FakeCcpSlave _slave = new();
    private readonly SimulatedCcpTransport _simulation;
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;
    private readonly string _backups = Path.Combine(Path.GetTempPath(), $"ccp-backups-{Guid.NewGuid():N}");
    private readonly CcpCalibrationWriter _writer;

    public CcpCalibrationWriterTests()
    {
        _slave.Load(Cal, Encoding.ASCII.GetBytes("C132E0278 test calibration"));
        _slave.Load(Cal + 0x100, Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        _simulation = new SimulatedCcpTransport(_slave, speedFactor: 200);
        _script = new ScriptedTransport(_simulation);
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
        _writer = new CcpCalibrationWriter(_master, Gt430, CcpBuildSource.Identified, _backups, Rpm);
        _master.ConnectAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _master.Dispose();
        _slave.Dispose();
        if (Directory.Exists(_backups)) Directory.Delete(_backups, recursive: true);
    }

    private byte[] Peek(uint address, int length) =>
        _simulation.With(s => Enumerable.Range(0, length).Select(i => s.Peek(address + (uint)i)).ToArray());

    private int Downloads => _script.Sent.Count(c => c[0] is (byte)CcpCommandCode.Download or (byte)CcpCommandCode.Download6);

    private byte[] FullImage()
    {
        var image = Peek(Cal, (int)Gt430.CalibrationSize);
        return image;
    }

    [Fact]
    public async Task BacksUpThenWritesOnlyTheChangedRunsAndVerifiesThem()
    {
        var target = Peek(Cal + 0x100, 32);
        target[2] = 0xAA; target[3] = 0xBB;   // run 1
        target[20] = 0xCC;                     // run 2 (gap of 16 is not merged)
        var plan = await _writer.PrepareAsync(Cal + 0x100, target);
        Assert.Equal([new CcpWriteRun(Cal + 0x102, 2), new CcpWriteRun(Cal + 0x114, 1)], plan.Runs);
        Assert.Equal(3, plan.ChangedBytes);
        Assert.Equal(0.0, plan.EngineSpeedRpm);
        Assert.Equal(0, Downloads); // preparing writes nothing

        var original = Peek(Cal + 0x100, 32);
        var result = await _writer.ExecuteAsync(plan);
        Assert.Equal(2, result.RunsWritten);
        Assert.Equal(target, Peek(Cal + 0x100, 32));
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(2, Downloads);
        Assert.True(CcpCalibrationWriter.TryParseBackupName(result.BackupPath!, out var build, out uint address, out int length));
        Assert.Equal(("C132E0278", Cal + 0x100, 32), (build, address, length));
    }

    [Fact]
    public void NearbyRunsAreMerged()
    {
        byte[] current = new byte[20], target = new byte[20];
        target[1] = target[6] = target[15] = 1; // gaps of 4 (merged) and 8 (not merged)
        Assert.Equal([new CcpWriteRun(101, 6), new CcpWriteRun(115, 1)], CcpCalibrationWriter.ChangedRuns(100, current, target));
    }

    [Theory]
    [InlineData(-1, 2)]   // starts before calibration RAM
    [InlineData(0x69AB, 2)] // runs past its end
    public async Task RefusesWritesOutsideCalibrationRam(int offset, int length)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _writer.PrepareAsync((uint)(Cal + offset), new byte[length]));
        Assert.Equal(0, Downloads);
    }

    [Fact]
    public async Task RefusesWhileTheEngineTurns()
    {
        _simulation.With(s => s.Load(Rpm.Address, [0x03, 0x20])); // 800 rpm
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.PrepareAsync(Cal + 0x100, new byte[] { 9 }));
        Assert.Contains("800 rpm", error.Message);
        Assert.Equal(0, Downloads);
    }

    [Fact]
    public async Task AValueWriteMustNotChangeTheEnableByte()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.PrepareAsync(Enable - 1, new byte[] { 0, 0, 0 }));
        Assert.Contains("enable byte", error.Message);
        var plan = await _writer.PrepareAsync(Enable, Peek(Enable, 1)); // its own value is fine (and a no-op)
        Assert.Empty(plan.Runs);
    }

    [Fact]
    public async Task AWholeImageKeepsTheEcusEnableByte()
    {
        var image = FullImage();
        image[Enable - Cal] = 0; // the file would disable CCP
        image[0x200] ^= 0xFF;
        var plan = await _writer.PrepareAsync(Cal, _writer.CalibrationImageFromFile(image), wholeImage: true);
        Assert.Contains("kept the ECU's 0x01", plan.EnableBytePreserved);
        Assert.Equal([new CcpWriteRun(Cal + 0x200, 1)], plan.Runs);
        await _writer.ExecuteAsync(plan);
        Assert.Equal((byte)1, Peek(Enable, 1)[0]);
        Assert.Equal(CcpSessionState.Connected, _master.State); // CCP still answers
        Assert.Equal("Ccp_T6", (await _master.ExchangeIdAsync()).SlaveId);
    }

    [Fact]
    public void CalibrationFilesMustMatchTheBuildAndASupportedSize()
    {
        var image = FullImage();
        Assert.Equal(image, _writer.CalibrationImageFromFile(image));

        var snapshot = new byte[CcpCalibrationWriter.SnapshotCalibrationSize];
        image.CopyTo(snapshot, 0);
        Assert.Equal(image, _writer.CalibrationImageFromFile(snapshot));

        Assert.Throws<InvalidDataException>(() => _writer.CalibrationImageFromFile(image[..^1]));
        var other = (byte[])image.Clone();
        Encoding.ASCII.GetBytes("C132E0271 ").CopyTo(other, 0);
        Assert.Contains("C132E0271", Assert.Throws<InvalidDataException>(() => _writer.CalibrationImageFromFile(other)).Message);
    }

    [Fact]
    public async Task NothingToChangeWritesNothing()
    {
        var plan = await _writer.PrepareAsync(Cal + 0x100, Peek(Cal + 0x100, 16));
        var result = await _writer.ExecuteAsync(plan);
        Assert.Null(result.BackupPath);
        Assert.Equal(0, Downloads);
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public async Task RefusesIfTheRegionChangedSinceItWasPrepared()
    {
        var plan = await _writer.PrepareAsync(Cal + 0x100, new byte[] { 0xEE });
        _simulation.With(s => s.Load(Cal + 0x100, [0x42]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.ExecuteAsync(plan));
        Assert.Equal(0, Downloads);
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public async Task AnInterruptedWriteReportsItsProgressAndBackup()
    {
        var target = Peek(Cal + 0x100, 32);
        target[0] ^= 1; target[30] ^= 1;
        var plan = await _writer.PrepareAsync(Cal + 0x100, target);
        int downloads = 0;
        _script.Drop = cro => cro[0] is (byte)CcpCommandCode.Download or (byte)CcpCommandCode.Download6 && ++downloads == 2;
        var error = await Assert.ThrowsAsync<CcpCalibrationWriteException>(() => _writer.ExecuteAsync(plan));
        Assert.Equal(1, error.RunsWritten);
        Assert.True(File.Exists(error.BackupPath));
        Assert.Equal(CcpSessionState.Uncertain, _master.State);
    }

    [Fact]
    public void TheWriterMustMatchTheSessionsTarget()
    {
        var tcu = CcpBuildProfile.Known.Single(p => p.Build == "C132F0395");
        Assert.Throws<ArgumentException>(() => new CcpCalibrationWriter(_master, tcu, CcpBuildSource.UserOverride, _backups));
    }
}
