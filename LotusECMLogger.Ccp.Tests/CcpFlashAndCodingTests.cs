using System.Text;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>What replaced the RMA library: flash snapshots and coding commands over CCP.</summary>
public sealed class CcpFlashAndCodingTests
{
    private const uint CodingCmd = 0x40002770; // C132E0278
    private const uint EcuUnlocked = 0x40001678, UnlockMagic = 0x4000EFFA;

    private static (CcpSessionHost Host, Func<SimulatedCcpTransport> Transport) Session(FakeCcpSlave slave)
    {
        SimulatedCcpTransport? transport = null;
        var host = new CcpSessionHost(() => transport = new SimulatedCcpTransport(slave, speedFactor: 20),
            new CcpMasterOptions { CommandTimeout = TimeSpan.FromMilliseconds(500) });
        return (host, () => transport!);
    }

    [Fact]
    public async Task ASnapshotIsTheEcusFlashAndItsChecksumAgrees()
    {
        using var slave = new FakeCcpSlave();
        var region = T6FlashRegion.LearnedData;
        var flash = new byte[region.Length];
        new Random(278).NextBytes(flash);
        slave.Load(region.Address, flash);
        var (host, _) = Session(slave);
        using (host)
        {
            await using var lease = await host.AcquireAsync();
            int reported = 0;
            var bytes = await CcpFlashSnapshot.ReadAsync(lease.Master, region, new Progress<int>(n => reported = Math.Max(reported, n)));
            Assert.Equal(flash, bytes);
        }
    }

    [Fact]
    public void TheRegionsAreTheT6FlashLayout()
    {
        Assert.Equal((0x10000u, 0xC000), (T6FlashRegion.LearnedData.Address, T6FlashRegion.LearnedData.Length));
        Assert.Equal((0x20000u, 0x10000), (T6FlashRegion.Calibration.Address, T6FlashRegion.Calibration.Length));
        Assert.Equal((0x40000u, 0xC0000), (T6FlashRegion.Program.Address, T6FlashRegion.Program.Length));
        Assert.Equal(0x100000u, T6FlashRegion.Program.Address + (uint)T6FlashRegion.Program.Length); // the end of flash
    }

    [Fact]
    public async Task ACommandTheFirmwareTakesIsReportedExecuted()
    {
        using var slave = new FakeCcpSlave();
        var (host, transport) = Session(slave);
        using (host)
        {
            await using var lease = await host.AcquireAsync();
            // Stand in for service_coding_333ms on an unlocked ECU: take the command and clear the register.
            using var stop = new CancellationTokenSource();
            var firmware = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    transport().With(s => { if (s.Peek(CodingCmd) == 0x04) s.Load(CodingCmd, [0]); });
                    await Task.Delay(20);
                }
            });
            var outcome = await CcpCodingCommand.SendAsync(lease.Master, CodingCmd, 0x04, TimeSpan.FromSeconds(3));
            stop.Cancel();
            await firmware;
            Assert.Equal(CodingCommandOutcome.Executed, outcome);
        }
    }

    [Fact]
    public async Task ACommandALockedEcuIgnoresIsPutBack()
    {
        using var slave = new FakeCcpSlave();
        var (host, transport) = Session(slave);
        using (host)
        {
            await using var lease = await host.AcquireAsync();
            var outcome = await CcpCodingCommand.SendAsync(lease.Master, CodingCmd, 0x04, TimeSpan.FromMilliseconds(300));
            Assert.Equal(CodingCommandOutcome.NotExecuted, outcome);
            Assert.Equal(0, transport().With(s => s.Peek(CodingCmd))); // no command left to run later
        }
    }

    [Fact]
    public async Task TheUnlockStateIsReadFromTheFlagOrTheCalibrationField()
    {
        using var slave = new FakeCcpSlave();
        slave.Load(EcuUnlocked, [1]);
        slave.Load(UnlockMagic, Encoding.ASCII.GetBytes("WTF?"));
        var (host, transport) = Session(slave);
        using (host)
        {
            await using var lease = await host.AcquireAsync();
            Assert.True(await CcpCodingCommand.ReadUnlockedFlagAsync(lease.Master, EcuUnlocked));
            Assert.True(await CcpCodingCommand.ReadUnlockMagicAsync(lease.Master, UnlockMagic));
            transport().With(s => { s.Load(EcuUnlocked, [0]); s.Load(UnlockMagic, Encoding.ASCII.GetBytes("FAST")); });
            Assert.False(await CcpCodingCommand.ReadUnlockedFlagAsync(lease.Master, EcuUnlocked));
            Assert.False(await CcpCodingCommand.ReadUnlockMagicAsync(lease.Master, UnlockMagic));
        }
    }
}
