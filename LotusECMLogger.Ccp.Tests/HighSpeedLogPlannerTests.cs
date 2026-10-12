using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class HighSpeedLogPlannerTests
{
    private static HighSpeedChannel Channel(int i, byte size = 2) =>
        new() { Name = $"ch{i}", Address = 0x40001000u + (uint)i * 4, Size = size };

    [Theory]
    [InlineData(100, 0, 1)]
    [InlineData(50, 2, 1)]
    [InlineData(20, 4, 1)]
    [InlineData(10, 6, 1)]
    [InlineData(5, 6, 2)]
    [InlineData(2, 8, 1)]
    [InlineData(1, 9, 1)]
    public void T6EventChannelsAndPrescalersMatchTheFirmwareRateTable(int rateHz, int slot, int divider)
    {
        var group = Assert.Single(HighSpeedLogPlanner.Plan([(Channel(0), rateHz)], CcpTarget.T6).Groups);
        Assert.Equal((slot, (ushort)divider, rateHz), (group.Slot, group.Divider, group.RateHz));
    }

    [Theory]
    [InlineData(100, new[] { 0, 1, 0 })]
    [InlineData(5, new[] { 6, 7, 6 })]
    [InlineData(2, new[] { 8, 8, 8 })] // event 9 runs at 1 Hz, so 8 has no partner
    public void SpillGroupsAlternateAcrossSameRateEventPairs(int rateHz, int[] slots)
    {
        // 3 groups × 12 frames × 3 two-byte channels per frame.
        var selection = Enumerable.Range(0, 3 * 12 * 3).Select(i => (Channel(i), rateHz)).ToList();
        Assert.Equal(slots, HighSpeedLogPlanner.Plan(selection).Groups.Select(g => g.Slot));
    }

    [Fact]
    public void LabelsAreTheExpectedFirstPidPlusTheFrameIndex()
    {
        var selection = Enumerable.Range(0, 13 * 3).Select(i => (Channel(i), 100)).ToList();
        var plan = HighSpeedLogPlanner.Plan(selection);
        Assert.Equal(new byte[] { 0, 11 }, new[] { plan.Groups[0].Frames[0].Label, plan.Groups[0].Frames[11].Label });
        Assert.Equal((byte)12, plan.Groups[1].Frames[0].Label);
    }

    [Fact]
    public void CapacityComesFromTheTarget()
    {
        var selection = Enumerable.Range(0, 10 * 12 * 3 + 1).Select(i => (Channel(i), 100)).ToList();
        var error = Assert.Throws<InvalidOperationException>(() => HighSpeedLogPlanner.Plan(selection));
        Assert.Contains("more than 10 ECU groups", error.Message);
        Assert.Equal(10, HighSpeedLogPlanner.Plan(selection.Take(10 * 12 * 3).ToList()).Groups.Count);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(25)]
    [InlineData(0)]
    public void UnsupportedRatesAreRejected(int rateHz) =>
        Assert.Throws<ArgumentException>(() => HighSpeedLogPlanner.Plan([(Channel(0), rateHz)]));
}
