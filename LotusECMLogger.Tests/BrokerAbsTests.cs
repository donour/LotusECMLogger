using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Tests.Fakes;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The ABS/ESP service on the broker (J2534-session-broker-plan.md, Phase 6), against
    /// <see cref="FakeJ2534Backend"/>: KWP reads share the adapter, the passive telemetry runs alongside other
    /// traffic on three CAN filters, actuation holds the ABS module, and the probe and sniff hold the adapter.
    /// </summary>
    public sealed class BrokerAbsTests
    {
        private static readonly ECUDefinition Abs = ECUDefinition.ABS;

        private static (J2534Broker Broker, FakeJ2534Backend Adapter, J2534AbsService Service) Create()
        {
            var adapter = new FakeJ2534Backend();
            var broker = new J2534Broker(adapter);
            return (broker, adapter, new J2534AbsService(broker));
        }

        /// <summary>
        /// Broadcasts the three telemetry frames as all zeros (stationary, brake released, no intervention) until
        /// disposed, as the module does at 100 Hz.
        /// </summary>
        private static IDisposable BroadcastTelemetry(FakeJ2534Backend adapter)
        {
            var stop = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    adapter.InjectCan(0xA2, new byte[8]);
                    adapter.InjectCan(0xA4, new byte[8]);
                    adapter.InjectCan(0xA8, new byte[8]);
                    await Task.Delay(10);
                }
            });
            return new Stopper(stop, task);
        }

        private sealed class Stopper(CancellationTokenSource stop, Task task) : IDisposable
        {
            public void Dispose()
            {
                stop.Cancel();
                task.Wait();
                stop.Dispose();
            }
        }

        [Fact]
        public void FaultCodes_AreReadOverTheSharedIsoChannel()
        {
            var (broker, adapter, service) = Create();
            // ReadDtcByStatus with one stored code (3-byte entries), then ReadStatusOfDtc for it.
            adapter.Reply(Abs, [0x18, 0x00, 0xFF, 0x00], [0x58, 0x01, 0x50, 0x12, 0xE0])
                .Reply(Abs, [0x17, 0x50, 0x12], [0x57, 0x01, 0x50, 0x12, 0xE0]);
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");
            using var ccpLink = ccp.OpenCan(ECUDefinition.ECM);

            var (success, error, result) = service.ReadDtcs();

            Assert.True(success, error);
            Assert.Single(result.Codes);
            Assert.Equal(1, adapter.DevicesOpened); // alongside the CCP session, on its device
            Assert.Single(broker.State.Leases);    // only the CCP session's lease is left
        }

        [Fact]
        public void Telemetry_RunsAlongsideOtherTrafficOnThreeFilters()
        {
            var (broker, adapter, service) = Create();
            adapter.Reply(Abs, [0x18, 0x00, 0xFF, 0x00], [0x58, 0x00]);
            var samples = new ManualResetEventSlim(false);
            string? telemetryError = null;
            service.TelemetryReceived += (_, sample) => { if (sample.HasData) samples.Set(); };
            service.TelemetryError += (_, message) => telemetryError = message;

            using (BroadcastTelemetry(adapter))
            {
                service.StartTelemetryMonitor(csvFilePath: null);
                Assert.True(samples.Wait(2000), telemetryError ?? "no telemetry");

                Assert.Contains(new BrokerLeaseInfo(LeaseKind.Shared, J2534AbsService.TelemetryHolder, null), broker.State.Leases);
                Assert.Equal(3, adapter.OpenChannels(BrokerProtocol.Can).Single().FilterCount);

                // A KWP read of the module runs while telemetry does (it used to be refused).
                Assert.True(service.ReadDtcs().success);

                // The whole-adapter tools are refused, naming the monitor.
                const string busy = "The adapter is in use: ABS telemetry is running.";
                Assert.Equal(busy, service.ProbeConnection().errorMessage);
                Assert.Equal(busy, service.SniffBus(1, null).errorMessage);

                service.StopTelemetryMonitor();
            }

            Assert.Null(telemetryError);
            Assert.Empty(broker.State.Leases);
            Assert.Equal(0, adapter.DevicesOpen);
        }

        [Fact]
        public void Actuation_IsRefusedWhileAnotherLeaseTalksToTheAbs()
        {
            var (broker, adapter, service) = Create();
            using var other = broker.Acquire(LeaseKind.Shared, "the ABS fault-code read");
            using var otherLink = other.OpenDiagnostic(Abs);

            using (BroadcastTelemetry(adapter))
            {
                var (success, error, _) = service.RunBleedSequence(null, CancellationToken.None);

                Assert.False(success);
                Assert.Equal("The ABS/ESP is in use: the ABS fault-code read is running.", error);
            }

            // Nothing was sent to the module: no session request, no routine.
            Assert.All(adapter.Channels.Where(c => c.Protocol == BrokerProtocol.Iso15765),
                channel => Assert.Empty(channel.Sent));
        }

        [Fact]
        public void ReadsAreRefusedWhileActuationHoldsTheAbs()
        {
            var (broker, _, service) = Create();
            using var bleed = broker.AcquireEcuExclusive(Abs, J2534AbsService.BleedHolder);

            var (success, error, _) = service.ReadDtcs();

            Assert.False(success);
            Assert.Equal("The ABS/ESP is in use: the ABS bleed is running.", error);
        }

        [Fact]
        public void ProbeAndSniff_AreRefusedWhileAnythingElseHoldsTheAdapter()
        {
            var (broker, adapter, service) = Create();
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");

            const string busy = "The adapter is in use: the CCP session is running.";
            Assert.Equal((false, busy), (service.ProbeConnection().success, service.ProbeConnection().errorMessage));
            Assert.Equal((false, busy), (service.SniffBus(1, null).success, service.SniffBus(1, null).errorMessage));
            Assert.Equal(0, adapter.DevicesOpened);
        }

        [Fact]
        public async Task Sniff_HoldsTheAdapterOnAChannelThatAlsoReceives29BitFrames()
        {
            var (broker, adapter, service) = Create();
            BrokerState? during = null;
            broker.StateChanged += (_, state) => { if (state.DeviceOpen) during ??= state; };

            // The baseline phase alone listens for 5 s; one second of capture keeps the test short enough.
            var sniff = Task.Run(() => service.SniffBus(1, null));
            var deadline = Environment.TickCount64 + 2000;
            while (adapter.Channels.Count == 0 && Environment.TickCount64 < deadline) Thread.Sleep(10);
            var channel = Assert.Single(adapter.Channels);
            Assert.Equal(BrokerProtocol.CanWithExtendedIds, channel.Protocol);
            Assert.Throws<BrokerBusyException>(() => broker.Links("the trouble-code read").OpenDiagnostic(ECUDefinition.ECM));

            var (success, error, _) = await sniff;
            Assert.True(success, error);
            Assert.Equal(J2534AbsService.SniffHolder, during?.Blocking?.Holder);
            Assert.True(channel.Closed);
            Assert.Empty(broker.State.Leases);
        }
    }
}
