using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public sealed class CcpMasterMemoryTests : IDisposable
{
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static readonly CcpMemoryRange Calibration = CcpMemoryRange.Calibration(Gt430);
    private readonly FakeCcpSlave _slave = new();
    private readonly SimulatedCcpTransport _simulation;
    private readonly ScriptedTransport _script;
    private readonly CcpMaster _master;

    public CcpMasterMemoryTests()
    {
        _simulation = new SimulatedCcpTransport(_slave, speedFactor: 200);
        _script = new ScriptedTransport(_simulation);
        _master = new CcpMaster(_script, new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(150) });
    }

    public void Dispose()
    {
        _master.Dispose();
        _slave.Dispose();
    }

    private async Task ConnectAsync()
    {
        await _master.ConnectAsync();
        _script.ClearSent();
    }

    private static byte[] Pattern(int length, int seed = 0) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * 7 + seed)).ToArray();

    [Fact]
    public async Task ReadSetsMtaOnceThenUploadsInFiveByteSteps()
    {
        var bytes = Pattern(12);
        _simulation.With(s => s.Load(0x40001000, bytes));
        await ConnectAsync();
        var progress = new List<int>();
        Assert.Equal(bytes, await _master.ReadAsync(0x40001000, 12, new SyncProgress(progress)));
        Assert.Equal(["0202000040001000", "0403050000000000", "0404050000000000", "0405020000000000"], _script.SentHex);
        Assert.Equal([5, 10, 12], progress);
    }

    [Fact]
    public async Task ShortUploadReadsOneValue()
    {
        _simulation.With(s => s.Load(0x40001764, [0x0B, 0xB8]));
        await ConnectAsync();
        Assert.Equal(new byte[] { 0x0B, 0xB8 }, await _master.ShortUploadAsync(0x40001764, 2));
        Assert.Equal(["0F02020040001764"], _script.SentHex);
    }

    [Theory]
    [InlineData(0x40001000u, 0)]
    [InlineData(0xFFFFFFFEu, 3)]
    public async Task InvalidRangesAreRejectedBeforeAnythingIsSent(uint address, int length)
    {
        await ConnectAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _master.ReadAsync(address, length); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _master.ChecksumAsync(address, length); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _master.ShortUploadAsync(0x40001000, 6); });
        Assert.Empty(_script.Sent);
    }

    [Fact]
    public async Task WriteUsesDownload6ThenDownloadAndReadsBack()
    {
        var bytes = Pattern(14, 3);
        await ConnectAsync();
        await _master.WriteVerifiedAsync(Calibration.Start, bytes, Calibration);
        Assert.Equal(bytes, _simulation.With(s => Enumerable.Range(0, 14).Select(i => s.Peek(Calibration.Start + (uint)i)).ToArray()));
        var commands = _script.Sent.Select(c => (CcpCommandCode)c[0]).ToArray();
        Assert.Equal([CcpCommandCode.SetMta, CcpCommandCode.Download6, CcpCommandCode.Download6, CcpCommandCode.Download,
            CcpCommandCode.SetMta, CcpCommandCode.Upload, CcpCommandCode.Upload, CcpCommandCode.Upload], commands);
        Assert.Equal((byte)2, _script.Sent[3][2]); // final DNLOAD carries the two remaining bytes
    }

    [Fact]
    public async Task WritesOutsideTheAllowedRangeAreRefusedBeforeAnythingIsSent()
    {
        await ConnectAsync();
        uint end = Calibration.Start + Calibration.Length;
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _master.WriteVerifiedAsync(end - 1, new byte[2], Calibration); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _master.WriteVerifiedAsync(Calibration.Start - 1, new byte[1], Calibration); });
        Assert.Empty(_script.Sent);
    }

    [Fact]
    public async Task AcknowledgedWriteThatDoesNotStickFailsVerification()
    {
        await ConnectAsync();
        // Acknowledge downloads without applying them, as a store to flash would.
        _script.Reply = cro => cro[0] is (byte)CcpCommandCode.Download or (byte)CcpCommandCode.Download6
            ? ScriptedTransport.Crm(cro[1]) : null;
        var error = await Assert.ThrowsAsync<CcpWriteVerificationException>(
            () => _master.WriteVerifiedAsync(Calibration.Start + 2, new byte[] { 0, 9, 9 }, Calibration));
        Assert.Equal(Calibration.Start + 3, error.FirstMismatchAddress);
        Assert.Equal(2, error.MismatchedBytes);
        Assert.Equal(CcpSessionState.Connected, _master.State);
    }

    [Fact]
    public async Task InterruptedWriteReportsAcknowledgedBytesAndIsNotRetried()
    {
        await ConnectAsync();
        int downloads = 0;
        _script.Drop = cro => cro[0] == (byte)CcpCommandCode.Download6 && ++downloads == 2;
        var error = await Assert.ThrowsAsync<CcpWriteException>(
            () => _master.WriteVerifiedAsync(Calibration.Start, Pattern(18), Calibration));
        Assert.Equal(6, error.BytesAcknowledged);
        Assert.IsType<CcpTimeoutException>(error.InnerException);
        Assert.Equal(CcpSessionState.Uncertain, _master.State);
        Assert.Equal(2, _script.Sent.Count(c => c[0] == (byte)CcpCommandCode.Download6));
    }

    [Fact]
    public async Task ChecksumSplitsLargeRegionsIntoBlocksAndSumsThem()
    {
        var bytes = Pattern(70000, 1);
        _simulation.With(s => s.Load(0x10000, bytes));
        await ConnectAsync();
        Assert.Equal(CcpFrame.AdditiveChecksum(bytes), await _master.ChecksumAsync(0x10000, bytes.Length));
        var sizes = _script.Sent.Where(c => c[0] == (byte)CcpCommandCode.BuildChecksum)
            .Select(c => (int)BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(4))).ToArray();
        Assert.Equal([0x8000, 0x8000, 70000 - 0x10000], sizes);
        var mtas = _script.Sent.Where(c => c[0] == (byte)CcpCommandCode.SetMta)
            .Select(c => BinaryPrimitives.ReadUInt32BigEndian(c.AsSpan(4))).ToArray();
        Assert.Equal([0x10000u, 0x18000u, 0x20000u], mtas);
    }

    [Fact]
    public async Task IdentifiesTheBuildFromTheCalibrationIdAndReadsItsDescriptionAndRegistryHeader()
    {
        _simulation.With(s =>
        {
            s.Load(Gt430.CalibrationBase, Encoding.ASCII.GetBytes("C132E0278 CAL ID padding......."));
            s.Load(Gt430.CalibrationBase + 0x59D4, Encoding.ASCII.GetBytes("LOTUS EVORA GT430 MAN MY19 ROW  "));
            s.Load(Gt430.SymbolEntriesAddress!.Value - 44, Encoding.ASCII.GetBytes("T6-EVORA-GT430-V000E 24/01/2018"));
            s.Load(Gt430.SymbolEntriesAddress!.Value - 12, Encoding.ASCII.GetBytes("V0001"));
        });
        await ConnectAsync();
        var identification = await _master.IdentifyAsync();
        Assert.Equal("Ccp_T6", identification.Slave.SlaveId);
        Assert.Same(Gt430, identification.Build);
        Assert.Equal("C132E0278", identification.CalibrationId);
        Assert.Equal("LOTUS EVORA GT430 MAN MY19 ROW", identification.CalibrationDescription);
        Assert.Equal("T6-EVORA-GT430-V000E 24/01/2018", identification.RegistryBuildId);
        Assert.Equal("V0001", identification.RegistryVersion);
    }

    [Fact]
    public async Task UnknownCalibrationIdLeavesTheBuildForTheUserToPick()
    {
        _simulation.With(s => s.Load(Gt430.CalibrationBase, Encoding.ASCII.GetBytes("X999E9999 ")));
        await ConnectAsync();
        var identification = await _master.IdentifyAsync();
        Assert.Equal("Ccp_T6", identification.Slave.SlaveId);
        Assert.Null(identification.Build);
    }

    [Fact]
    public async Task ReadsTheRegistryUntilItsTerminator()
    {
        uint entries = Gt430.SymbolEntriesAddress!.Value;
        var table = new byte[44 + 70 * 6 + 6];
        Encoding.ASCII.GetBytes("TEST PROGRAM").CopyTo(table, 0);
        Encoding.ASCII.GetBytes("V0001").CopyTo(table, 32);
        for (int i = 0; i < 70; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(44 + i * 6), 0x40000000u + (uint)i * 4);
            table[44 + i * 6 + 4] = (byte)(1 << (i % 3));
        }
        _simulation.With(s => s.Load(entries - 44, table));
        await ConnectAsync();
        var registry = await _master.ReadSymbolRegistryAsync(Gt430);
        Assert.Equal(70, registry.Entries.Count); // spans two 64-entry chunks
        Assert.Equal("TEST PROGRAM", registry.BuildId);
        Assert.Equal(new T6SymbolEntry(69, 0x40000000u + 69 * 4, 1), registry.Entries[69]);
    }

    [Fact]
    public async Task MemoryOperationsRequireAConnectedSession()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.ReadAsync(0x40001000, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.WriteVerifiedAsync(Calibration.Start, new byte[1], Calibration));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _master.ChecksumAsync(0x40001000, 1));
        Assert.Empty(_script.Sent);
    }

    /// <summary>Records synchronously; Progress&lt;T&gt; posts to the thread pool and would race the assertion.</summary>
    private sealed class SyncProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) { lock (values) values.Add(value); }
    }
}
