using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Tests.Fakes;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The one-shot services on the broker as the app composes them (J2534-session-broker-plan.md, Phase 3): each
    /// service's <c>(J2534Broker)</c> constructor is what its parameterless one runs against
    /// <see cref="J2534Broker.Shared"/>. The protocol of each service is pinned by <see cref="DiagnosticServiceTests"/>;
    /// these pin the leases: reads share, and operations that change an ECU hold it (plan §3.4).
    /// </summary>
    public sealed class BrokerServiceTests
    {
        private const string Vin = "SCCLMDTU9BHA10001";

        private static (J2534Broker Broker, FakeJ2534Backend Adapter) Create()
        {
            var adapter = new FakeJ2534Backend();
            return (new J2534Broker(adapter), adapter);
        }

        [Fact]
        public void DtcRead_ReleasesEverythingWhenDone()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, [0x03], [0x43, 0x00])
                .Reply(ECUDefinition.ECM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.ECM, [0x0A], [0x4A, 0x00])
                .Reply(ECUDefinition.TCM, [0x03], [0x43, 0x00])
                .Reply(ECUDefinition.TCM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.TCM, [0x0A], [0x4A, 0x00]);

            var (success, error, _) = new J2534DtcService(broker).ReadCodes();

            Assert.True(success, error);
            Assert.Empty(broker.State.Leases);
            Assert.Equal(0, adapter.DevicesOpen);
        }

        [Fact]
        public void DtcClear_HoldsOnlyTheModuleItClears()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.TCM, [0x04], [0x44]);
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");
            using var ccpLink = ccp.OpenCan(ECUDefinition.ECM);
            var service = new J2534DtcService(broker);

            var (engineCleared, engineError) = service.ClearCodes();
            Assert.False(engineCleared);
            Assert.Equal("The ECM is in use: the CCP session is running.", engineError);

            // The transmission is a different module, so its clear goes ahead.
            Assert.Equal((true, ""), service.ClearTcuCodes());
        }

        [Fact]
        public void Reads_AreRefusedWhileAWriteHoldsTheEcu()
        {
            var (broker, _) = Create();
            using var write = broker.AcquireEcuExclusive(ECUDefinition.ECM, "the VIN write");

            var (success, error, _) = new J2534DtcService(broker).ReadCodes();

            Assert.False(success);
            Assert.Equal("The ECM is in use: the VIN write is running.", error);
        }

        [Fact]
        public void EngineWrites_AreRefusedWhileAnotherLeaseTalksToTheEcm()
        {
            var (broker, adapter) = Create();
            using var reader = broker.Acquire(LeaseKind.Shared, "the trouble-code read");
            using var link = reader.OpenDiagnostic(ECUDefinition.ECM);

            const string expected = "The ECM is in use: the trouble-code read is running.";
            Assert.Equal((false, expected), new J2534VinSetService(broker).SetVin(Vin));
            Assert.Equal((false, expected), new J2534ObdResetService(broker).PerformLearningReset());
            Assert.Equal((false, expected), new J2534DynoModeService(broker).EnableDynoMode());
            Assert.Equal((false, "Failed to write coding: " + expected),
                new J2534EcuCodingService(broker).WriteCoding(new T6eCodingDecoder(0x1122334455667788UL)));

            // Nothing reached the bus, and the refused operations left no lease behind.
            Assert.Empty(adapter.OpenChannels(BrokerProtocol.Can));
            Assert.Single(broker.State.Leases);
        }

        [Fact]
        public void CodingWrite_HoldsTheEcmAndSendsEveryFrame()
        {
            var (broker, adapter) = Create();
            var coding = new T6eCodingDecoder(0x1122334455667788UL);
            var states = new List<BrokerState>();
            broker.StateChanged += (_, state) => { lock (states) states.Add(state); };

            Assert.Equal((true, ""), new J2534EcuCodingService(broker).WriteCoding(coding));

            var can = adapter.Channels.Single(c => c.Protocol == BrokerProtocol.Can);
            Assert.Equal(6, can.Sent.Count);
            lock (states)
            {
                var held = states.First(s => s.Leases.Count > 0).Leases.Single();
                Assert.Equal(new BrokerLeaseInfo(LeaseKind.EcuExclusive, "the ECU coding write", "ECM"), held);
            }
            Assert.Empty(broker.State.Leases);
        }

        [Fact]
        public void NoAdapter_IsReportedByTheService()
        {
            var (broker, adapter) = Create();
            adapter.OpenError = new IOException("The J2534 adapter (OpenPort) is not connected, or is already in use.");

            var (success, error, _) = new J2534DtcService(broker).ReadCodes();

            Assert.False(success);
            Assert.Contains("not connected", error);
            Assert.Empty(broker.State.Leases);
        }
    }
}
