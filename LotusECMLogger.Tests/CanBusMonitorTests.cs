using LotusECMLogger.Controls;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Broker;
using LotusECMLogger.Tests.Fakes;

namespace LotusECMLogger.Tests
{
    /// <summary>The CAN Monitor tab: its ID filter, the capture against <see cref="FakeJ2534Backend"/>, and the control.</summary>
    public sealed class CanBusMonitorTests
    {
        [Theory]
        [InlineData("", 0x7E8, true)]
        [InlineData("7E8", 0x7E8, true)]
        [InlineData("7E8", 0x7E0, false)]
        [InlineData("0x7e8", 0x7E8, true)]
        [InlineData("7E0-7EF", 0x7E9, true)]
        [InlineData("7E0-7EF", 0x7F0, false)]
        [InlineData("700-7FF, !7DF", 0x7DF, false)]
        [InlineData("!0A2 !0A4", 0x0A4, false)]
        [InlineData("!0A2 !0A4", 0x0A8, true)]
        [InlineData("A2;A4", 0xA4, true)]
        [InlineData("18DAF110", 0x18DAF110, true)]
        public void Filter_MatchesIdsAndRanges(string text, uint id, bool expected)
        {
            Assert.True(CanIdFilter.TryParse(text, out var filter, out var error), error);
            Assert.Equal(expected, filter.Matches(id));
        }

        [Theory]
        [InlineData("7G0")]
        [InlineData("7EF-7E0")]
        [InlineData("1-2-3")]
        [InlineData("!")]
        [InlineData("20000000")]
        public void Filter_RejectsBadTerms(string text)
        {
            Assert.False(CanIdFilter.TryParse(text, out var filter, out var error));
            Assert.Contains(text, error);
            Assert.True(filter.IsEmpty);
        }

        [Fact]
        public void Monitor_CapturesEveryFrameAndHoldsTheAdapter()
        {
            var adapter = new FakeJ2534Backend();
            var broker = new J2534Broker(adapter);
            using var monitor = new CanBusMonitor(broker);

            monitor.Start();
            var channel = Assert.Single(adapter.Channels);
            Assert.Equal(BrokerProtocol.CanWithExtendedIds, channel.Protocol);
            Assert.Equal(CanBusMonitor.Holder, broker.State.Blocking?.Holder);
            Assert.Throws<BrokerBusyException>(() => broker.Links("the trouble-code read").OpenDiagnostic(ECUDefinition.ECM));

            adapter.InjectCan(0x0A2, [1, 2, 3]);
            adapter.InjectCan(0x7E8, [0x02, 0x41, 0x0C]);
            var frames = WaitForFrames(monitor, 2);

            Assert.Equal([0x0A2u, 0x7E8u], frames.Select(f => f.Id));
            Assert.Equal("02 41 0C", frames[1].DataText);
            Assert.Equal("7E8", frames[1].IdText);
            Assert.False(frames[1].Extended);
            Assert.True(frames[1].TimeMs >= frames[0].TimeMs);

            monitor.Stop();
            Assert.False(monitor.IsRunning);
            Assert.True(channel.Closed);
            Assert.Empty(broker.State.Leases);
        }

        [Fact]
        public void Monitor_IsRefusedWhileAnythingElseUsesTheAdapter()
        {
            var adapter = new FakeJ2534Backend();
            var broker = new J2534Broker(adapter);
            using var ccp = broker.Acquire(LeaseKind.Shared, "the CCP session");
            using var monitor = new CanBusMonitor(broker);

            var refused = Assert.Throws<BrokerBusyException>(monitor.Start);
            Assert.Equal("the CCP session", refused.Holder);
            Assert.False(monitor.IsRunning);
        }

        [Fact]
        public void Control_FiltersTheLogAndThePerIdTable()
        {
            var adapter = new FakeJ2534Backend();
            var broker = new J2534Broker(adapter);

            UiTestHost.RunInMessageLoop(async form =>
            {
                var control = new CanMonitorControl(new CanBusMonitor(broker)) { Dock = DockStyle.Fill };
                form.Controls.Add(control);

                control.StartButton.PerformClick();
                Assert.Equal("Stop", control.StartButton.Text);
                for (int i = 0; i < 3; i++)
                {
                    adapter.InjectCan(0x0A2, [(byte)i]);
                    adapter.InjectCan(0x7E8, [0x02, 0x41, (byte)i]);
                }

                long deadline = Environment.TickCount64 + 3000;
                while (control.FrameView.VirtualListSize < 2 && Environment.TickCount64 < deadline) await Task.Delay(20);
                await Task.Delay(300);

                // One row per ID: both IDs, the latest data.
                Assert.Equal(2, control.FrameView.VirtualListSize);
                control.FilterBox.Text = "7E0-7EF";
                Assert.Equal(1, control.FrameView.VirtualListSize);
                Assert.Equal("7E8", control.FrameView.Items[0].Text);
                Assert.Equal("02 41 02", control.FrameView.Items[0].SubItems[3].Text);

                // The raw log, filtered by exclusion.
                control.GroupByIdBox.Checked = false;
                Assert.Equal(3, control.FrameView.VirtualListSize);
                control.FilterBox.Text = "!7E8";
                Assert.Equal(3, control.FrameView.VirtualListSize);
                Assert.Equal("0A2", control.FrameView.Items[0].SubItems[1].Text);

                // A bad filter keeps the last good one and says why.
                control.FilterBox.Text = "!7E8, zz";
                Assert.Equal(3, control.FrameView.VirtualListSize);
                Assert.Contains("'zz'", control.StatusText);

                control.StartButton.PerformClick();
                Assert.Equal("Start", control.StartButton.Text);
                Assert.Empty(broker.State.Leases);
            });
        }

        private static List<CanBusFrame> WaitForFrames(CanBusMonitor monitor, int count)
        {
            var frames = new List<CanBusFrame>();
            long deadline = Environment.TickCount64 + 3000;
            while (frames.Count < count && Environment.TickCount64 < deadline)
            {
                frames.AddRange(monitor.Drain());
                Thread.Sleep(10);
            }
            return frames;
        }
    }
}
