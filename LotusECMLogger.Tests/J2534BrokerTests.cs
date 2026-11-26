using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Links;
using LotusECMLogger.Tests.Fakes;
using SAE.J2534;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The broker core (J2534-session-broker-plan.md, Phase 2) against <see cref="FakeJ2534Backend"/>, which behaves
    /// as the OpenPort 2.0 measured on a car (DLL lock held through blocking reads, 10 filters per channel, ISO 15765
    /// traffic visible on CAN).
    /// </summary>
    public sealed class J2534BrokerTests
    {
        private static readonly byte[] StoredDtcs = [0x03];

        private static (J2534Broker Broker, FakeJ2534Backend Adapter) Create(BrokerOptions? options = null)
        {
            var adapter = new FakeJ2534Backend();
            return (new J2534Broker(adapter, options), adapter);
        }

        // ── Exchanges ───────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Exchange_ReturnsTheReplyAndTheLastLinkClosesTheDevice()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, [0x43, 0x01, 0x03, 0x01]);

            using (var link = broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM))
            {
                var reply = link.Exchange(StoredDtcs);
                Assert.Equal([0x00, 0x00, 0x07, 0xE8, 0x43, 0x01, 0x03, 0x01], reply.Message);
                Assert.Equal(1, adapter.DevicesOpen);
                Assert.Single(broker.State.Leases);
            }

            Assert.Equal(0, adapter.DevicesOpen);
            Assert.Equal(1, adapter.DevicesOpened);
            Assert.Empty(broker.State.Leases);
            Assert.False(broker.State.DeviceOpen);
        }

        [Fact]
        public void Exchange_ReportsNegativeRepliesAndSilence()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, [0x07], [0x7F, 0x07, 0x11]);

            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var link = lease.OpenDiagnostic(ECUDefinition.ECM);

            Assert.Equal((byte)0x11, link.Exchange([0x07]).Nrc);
            Assert.True(link.Exchange([0x0A], timeoutMs: 50).IsTimeout);
        }

        [Fact]
        public void Exchange_ResponsePendingExtendsTheWait()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, (0, [0x7F, 0x03, 0x78]), (150, [0x43, 0x00]));

            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var link = lease.OpenDiagnostic(ECUDefinition.ECM);

            // The final answer comes after the 100 ms timeout; only the 0x78 extension lets it through.
            Assert.True(link.Exchange(StoredDtcs, timeoutMs: 100).IsPositive);
        }

        [Fact]
        public async Task Exchange_ConcurrentExchangesToTwoEcusEachGetTheirOwnReply()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, (60, [0x43, 0x01, 0x03, 0x01]));
            adapter.Reply(ECUDefinition.TCM, StoredDtcs, (0, [0x43, 0x01, 0x07, 0x00]));

            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var ecm = lease.OpenDiagnostic(ECUDefinition.ECM);
            using var tcm = lease.OpenDiagnostic(ECUDefinition.TCM);

            var ecmReply = Task.Run(() => ecm.Exchange(StoredDtcs));
            var tcmReply = Task.Run(() => tcm.Exchange(StoredDtcs));

            Assert.Equal(0x07, (await tcmReply).Message![6]);
            Assert.Equal(0x03, (await ecmReply).Message![6]);
            // One flow-control filter per ECU, shared by its links.
            Assert.Equal(2, adapter.OpenChannels(BrokerProtocol.Iso15765).Single().FilterCount);
        }

        [Fact]
        public async Task Exchange_TwoOperationsOnOneEcuCannotTakeEachOthersReplies()
        {
            var (broker, adapter) = Create();
            // Mode 09 replies echo their PID; both are slow, so without the gate both requests would be in flight.
            adapter.Reply(ECUDefinition.ECM, [0x09, 0x02], (40, [0x49, 0x02, 0x01, 0x56]));
            adapter.Reply(ECUDefinition.ECM, [0x09, 0x04], (40, [0x49, 0x04, 0x01, 0x43]));

            var vin = broker.Links("vehicle info");
            var calId = broker.Links("emissions");
            var first = Task.Run(() => { using var l = vin.OpenDiagnostic(ECUDefinition.ECM); return l.Exchange([0x09, 0x02], echoLength: 1); });
            var second = Task.Run(() => { using var l = calId.OpenDiagnostic(ECUDefinition.ECM); return l.Exchange([0x09, 0x04], echoLength: 1); });

            Assert.Equal(0x02, (await first).Message![5]);
            Assert.Equal(0x04, (await second).Message![5]);
            Assert.Equal(1, adapter.DevicesOpened); // the two leases overlapped on one device
        }

        [Fact]
        public void IsoReader_NeverBlocksInTheDllWhileNoReplyIsAwaited()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, (20, [0x43, 0x00]));

            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var link = lease.OpenDiagnostic(ECUDefinition.ECM);
            var channel = adapter.OpenChannels(BrokerProtocol.Iso15765).Single();

            Thread.Sleep(60);
            Assert.NotEmpty(channel.Reads);
            Assert.All(channel.Reads, read => Assert.Equal(0, read.TimeoutMs));

            Assert.True(link.Exchange(StoredDtcs).IsPositive);
            Assert.Contains(channel.Reads, read => read.TimeoutMs > 0 && read.Max == 1);
        }

        [Fact]
        public void IsoSends_ArePacedWhileTheCanChannelIsOpen()
        {
            // A long interval, so "not paced" and "paced" are far apart even on a loaded test machine.
            var (broker, adapter) = Create(new BrokerOptions { IsoSendIntervalWithCanMs = 100 });
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, [0x43, 0x00]);

            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var link = lease.OpenDiagnostic(ECUDefinition.ECM);
            for (int i = 0; i < 3; i++) link.Exchange(StoredDtcs);
            var iso = adapter.OpenChannels(BrokerProtocol.Iso15765).Single();
            var unpaced = iso.Sent.Select(s => s.AtMs).ToList();
            Assert.True(unpaced[^1] - unpaced[0] < 100, "no pacing without a CAN channel");

            using var can = lease.OpenCan(null);
            for (int i = 0; i < 3; i++) link.Exchange(StoredDtcs);
            var paced = iso.Sent.Select(s => s.AtMs).Skip(3).ToList();
            for (int i = 1; i < paced.Count; i++)
                Assert.True(paced[i] - paced[i - 1] >= 95, $"gap {paced[i] - paced[i - 1]:0.0} ms");
        }

        [Fact]
        public void Exchange_IsNotSlowedByTheReaderHoldingTheDllLock()
        {
            // Regression: the reader used to block in the DLL as soon as an exchange was registered, before its
            // request was sent, and kept winning the lock the send needed: 50 ms median against an instant reply.
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, [0x43, 0x00]);
            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var link = lease.OpenDiagnostic(ECUDefinition.ECM);

            var times = new List<double>();
            for (int i = 0; i < 50; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Assert.True(link.Exchange(StoredDtcs).IsPositive);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            double median = times.Order().ElementAt(times.Count / 2);
            Assert.True(median < 5, $"median {median:0.0} ms");
        }

        [Fact]
        public async Task CanSends_AreNotStarvedWhileAnIsoReplyIsAwaited()
        {
            // While a slow reply is awaited the ISO 15765 reader blocks in the DLL slice after slice; a CAN send
            // (a CCP command) must get the lock after at most one slice instead of losing it to every new slice.
            var (broker, adapter) = Create(new BrokerOptions { IsoReplySliceMs = 10 });
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, (400, [0x43, 0x00]));
            using var lease = broker.Acquire(LeaseKind.Shared, "test");
            using var iso = lease.OpenDiagnostic(ECUDefinition.ECM);
            using var can = lease.OpenCan(null);

            var waiting = Task.Run(() => iso.Exchange(StoredDtcs));
            Thread.Sleep(30);
            var times = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                can.Send(0x350, [0x01]);
                times.Add(clock.Elapsed.TotalMilliseconds);
                Thread.Sleep(2);
            }

            Assert.True((await waiting).IsPositive);
            Assert.True(times.Max() < 30, $"slowest CAN send {times.Max():0.0} ms");
        }

        [Fact]
        public void IsoFilters_AreSharedPerEcuAndTheLimitIsReportedClearly()
        {
            var (broker, adapter) = Create();
            using var lease = broker.Acquire(LeaseKind.Shared, "test");

            var first = lease.OpenDiagnostic(ECUDefinition.ECM);
            var second = lease.OpenDiagnostic(ECUDefinition.ECM);
            var iso = adapter.OpenChannels(BrokerProtocol.Iso15765).Single();
            Assert.Equal(1, iso.FilterCount);
            first.Dispose();
            Assert.Equal(1, iso.FilterCount);
            second.Dispose();
            Assert.Equal(0, iso.FilterCount);

            var links = Enumerable.Range(0, 10)
                .Select(i => lease.OpenDiagnostic(new ECUDefinition { Name = $"M{i}", RequestId = (uint)(0x600 + i), ResponseId = (uint)(0x680 + i) }))
                .ToList();
            var ex = Assert.Throws<IOException>(() => lease.OpenDiagnostic(ECUDefinition.ECM));
            Assert.Contains("ECM", ex.Message);
            Assert.Contains("10 ISO 15765 filters", ex.Message);
        }

        [Fact]
        public async Task AdapterFailure_FailsTheWaitingExchangeAndTheDeviceReopensAfterRelease()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, (5000, [0x43, 0x00]));

            var lease = broker.Acquire(LeaseKind.Shared, "test");
            var link = lease.OpenDiagnostic(ECUDefinition.ECM);
            var iso = adapter.OpenChannels(BrokerProtocol.Iso15765).Single();
            var waiting = Task.Run(() => link.Exchange(StoredDtcs));
            Thread.Sleep(30);
            iso.ReadError = new J2534Exception(ResultCode.DEVICE_NOT_CONNECTED, "unplugged");

            var failure = await Assert.ThrowsAsync<IOException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("unplugged", failure.Message);
            Assert.Throws<IOException>(() => link.Exchange(StoredDtcs));
            lease.Dispose();

            adapter.Reply(ECUDefinition.ECM, StoredDtcs, [0x43, 0x00]);
            using var again = broker.Links("retry").OpenDiagnostic(ECUDefinition.ECM);
            Assert.True(again.Exchange(StoredDtcs).IsPositive);
            Assert.Equal(2, adapter.DevicesOpened);
        }

        [Fact]
        public void NoAdapter_IsReportedWhenTheFirstLinkOpensAndLeavesNoLease()
        {
            var (broker, adapter) = Create();
            adapter.OpenError = new IOException("The J2534 adapter is not connected, or another program is using it.");

            var ex = Assert.Throws<IOException>(() => broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM));
            Assert.Contains("not connected", ex.Message);
            Assert.Empty(broker.State.Leases);
        }

        // ── The Phase 0 services run on the broker unchanged ────────────────────────────────────────────────────

        [Fact]
        public void DtcService_ReadsEngineAndTransmissionThroughTheBroker()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, [0x03], [0x43, 0x01, 0x03, 0x01])
                .Reply(ECUDefinition.ECM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.ECM, [0x0A], [0x4A, 0x00])
                .Reply(ECUDefinition.TCM, [0x03], [0x43, 0x01, 0x07, 0x00])
                .Reply(ECUDefinition.TCM, [0x07], [0x47, 0x00])
                .Reply(ECUDefinition.TCM, [0x0A], [0x4A, 0x00]);

            var (success, error, result) = new J2534DtcService(broker.Links("DTC read")).ReadCodes();

            Assert.True(success, error);
            Assert.Equal(["P0301"], result.Stored.Select(c => c.Code));
            Assert.Equal(["P0700"], result.Tcu!.Stored.Select(c => c.Code));
            Assert.Equal(0, adapter.DevicesOpen);
        }

        // ── Raw CAN ─────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Can_DeliversOnlySubscribedIdsAndSkipsOwnTransmissions()
        {
            var (broker, adapter) = Create();
            using var lease = broker.Acquire(LeaseKind.Shared, "CCP");
            using var link = lease.OpenCan(ECUDefinition.ECM);
            using var dto = link.Subscribe(0x351);
            using var second = link.Subscribe(0x351);
            Assert.Equal(1, adapter.OpenChannels(BrokerProtocol.Can).Single().FilterCount);

            adapter.InjectCan(0x351, [0xFF, 0x00, 0x01]);
            adapter.InjectCan(0x351, [0xFF, 0x00, 0x02], RxFlag.TX_MSG_TYPE);
            adapter.InjectCan(0x352, [0xAA]);

            using var timeout = new CancellationTokenSource(1000);
            var frame = await dto.Reader.ReadAsync(timeout.Token);
            Assert.Equal(0x351u, frame.CanId);
            Assert.Equal([0xFF, 0x00, 0x01], frame.Data.ToArray());
            Assert.Equal(0x01, (await second.Reader.ReadAsync(timeout.Token)).Data.Span[2]);
            await Task.Delay(30);
            Assert.False(dto.Reader.TryRead(out _));

            link.Send(0x350, [0x01, 0x00]);
            Assert.Equal([0x00, 0x00, 0x03, 0x50, 0x01, 0x00], adapter.OpenChannels(BrokerProtocol.Can).Single().Sent.Single().Message);
        }

        [Fact]
        public void Can_FiltersAreReleasedWithTheirLastSubscriberAndTheLimitIsReported()
        {
            var (broker, adapter) = Create();
            using var lease = broker.Acquire(LeaseKind.Shared, "ABS telemetry");
            var link = lease.OpenCan(null);
            var can = adapter.OpenChannels(BrokerProtocol.Can).Single();

            var streams = Enumerable.Range(0, 10).Select(i => link.Subscribe((uint)(0x100 + i))).ToList();
            var ex = Assert.Throws<IOException>(() => link.Subscribe(0x351));
            Assert.Contains("0x351", ex.Message);

            streams[0].Dispose();
            Assert.Equal(9, can.FilterCount);
            link.Dispose(); // ends the link's remaining subscriptions
            Assert.Equal(0, can.FilterCount);
        }

        [Fact]
        public void Can_PollsFastOnlyWhileAsked()
        {
            var (broker, adapter) = Create(new BrokerOptions { CanIdlePollMs = 20, CanFastPollMs = 1 });
            using var lease = broker.Acquire(LeaseKind.Shared, "CCP");
            using var link = lease.OpenCan(null);
            var can = adapter.OpenChannels(BrokerProtocol.Can).Single();

            Thread.Sleep(100);
            int idleReads = can.Reads.Count;
            using (link.PollFast()) Thread.Sleep(100);
            int fastReads = can.Reads.Count - idleReads;

            Assert.True(idleReads <= 8, $"idle reads {idleReads}");
            Assert.True(fastReads >= 3 * idleReads, $"fast {fastReads} vs idle {idleReads}");
            Assert.All(can.Reads, read => Assert.Equal(0, read.TimeoutMs));
        }

        // ── Leases ──────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void DeviceExclusive_IsRefusedWhileAnythingIsHeldAndNamesTheHolder()
        {
            var (broker, _) = Create();
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");

            var ex = Assert.Throws<BrokerBusyException>(() => broker.Acquire(LeaseKind.DeviceExclusive, "OBD-II logging"));
            Assert.Equal("the CCP session", ex.Holder);
            Assert.Contains("the CCP session is running", ex.Message);
        }

        [Fact]
        public void DeviceExclusive_RefusesEveryOtherLeaseAndGetsAChannelNoOneElseReads()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, StoredDtcs, [0x43, 0x00]);
            var logger = broker.Acquire(LeaseKind.DeviceExclusive, "OBD-II logging");

            var refused = Assert.Throws<BrokerBusyException>(() => broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM));
            Assert.Contains("OBD-II logging is running", refused.Message);
            Assert.Throws<BrokerBusyException>(() => broker.AcquireEcuExclusive(ECUDefinition.ABS, "ABS bleed"));
            Assert.Throws<InvalidOperationException>(() => logger.OpenDiagnostic(ECUDefinition.ECM));
            Assert.Equal("OBD-II logging", broker.State.Blocking?.Holder);

            var channel = logger.OpenExclusiveChannel(BrokerProtocol.Iso15765);
            var fake = adapter.OpenChannels(BrokerProtocol.Iso15765).Single();
            Thread.Sleep(40);
            Assert.Empty(fake.Reads); // the broker runs no reader on it

            logger.Dispose();
            Assert.True(fake.Closed);
            Assert.Equal(0, adapter.DevicesOpen);

            using var link = broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM);
            Assert.True(link.Exchange(StoredDtcs).IsPositive);
        }

        [Fact]
        public void DeviceReleased_KeepsTheDeviceClosedUntilReleased()
        {
            var (broker, adapter) = Create();
            var flasher = broker.Acquire(LeaseKind.DeviceReleased, "the T6 flasher");

            Assert.Equal(0, adapter.DevicesOpen);
            var ex = Assert.Throws<BrokerBusyException>(() => broker.Acquire(LeaseKind.Shared, "CCP"));
            Assert.Contains("closed while the T6 flasher runs", ex.Message);
            Assert.Throws<InvalidOperationException>(() => flasher.OpenDiagnostic(ECUDefinition.ECM));

            flasher.Dispose();
            using var lease = broker.Acquire(LeaseKind.Shared, "CCP");
        }

        [Fact]
        public void EcuExclusive_RefusesOtherLinksToItsEcuOnly()
        {
            var (broker, _) = Create();
            using var bleed = broker.AcquireEcuExclusive(ECUDefinition.ABS, "ABS bleed");
            using var abs = bleed.OpenDiagnostic(ECUDefinition.ABS);
            using var other = broker.Acquire(LeaseKind.Shared, "DTC read");

            var ex = Assert.Throws<BrokerBusyException>(() => other.OpenDiagnostic(ECUDefinition.ABS));
            Assert.Contains("ABS/ESP is in use: ABS bleed is running", ex.Message);
            using var ecm = other.OpenDiagnostic(ECUDefinition.ECM);
            Assert.Throws<BrokerBusyException>(() => broker.AcquireEcuExclusive(ECUDefinition.ABS, "ABS actuation"));
        }

        [Fact]
        public void EcuExclusive_IsRefusedWhileAnotherLeaseTalksToThatEcuOverCanOrIso()
        {
            var (broker, _) = Create();
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");
            var can = ccp.OpenCan(ECUDefinition.ECM);

            var ex = Assert.Throws<BrokerBusyException>(() => broker.AcquireEcuExclusive(ECUDefinition.ECM, "DTC clear"));
            Assert.Contains("ECM is in use: the CCP session is running", ex.Message);

            can.Dispose();
            using var clear = broker.AcquireEcuExclusive(ECUDefinition.ECM, "DTC clear");
            Assert.Throws<BrokerBusyException>(() => ccp.OpenCan(ECUDefinition.ECM));
            using var anonymous = ccp.OpenCan(null); // CAN use that names no ECU is not held back
        }

        [Fact]
        public void EcuExclusiveLinks_HoldTheEcuOnlyWhileTheLinkIsOpen()
        {
            var (broker, adapter) = Create();
            adapter.Reply(ECUDefinition.ECM, [0x04], [0x44]);
            var clearing = broker.EcuExclusiveLinks(ECUDefinition.ECM, "DTC clear").OpenDiagnostic(ECUDefinition.ECM);

            Assert.Throws<BrokerBusyException>(() => broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM));
            Assert.True(clearing.Exchange([0x04]).IsPositive);
            clearing.Dispose();

            using var reading = broker.Links("DTC read").OpenDiagnostic(ECUDefinition.ECM);
        }

        [Fact]
        public void StateChanged_ReportsLeasesAndTheDevice()
        {
            var (broker, _) = Create();
            var states = new List<BrokerState>();
            broker.StateChanged += (_, state) => { lock (states) states.Add(state); };

            using (var lease = broker.Acquire(LeaseKind.Shared, "CCP"))
            using (lease.OpenCan(null)) { }

            lock (states)
            {
                Assert.Equal(["CCP"], states[0].Leases.Select(l => l.Holder));
                Assert.Contains(states, s => s.DeviceOpen);
                Assert.Empty(states[^1].Leases);
                Assert.False(states[^1].DeviceOpen);
            }
        }

        [Fact]
        public void Dispose_ReleasesEverythingAndRefusesNewLeases()
        {
            var (broker, adapter) = Create();
            var lease = broker.Acquire(LeaseKind.Shared, "CCP");
            using var link = lease.OpenCan(null);

            broker.Dispose();

            Assert.Equal(0, adapter.DevicesOpen);
            Assert.Throws<ObjectDisposedException>(() => broker.Acquire(LeaseKind.Shared, "late"));
            Assert.Throws<ObjectDisposedException>(() => link.Send(0x350, [0x01]));
        }
    }
}
