using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Tests.Fakes;
using SAE.J2534;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The OBD logger on a DeviceExclusive broker lease (J2534-session-broker-plan.md, Phase 4), against
    /// <see cref="FakeJ2534Backend"/>. Its polling loop is unchanged; these pin how it takes and gives back the adapter.
    /// </summary>
    public sealed class LoggingServiceBrokerTests : IDisposable
    {
        private static readonly byte[] Rpm = [0x01, 0x0C];
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "LotusECMLogger.Tests", Guid.NewGuid().ToString("N"));

        public LoggingServiceBrokerTests() => Directory.CreateDirectory(_directory);

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch (IOException) { /* a CSV still open on a failed test; the temp folder is cleaned eventually */ }
        }

        private static MultiECUConfiguration Config(int requests = 1) => new()
        {
            ECUGroups =
            [
                new ECURequestGroup
                {
                    ECU = ECUDefinition.ECM,
                    Requests = [.. Enumerable.Range(0, requests).Select(i => (IOBDRequest)new Mode01Request($"rpm {i}", 0x0C))],
                },
            ],
        };

        private (J2534LoggingService Logger, ManualResetEventSlim Data, List<Exception> Errors) CreateLogger(
            J2534Broker broker, MultiECUConfiguration? config = null)
        {
            var data = new ManualResetEventSlim(false);
            var errors = new List<Exception>();
            var logger = new J2534LoggingService(
                Path.Combine(_directory, $"{Guid.NewGuid():N}.csv"),
                readings => { if (readings.Count > 0) data.Set(); },
                ex => { lock (errors) errors.Add(ex); },
                config ?? Config(),
                broker);
            return (logger, data, errors);
        }

        [Fact]
        public void Logging_HoldsTheAdapterAndRefusesEveryOtherLease()
        {
            var adapter = new FakeJ2534Backend();
            adapter.Reply(ECUDefinition.ECM, Rpm, [0x41, 0x0C, 0x1A, 0xF8]);
            using var broker = new J2534Broker(adapter);
            var (logger, data, errors) = CreateLogger(broker);

            logger.Start();
            Assert.True(data.Wait(2000), "no readings arrived");

            Assert.Equal(new BrokerLeaseInfo(LeaseKind.DeviceExclusive, "OBD-II logging", null), broker.State.Blocking);
            var refused = Assert.Throws<BrokerBusyException>(() => broker.Links("the trouble-code read").OpenDiagnostic(ECUDefinition.ECM));
            Assert.Equal("The adapter is in use: OBD-II logging is running.", refused.Message);
            Assert.Throws<BrokerBusyException>(() => broker.Acquire(LeaseKind.Shared, "the CCP session"));
            Assert.Throws<BrokerBusyException>(() => broker.AcquireEcuExclusive(ECUDefinition.ABS, "ABS bleed"));

            // Only the logger's own channel is open: no shared channel, so no broker reader polls the device.
            var channel = Assert.Single(adapter.Channels);
            Assert.Equal(BrokerProtocol.Iso15765, channel.Protocol);
            Assert.Equal(1, channel.FilterCount);

            logger.Dispose();
            Assert.Empty(errors);
            Assert.True(channel.Closed);
            Assert.Equal(0, adapter.DevicesOpen);
            Assert.Empty(broker.State.Leases);

            // The broker's shared channels come back for the next user.
            using var link = broker.Links("the trouble-code read").OpenDiagnostic(ECUDefinition.ECM);
            Assert.True(link.Exchange(Rpm, echoLength: 1).IsPositive);
        }

        [Fact]
        public void Start_IsRefusedWhileAnotherLeaseIsHeldAndNamesTheHolder()
        {
            var adapter = new FakeJ2534Backend();
            using var broker = new J2534Broker(adapter);
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");
            var (logger, _, _) = CreateLogger(broker);

            var ex = Assert.Throws<BrokerBusyException>(logger.Start);
            Assert.Equal("The adapter is in use: the CCP session is running.", ex.Message);
            Assert.Equal(0, adapter.DevicesOpened);
            Assert.Single(broker.State.Leases);
            logger.Dispose();
        }

        [Fact]
        public void Start_GivesTheAdapterBackWhenItCannotOpen()
        {
            var adapter = new FakeJ2534Backend { OpenError = new IOException("The J2534 adapter is not connected.") };
            using var broker = new J2534Broker(adapter);
            var (logger, _, _) = CreateLogger(broker);

            var ex = Assert.Throws<IOException>(logger.Start);
            Assert.Contains("not connected", ex.Message);
            Assert.Empty(broker.State.Leases);
            logger.Dispose();
        }

        [Fact]
        public void Start_GivesTheAdapterBackWhenTheFiltersDoNotFit()
        {
            var adapter = new FakeJ2534Backend { FilterLimit = 0 };
            using var broker = new J2534Broker(adapter);
            var (logger, _, _) = CreateLogger(broker);

            Assert.Throws<FilterLimitException>(logger.Start);
            Assert.Empty(broker.State.Leases);
            Assert.Equal(0, adapter.DevicesOpen);
            logger.Dispose();
        }

        [Fact]
        public void Loop_StillSendsInBatchesOfFive()
        {
            var adapter = new FakeJ2534Backend();
            adapter.Reply(ECUDefinition.ECM, Rpm, [0x41, 0x0C, 0x1A, 0xF8]);
            using var broker = new J2534Broker(adapter);
            var (logger, data, _) = CreateLogger(broker, Config(requests: 7));

            logger.Start();
            Assert.True(data.Wait(2000));
            logger.Dispose();

            var batches = adapter.Channels.Single().Batches.ToList();
            Assert.True(batches.Count >= 2);
            // Seven requests per pass: a chunk of five, then two, every pass.
            for (int i = 0; i + 1 < batches.Count; i += 2)
                Assert.Equal([5, 2], batches.Skip(i).Take(2));
        }

        [Fact]
        public void UnpluggedAdapter_StopsLoggingWithAnErrorAndReleasesTheAdapter()
        {
            var adapter = new FakeJ2534Backend();
            adapter.Reply(ECUDefinition.ECM, Rpm, [0x41, 0x0C, 0x1A, 0xF8]);
            using var broker = new J2534Broker(adapter);
            var (logger, data, errors) = CreateLogger(broker);

            logger.Start();
            Assert.True(data.Wait(2000));
            adapter.Channels.Single().ReadError = new J2534Exception(ResultCode.DEVICE_NOT_CONNECTED, "unplugged");

            // The loop ends on its own: the lease goes back without anyone calling Stop.
            var deadline = Environment.TickCount64 + 2000;
            while (broker.State.Leases.Count > 0 && Environment.TickCount64 < deadline) Thread.Sleep(10);
            Assert.Empty(broker.State.Leases);
            lock (errors) Assert.Contains(errors, e => e.Message.Contains("unplugged"));
            logger.Dispose();
        }
    }
}
