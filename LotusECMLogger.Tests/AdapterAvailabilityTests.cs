using LotusECMLogger.Controls;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Tests.Fakes;
using static LotusECMLogger.Tests.UiTestHost;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// <see cref="AdapterAvailability"/>, which replaced the <c>IsLoggerActive</c> flags (J2534-session-broker-plan.md,
    /// Phase 7): it follows the broker on the UI thread, inside a real message loop.
    /// </summary>
    public sealed class AdapterAvailabilityTests
    {
        [Fact]
        public void Follows_WholeAdapterLeases_OnTheUiThread()
        {
            var broker = new J2534Broker(new FakeJ2534Backend());
            RunInMessageLoop(async form =>
            {
                int uiThread = Environment.CurrentManagedThreadId;
                var calls = new List<(int Thread, bool Blocked)>();
                AdapterAvailability? availability = null;
                availability = AdapterAvailability.Track(form, () => calls.Add((Environment.CurrentManagedThreadId, availability!.Blocked)), broker);
                Assert.False(availability.Blocked);

                // Taken on another thread, as the logger and the ABS tools do.
                var logging = await Task.Run(() => broker.Acquire(LeaseKind.DeviceExclusive, J2534LoggingService.LeaseHolder));
                await WaitUntil(() => calls.Count == 1);
                Assert.Equal((uiThread, true), calls[0]);
                Assert.Equal("The adapter is in use: OBD-II logging is running.", availability.Reason);

                await Task.Run(logging.Dispose);
                await WaitUntil(() => calls.Count == 2);
                Assert.Equal((uiThread, false), calls[1]);
                Assert.Null(availability.Reason);

                using (broker.Acquire(LeaseKind.DeviceReleased, T6EFlasher.LeaseHolder))
                {
                    await WaitUntil(() => calls.Count == 3);
                    Assert.Equal("The adapter is closed while the T6 flasher runs.", availability.Reason);
                }
            });
        }

        [Fact]
        public void SharedAndSingleEcuLeases_DoNotBlock()
        {
            var broker = new J2534Broker(new FakeJ2534Backend());
            RunInMessageLoop(async form =>
            {
                int calls = 0;
                var availability = AdapterAvailability.Track(form, () => calls++, broker);

                using (broker.Acquire(LeaseKind.Shared, BrokerCcpTransport.LeaseHolder))
                using (broker.AcquireEcuExclusive(ECUDefinition.ABS, J2534AbsService.BleedHolder))
                {
                    await Task.Delay(50);
                    Assert.False(availability.Blocked);
                }
                Assert.Equal(0, calls);
            });
        }

        [Fact]
        public void TheControlsOwnWholeAdapterOperations_DoNotBlockIt()
        {
            var broker = new J2534Broker(new FakeJ2534Backend());
            RunInMessageLoop(async form =>
            {
                var availability = AdapterAvailability.Track(form, () => { }, broker, J2534AbsService.SniffHolder);

                using (broker.Acquire(LeaseKind.DeviceExclusive, J2534AbsService.SniffHolder))
                {
                    await Task.Delay(50);
                    Assert.False(availability.Blocked);
                }
            });
        }

        [Fact]
        public void ADisposedControl_StopsFollowing()
        {
            var broker = new J2534Broker(new FakeJ2534Backend());
            RunInMessageLoop(async form =>
            {
                var child = new Panel();
                form.Controls.Add(child);
                int calls = 0;
                AdapterAvailability.Track(child, () => calls++, broker);
                child.Dispose();

                using (broker.Acquire(LeaseKind.DeviceExclusive, J2534LoggingService.LeaseHolder))
                    await Task.Delay(100);
                Assert.Equal(0, calls);
            });
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("The control was not updated.");
                await Task.Delay(10);
            }
        }
    }
}
