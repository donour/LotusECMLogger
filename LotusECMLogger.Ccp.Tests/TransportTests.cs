using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class TransportTests
{
    private static readonly byte[] AemPoll = [0x02, 0x01, 0x24, 0, 0, 0, 0, 0];

    [Fact]
    public void AuxiliaryFramesArriveOnlyAfterTheirIdIsAdmittedAndCarryTheirId()
    {
        using var slave = new FakeCcpSlave();
        slave.InjectFrame(0x7E9, [0x06, 0x41, 0x24]);
        Assert.Empty(slave.Read(0));
        slave.AddReceiveId(0x7E9);
        slave.InjectFrame(0x7E9, [0x06, 0x41, 0x24]);
        slave.Send(CcpFrame.Test(0));
        var frames = slave.Read(0);
        Assert.Equal(2, frames.Count);
        Assert.Equal(0x7E9u, frames[0].CanId);
        Assert.Equal(new byte[] { 0x06, 0x41, 0x24 }, frames[0].Payload.ToArray());
        Assert.Equal(CcpTarget.T6.DtoId, frames[1].CanId);
    }

    [Fact]
    public void AuxiliarySendReachesTheBusButIsNeverACcpCommand()
    {
        using var slave = new FakeCcpSlave();
        var sent = new List<(uint, byte[])>();
        slave.AuxiliarySent += (id, payload) => sent.Add((id, payload));
        slave.SendAuxiliary(0x7E1, AemPoll);
        var (canId, bytes) = Assert.Single(sent);
        Assert.Equal(0x7E1u, canId);
        Assert.Equal(AemPoll, bytes);
        Assert.Empty(slave.Read(0));
    }

    [Theory]
    [InlineData(0x350u, 8)] // CRO: commands must use Send so replies can be matched
    [InlineData(0x351u, 8)] // DTO
    [InlineData(0x800u, 8)] // not an 11-bit ID
    [InlineData(0x7E1u, 0)]
    [InlineData(0x7E1u, 9)]
    public void RejectsAuxiliaryFramesThatCouldBeConfusedWithCcp(uint canId, int length)
    {
        using var slave = new FakeCcpSlave();
        Assert.ThrowsAny<ArgumentException>(() => slave.SendAuxiliary(canId, new byte[length]));
    }

    [Fact]
    public void RejectsTheCroAsAReceiveId()
    {
        using var slave = new FakeCcpSlave();
        Assert.Throws<ArgumentException>(() => slave.AddReceiveId(CcpTarget.T6.CroId));
    }

    [Fact]
    public void SimulatedTransportAdvancesVirtualTimeWhileTheReceiveLoopWaits()
    {
        using var slave = new FakeCcpSlave();
        using var transport = new SimulatedCcpTransport(slave, speedFactor: 1000);
        slave.Load(0x10000, Enumerable.Repeat((byte)1, 1000).ToArray());
        transport.Send(CcpFrame.Connect(0));
        Assert.Single(transport.Read(0));
        transport.Send(CcpFrame.SetMta(1, 0x10000));
        Assert.Single(transport.Read(0));
        transport.Send(CcpFrame.BuildChecksum(2, 1000)); // four 256-byte passes = four virtual ms
        var reply = Enumerable.Range(0, 100).Select(_ => transport.Read(10)).First(f => f.Count > 0);
        var packet = CcpFrame.ParseDto(Assert.Single(reply).Payload.Span, slave.Target);
        packet.EnsureSuccess(CcpCommandCode.BuildChecksum, 2);
        Assert.InRange(transport.VirtualMilliseconds, 4, 1000);
    }

    [Fact]
    public void PausedSimulatedTransportOnlyAdvancesOnRequest()
    {
        using var slave = new FakeCcpSlave();
        using var transport = new SimulatedCcpTransport(slave, speedFactor: 1000) { Paused = true };
        transport.Send(CcpFrame.Connect(0));
        transport.Read(0);
        transport.Send(CcpFrame.BuildChecksum(1, 256));
        Assert.Empty(transport.Read(5));
        Assert.Equal(0, transport.VirtualMilliseconds);
        transport.Advance(1);
        Assert.Single(transport.Read(0));
    }
}
