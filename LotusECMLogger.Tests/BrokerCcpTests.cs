using System.Diagnostics;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;
using LotusECMLogger.Tests.Fakes;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// CCP on the broker (J2534-session-broker-plan.md, Phase 5): the real <see cref="CcpSessionHost"/> and
    /// <see cref="CcpMaster"/> over <see cref="BrokerCcpTransport"/>, the broker's shared CAN channel and
    /// <see cref="FakeJ2534Backend"/>, with the simulated T6 slave answering on the fake bus.
    /// </summary>
    public sealed class BrokerCcpTests
    {
        private const uint CalBase = 0x40008654; // C132E0278, FakeCcpSlave's default build

        private sealed class Bench : IDisposable
        {
            public FakeJ2534Backend Adapter { get; } = new();
            public FakeCcpSlave Slave { get; } = new();
            public J2534Broker Broker { get; }
            public CcpSessionHost Host { get; }

            public Bench(BrokerOptions? options = null)
            {
                Broker = new J2534Broker(Adapter, options);
                Host = new CcpSessionHost(() => new BrokerCcpTransport(Broker, CcpTarget.T6));
                // The ECU on the bus: CROs to 0x350 reach the slave, and its replies go out on 0x351.
                Adapter.CanFrameSent = (canId, data) =>
                {
                    if (canId != CcpTarget.T6.CroId) return;
                    lock (Slave)
                    {
                        Slave.Send(data);
                        foreach (var frame in Slave.Read(0)) Adapter.InjectCan(frame.CanId, frame.Payload.ToArray());
                    }
                };
                Slave.Load(CalBase, [0xC1, 0x32, 0xE0, 0x27]);
            }

            public void Dispose()
            {
                Host.Dispose();
                Broker.Dispose();
            }
        }

        [Fact]
        public async Task Session_ConnectsReadsAndHoldsASharedLease()
        {
            using var bench = new Bench();

            await using (var session = await bench.Host.AcquireAsync())
            {
                Assert.Equal([0xC1, 0x32, 0xE0, 0x27], await session.Master.ShortUploadAsync(CalBase, 4));
                var lease = Assert.Single(bench.Broker.State.Leases);
                Assert.Equal(new BrokerLeaseInfo(LeaseKind.Shared, "the CCP session", null), lease);
                Assert.Equal(1, bench.Adapter.DevicesOpen);
            }

            // The last CCP lease ends the session, which gives the adapter back.
            Assert.Empty(bench.Broker.State.Leases);
            Assert.Equal(0, bench.Adapter.DevicesOpen);
        }

        [Fact]
        public async Task Session_RunsAlongsideIsoDiagnostics()
        {
            using var bench = new Bench();
            bench.Adapter.Reply(ECUDefinition.ECM, [0x03], [0x43, 0x01, 0x03, 0x01])
                .Reply(ECUDefinition.ECM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.ECM, [0x0A], [0x4A, 0x00])
                .Reply(ECUDefinition.TCM, [0x03], [0x43, 0x00])
                .Reply(ECUDefinition.TCM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.TCM, [0x0A], [0x4A, 0x00]);
            await using var session = await bench.Host.AcquireAsync();

            // CCP reads and DTC reads at the same time, on one device.
            using var stop = new CancellationTokenSource();
            int ccpReads = 0;
            var ccp = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    Assert.Equal(0xC1, (await session.Master.ShortUploadAsync(CalBase, 4))[0]);
                    ccpReads++;
                }
            });
            for (int i = 0; i < 3; i++)
            {
                var (success, error, result) = new J2534DtcService(bench.Broker).ReadCodes();
                Assert.True(success, error);
                Assert.Equal(["P0301"], result.Stored.Select(c => c.Code));
            }
            stop.Cancel();
            await ccp;

            Assert.True(ccpReads > 10, $"{ccpReads} CCP reads");
            Assert.Equal(1, bench.Adapter.DevicesOpened);
            Assert.Equal(CcpSessionState.Connected, session.Master.State);
        }

        [Fact]
        public async Task Session_HoldsTheEcmAgainstWritesButNotTheTransmission()
        {
            using var bench = new Bench();
            bench.Adapter.Reply(ECUDefinition.TCM, [0x04], [0x44]);
            await using var session = await bench.Host.AcquireAsync();

            const string held = "The ECM is in use: the CCP session is running.";
            Assert.Equal((false, held), new J2534VinSetService(bench.Broker).SetVin("SCCLMDTU9BHA10001"));
            Assert.Equal((false, held), new J2534DtcService(bench.Broker).ClearCodes());
            Assert.Equal((true, ""), new J2534DtcService(bench.Broker).ClearTcuCodes());
        }

        [Fact]
        public async Task Session_AndObdLoggingRefuseEachOther()
        {
            using var bench = new Bench();
            using (bench.Broker.Acquire(LeaseKind.DeviceExclusive, J2534LoggingService.LeaseHolder))
            {
                var ex = await Assert.ThrowsAsync<BrokerBusyException>(() => bench.Host.AcquireAsync());
                Assert.Equal("The adapter is in use: OBD-II logging is running.", ex.Message);
                Assert.Equal(0, bench.Host.LeaseCount);
                Assert.Single(bench.Broker.State.Leases);
            }

            await using var session = await bench.Host.AcquireAsync();
            var refused = Assert.Throws<BrokerBusyException>(() => bench.Broker.Acquire(LeaseKind.DeviceExclusive, J2534LoggingService.LeaseHolder));
            Assert.Equal("The adapter is in use: the CCP session is running.", refused.Message);
        }

        [Fact]
        public async Task Commands_PollFastEvenWhenTheIdlePollIsSlow()
        {
            // With a 50 ms idle poll, only the fast polling held while a command waits keeps replies quick.
            using var bench = new Bench(new BrokerOptions { CanIdlePollMs = 50 });
            await using var session = await bench.Host.AcquireAsync();

            var times = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var clock = Stopwatch.StartNew();
                await session.Master.ShortUploadAsync(CalBase, 4);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            double median = times.Order().ElementAt(times.Count / 2);
            Assert.True(median < 20, $"median {median:0.0} ms");

            // Idle again: no command waits, so the reader is back on the slow poll.
            var can = bench.Adapter.OpenChannels(BrokerProtocol.Can).Single();
            await Task.Delay(30);
            // Against the measured window: on a busy CI runner a 200 ms delay can last far longer, and the
            // slow poll's reads grow with it. Fast polling would read at least every ~16 ms (the default timer).
            int before = can.Reads.Count;
            var window = Stopwatch.StartNew();
            await Task.Delay(200);
            int reads = can.Reads.Count - before;
            double elapsed = window.Elapsed.TotalMilliseconds;
            Assert.True(reads <= elapsed / 50 + 4, $"{reads} reads in {elapsed:0} ms idle");
        }

        [Fact]
        public async Task AuxiliaryIds_JoinTheSessionsStream()
        {
            using var bench = new Bench();
            await using var session = await bench.Host.AcquireAsync();

            // The AEM wideband's OBD reply ID, as the high-speed logger adds it.
            using var aem = session.Master.SubscribeAuxiliary(0x7E9);
            bench.Adapter.InjectCan(0x7E9, [0x04, 0x41, 0x24, 0x80, 0x00]);
            bench.Adapter.InjectCan(0x7EA, [0x04, 0x41, 0x24, 0x00, 0x00]); // not subscribed

            using var timeout = new CancellationTokenSource(1000);
            var frame = await aem.Reader.ReadAsync(timeout.Token);
            Assert.Equal(0x7E9u, frame.CanId);
            Assert.Equal(0x80, frame.Payload.Span[3]);
            Assert.Equal(2, bench.Adapter.OpenChannels(BrokerProtocol.Can).Single().FilterCount); // DTO + 0x7E9
        }

        [Fact]
        public async Task UnpluggedAdapter_FaultsTheSessionAndTheNextOneReopens()
        {
            using var bench = new Bench();
            var session = await bench.Host.AcquireAsync();
            bench.Adapter.OpenChannels(BrokerProtocol.Can).Single().ReadError = new J2534Exception(
                SAE.J2534.ResultCode.DEVICE_NOT_CONNECTED, "unplugged");

            var deadline = Environment.TickCount64 + 2000;
            while (session.Master.State != CcpSessionState.Faulted && Environment.TickCount64 < deadline) await Task.Delay(10);
            Assert.Equal(CcpSessionState.Faulted, session.Master.State);
            await session.DisposeAsync();
            Assert.Equal(0, bench.Adapter.DevicesOpen);

            await using var again = await bench.Host.AcquireAsync();
            Assert.Equal(0xC1, (await again.Master.ShortUploadAsync(CalBase, 4))[0]);
            Assert.Equal(2, bench.Adapter.DevicesOpened);
        }
    }
}
