using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class FakeCcpSlaveTests
{
    private static CcpPacket Exchange(FakeCcpSlave slave, byte[] command)
    {
        slave.Send(command);
        var frame = Assert.Single(slave.Read(0));
        var reply = CcpFrame.ParseDto(frame.Payload.Span, slave.Target);
        reply.EnsureSuccess((CcpCommandCode)command[0], command[1]);
        return reply;
    }
    private static void Configure(FakeCcpSlave slave, int list = 0, int lastOdt = 0, int prescaler = 1)
    {
        Exchange(slave, CcpFrame.GetDaqSize(1, slave.Target, list));
        Exchange(slave, CcpFrame.SetDaqPointer(2, slave.Target, list, 0, 0));
        Exchange(slave, CcpFrame.WriteDaq(3, 0x40002000, 2));
        Exchange(slave, CcpFrame.StartStop(4, slave.Target, CcpStartStopMode.Prepare,
            list, lastOdt, prescaler: prescaler));
        Exchange(slave, CcpFrame.StartStopAll(5, true));
    }

    [Fact]
    public void ReplaysExampleSessionAndStreamsRpmCoolantUntilStopped()
    {
        using var slave = new FakeCcpSlave();
        slave.Load(0x400017DA, [0x12, 0x34]);
        slave.Load(0x400018AE, [0x80]);
        Assert.Equal("FF0000FE00000000", Convert.ToHexString(Exchange(slave, CcpFrame.Connect(0)).Bytes.Span));
        Assert.Equal("FF00010201000000", Convert.ToHexString(Exchange(slave, CcpFrame.GetVersion(1)).Bytes.Span));
        var size = Exchange(slave, CcpFrame.GetDaqSize(2, CcpTarget.T6, 0));
        Assert.Equal((byte)12, size.Data.Span[0]);
        Assert.Equal((byte)0, size.Data.Span[1]);
        Exchange(slave, CcpFrame.SetDaqPointer(3, CcpTarget.T6, 0, 0, 0));
        Exchange(slave, CcpFrame.WriteDaq(4, 0x400017DA, 2));
        Exchange(slave, CcpFrame.SetDaqPointer(5, CcpTarget.T6, 0, 0, 1));
        Exchange(slave, CcpFrame.WriteDaq(6, 0x400018AE, 1));
        Exchange(slave, CcpFrame.StartStop(7, CcpTarget.T6, CcpStartStopMode.Prepare, 0));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Empty(slave.Read(0));
        Exchange(slave, CcpFrame.StartStopAll(8, true));
        slave.QueueEvent(0); slave.DrainDaq();
        Assert.Equal("0012348000000000", Convert.ToHexString(Assert.Single(slave.Read(0)).Payload.Span));
        Exchange(slave, CcpFrame.StartStopAll(9, false));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Empty(slave.Read(0));
        Exchange(slave, CcpFrame.Disconnect(10));
    }

    [Theory]
    [InlineData("C132E0278")] [InlineData("C132E0044")] [InlineData("C132F0395")]
    public void IdentifiesEachTargetAndUploadsItsId(string build)
    {
        using var slave = new FakeCcpSlave(CcpBuildProfile.Known.Single(p => p.Build == build));
        Exchange(slave, CcpFrame.Connect(0));
        var identification = CcpIdentification.Parse(Exchange(slave, CcpFrame.ExchangeId(1)));
        var bytes = new List<byte>();
        for (int remaining = identification.IdLength; remaining > 0; remaining -= Math.Min(5, remaining))
        {
            int size = Math.Min(5, remaining);
            bytes.AddRange(Exchange(slave, CcpFrame.Upload(2, size)).Data.Span[..size].ToArray());
        }
        Assert.Equal(slave.Target.SlaveId, Encoding.ASCII.GetString(bytes.ToArray()));
        Assert.Equal((byte)3, identification.AvailableResources);
        Assert.Equal((byte)0, identification.ProtectedResources);
        Assert.Equal((byte)0x8E, identification.DriverVersion);
    }

    [Fact]
    public void IgnoresCommandsBeforeConnectButTestDoesNotOpenSession()
    {
        using var slave = new FakeCcpSlave();
        slave.Send(CcpFrame.Upload(1, 1)); Assert.Empty(slave.Read(0));
        Exchange(slave, CcpFrame.Test(2));
        slave.Send(CcpFrame.Upload(3, 1)); Assert.Empty(slave.Read(0));
    }

    [Theory]
    [InlineData("C132E0278")] [InlineData("B13200091")] [InlineData("C132F0395")]
    public void DownloadsAdvanceMtaButLeaveReplyDataStale(string build)
    {
        using var slave = new FakeCcpSlave(CcpBuildProfile.Known.Single(p => p.Build == build));
        Exchange(slave, CcpFrame.Connect(0));
        var prior = Exchange(slave, CcpFrame.ExchangeId(1)).Data.ToArray();
        Exchange(slave, CcpFrame.SetMta(2, 0x40002000));
        Assert.Equal(prior, Exchange(slave, CcpFrame.Download(3, [10, 20, 30])).Data.ToArray());
        Assert.Equal((uint)0x40002003, slave.Mta0);
        Assert.Equal(prior, Exchange(slave, CcpFrame.Download6(4, [40, 50, 60, 70, 80, 90])).Data.ToArray());
        Exchange(slave, CcpFrame.SetMta(5, 0x40002000));
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, Exchange(slave, CcpFrame.Upload(6, 5)).Data.ToArray());
        Assert.Equal((uint)0x40002005, slave.Mta0);
        Assert.Equal(new byte[] { 60, 70, 80, 90 }, Exchange(slave, CcpFrame.Upload(7, 4)).Data.Span[..4].ToArray());
    }

    [Theory]
    [InlineData(255)] [InlineData(256)] [InlineData(257)] [InlineData(65535)]
    public void ChecksumCompletesAfter256ByteMainLoopPasses(int size)
    {
        using var slave = new FakeCcpSlave();
        byte[] bytes = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        slave.Load(0x10000, bytes);
        Exchange(slave, CcpFrame.Connect(0)); Exchange(slave, CcpFrame.SetMta(1, 0x10000));
        slave.Send(CcpFrame.BuildChecksum(2, size)); Assert.Empty(slave.Read(0));
        int passes = (size + 255) / 256;
        for (int pass = 1; pass < passes; pass++)
        { slave.AdvanceMainLoop(); Assert.Empty(slave.Read(0)); }
        slave.AdvanceMainLoop();
        var reply = CcpFrame.ParseDto(Assert.Single(slave.Read(0)).Payload.Span, slave.Target);
        reply.EnsureSuccess(CcpCommandCode.BuildChecksum, 2);
        Assert.Equal((byte)2, reply.Data.Span[0]);
        Assert.Equal(CcpFrame.AdditiveChecksum(bytes), BinaryPrimitives.ReadUInt16BigEndian(reply.Data.Span[1..]));
        Assert.Equal((uint)0x10000, slave.Mta0); // checksum advances MTA1, never MTA0
    }

    [Fact]
    public void Raw64KiBChecksumTruncatesToZeroAndNeverReplies()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0));
        slave.Send([0x0E, 1, 0, 1, 0, 0, 0, 0]);
        slave.AdvanceMainLoop();
        Assert.Equal(0, slave.ChecksumRemaining);
        Assert.Empty(slave.Read(0));
    }

    [Fact]
    public void ShortUploadDuringChecksumCorruptsWorkingPointerAndReplyCounter()
    {
        using var slave = new FakeCcpSlave();
        slave.Load(0x10000, Enumerable.Repeat((byte)1, 300).ToArray());
        slave.Load(0x20000, Enumerable.Repeat((byte)2, 100).ToArray());
        Exchange(slave, CcpFrame.Connect(0)); Exchange(slave, CcpFrame.SetMta(1, 0x10000));
        slave.Send(CcpFrame.BuildChecksum(2, 300)); slave.AdvanceMainLoop();
        Exchange(slave, CcpFrame.ShortUpload(3, 0x20000, 1));
        slave.AdvanceMainLoop();
        var reply = CcpFrame.ParseDto(Assert.Single(slave.Read(0)).Payload.Span, slave.Target);
        Assert.Equal((byte)3, reply.Counter);
        Assert.Equal((ushort)344, BinaryPrimitives.ReadUInt16BigEndian(reply.Data.Span[1..]));
    }

    [Fact]
    public void ElementSevenAliasesTheNextOdt()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0));
        Exchange(slave, CcpFrame.GetDaqSize(1, slave.Target, 0));
        // Bypass the master guard to reproduce the slave's accepted-but-invalid element.
        Exchange(slave, [0x15, 2, 0, 0, 7, 0, 0, 0]);
        Exchange(slave, CcpFrame.WriteDaq(3, 0x40002000, 2));
        Assert.Equal(new FakeCcpSlave.Entry(0x40002000, 2), slave.DaqEntry(0, 1, 0));
    }

    [Fact]
    public void ReceiveBatchPreservesDaqAlongsideMemoryReplyAndHardwareTimestampWrap()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave);
        slave.Load(0x40002000, [0x12, 0x34]);
        slave.Timestamp = uint.MaxValue;
        slave.QueueEvent(0); slave.DrainDaq();
        slave.Send(CcpFrame.ShortUpload(6, 0x40002000, 2));
        var frames = slave.Read(0);
        Assert.Equal(2, frames.Count);
        Assert.Equal(uint.MaxValue, frames[0].TimestampMicroseconds);
        Assert.Equal((uint)0, frames[1].TimestampMicroseconds);
        Assert.Equal(CcpPacketKind.Daq, CcpFrame.ParseDto(frames[0].Payload.Span, slave.Target).Kind);
        Assert.Equal(CcpPacketKind.CommandReturn, CcpFrame.ParseDto(frames[1].Payload.Span, slave.Target).Kind);
    }

    [Fact]
    public void GetDaqSizeDropsQueuedFramesFromOtherLists()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave);
        slave.QueueEvent(0);
        Exchange(slave, CcpFrame.GetDaqSize(6, slave.Target, 1));
        slave.DrainDaq(); Assert.Empty(slave.Read(0));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0)); // list 0 still active
    }

    [Fact]
    public void QueueOverrunSetsBitSevenOnTheNextSuccessfulListFrame()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave, lastOdt: 11);
        for (int i = 0; i < 11; i++) slave.QueueEvent(0); // capacity = 120, eleventh burst overflows
        slave.DrainDaq(); Assert.Equal(120, slave.Read(0).Count);
        slave.QueueEvent(0); slave.DrainDaq();
        var next = slave.Read(0);
        Assert.Equal(12, next.Count);
        Assert.True(CcpFrame.ParseDto(next[0].Payload.Span, slave.Target).Overrun);
        Assert.False(CcpFrame.ParseDto(next[1].Payload.Span, slave.Target).Overrun);
    }

    [Fact]
    public void PrescalerFiresOnFirstEventThenEveryNthEvent()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave, prescaler: 2);
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Empty(slave.Read(0));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0));
    }

    [Fact]
    public void TemporaryDisconnectKeepsDaqButEndSessionStopsIt()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave);
        Exchange(slave, CcpFrame.Disconnect(6, false));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0));
        slave.Send(CcpFrame.ShortUpload(7, 0x40002000, 1)); Assert.Empty(slave.Read(0));
        Exchange(slave, CcpFrame.Connect(8)); // reconnect from temporary state preserves list
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0));
        Exchange(slave, CcpFrame.Disconnect(9));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Empty(slave.Read(0));
    }

    [Fact]
    public void TestToAnotherStationTemporarilyDisconnectsWithoutReply()
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0)); Configure(slave);
        slave.Send([5, 6, 1, 0, 0, 0, 0, 0]); Assert.Empty(slave.Read(0));
        slave.Send(CcpFrame.Upload(7, 1)); Assert.Empty(slave.Read(0));
        slave.QueueEvent(0); slave.DrainDaq(); Assert.Single(slave.Read(0));
    }

    [Theory]
    [InlineData(0x12, 0, 0, 0x30)] // GET_SEED is unsupported
    [InlineData(0x02, 1, 0, 0x32)] // only MTA0 is accepted
    [InlineData(0x06, 2, 0, 0x22)] // no GET_DAQ_SIZE yet
    [InlineData(0x15, 10, 0, 0x31)] // invalid DAQ list
    public void ReturnsFirmwareErrorsForRejectedRawCommands(byte command, byte parameter2,
        byte parameter3, byte error)
    {
        using var slave = new FakeCcpSlave();
        Exchange(slave, CcpFrame.Connect(0));
        slave.Send([command, 1, parameter2, parameter3, 0, 0, 0, 0]);
        var reply = CcpFrame.ParseDto(Assert.Single(slave.Read(0)).Payload.Span, slave.Target);
        Assert.Equal(error, reply.ErrorCode);
        Assert.Equal((byte)1, reply.Counter);
    }

    [Fact]
    public void WritingEnableByteZeroStopsFurtherResponses()
    {
        var profile = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        using var slave = new FakeCcpSlave(profile);
        Exchange(slave, CcpFrame.Connect(0));
        Exchange(slave, CcpFrame.SetMta(1, profile.EnableByteAddress!.Value));
        // The real TX path is gated too: this command can also lose its ACK.
        slave.Send(CcpFrame.Download(2, [0])); Assert.Empty(slave.Read(0));
        Assert.Equal((byte)0, slave.Peek(profile.EnableByteAddress.Value));
        slave.Send(CcpFrame.Connect(3)); Assert.Empty(slave.Read(0));
    }
}
