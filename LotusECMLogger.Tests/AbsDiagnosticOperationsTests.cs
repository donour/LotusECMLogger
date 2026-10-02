using LotusECMLogger.Services;

namespace LotusECMLogger.Tests;

public class AbsDiagnosticOperationsTests
{
    [Fact]
    public void BaselineUsesOnlyTheBoundedReadPlanAndRetainsPartialFailures()
    {
        var requests = new List<string>();
        var client = new AbsDiagnosticOperations((request, _) =>
        {
            string hex = Convert.ToHexString(request);
            requests.Add(hex);
            if (hex == "1A86") throw new IOException("adapter timeout");
            return hex switch
            {
                "1089" => KwpResponse.Negative([0x7f, 0x10, 0x22]),
                "2101" => KwpResponse.Positive([0x61, 0x01, 0x07, 0x41]),
                "21BF" => KwpResponse.Positive([0x61, 0xbf, 0x99]),
                _ => KwpResponse.Negative([0x7f, request[0], 0x12]),
            };
        });
        var baseline = client.ReadBaseline();
        Assert.Equal(new[] { "1089", "1A85", "1A86", "1A87", "1A93", "1A9C", "2101", "21BF", "2104" }, requests);
        Assert.Equal(9, baseline.Exchanges.Count);
        Assert.Contains(baseline.Exchanges, e => e.RequestHex == "1A86" && !e.Success && e.Error == "adapter timeout");
        Assert.Contains(baseline.Exchanges, e => e.RequestHex == "21BF" && e.ResponseHex == "61BF99");
        Assert.Equal("unknown", baseline.FirmwareReference);
        Assert.All(baseline.Exchanges, e => Assert.Equal(TimeSpan.Zero, e.TimestampUtc.Offset));
        Assert.True(baseline.Exchanges.Select(e => e.ElapsedMilliseconds).SequenceEqual(
            baseline.Exchanges.Select(e => e.ElapsedMilliseconds).OrderBy(x => x)));
        client.ReadSample(baseline);
        Assert.Equal("2104", requests[^1]);
        Assert.Equal(10, requests.Count);
    }

    [Fact]
    public void CancellationPreventsTheNextTransmission()
    {
        using var cancellation = new CancellationTokenSource();
        int sent = 0;
        var client = new AbsDiagnosticOperations((request, _) =>
        {
            sent++;
            cancellation.Cancel();
            return KwpResponse.Positive([0x50, 0x89]);
        }, cancellation.Token);
        Assert.Single(client.ReadBaseline().Exchanges);
        Assert.Equal(1, sent);
        Assert.Empty(client.ReadBaseline().Exchanges);
        Assert.Equal(1, sent);
    }

    [Theory]
    [InlineData("2104", "61010741")]
    [InlineData("1A85", "5A86FF")]
    [InlineData("1089", "5081")]
    [InlineData("2701", "6702FFFF")]
    [InlineData("2104", "7F1A12")]
    [InlineData("2104", "7E")]
    [InlineData("17C150", "57C151A0")]
    [InlineData("17C150", "57C150")]
    public void UnrelatedOrStaleRepliesCannotSatisfyARequest(string request, string response)
    {
        Assert.Equal(AbsResponseKind.Ignore, AbsKwpResponseMatcher.Match(
            Convert.FromHexString(request), Convert.FromHexString(response), out _));
    }

    [Fact]
    public void PendingAndMalformedRepliesKeepTheirOriginalBytes()
    {
        Assert.Equal(AbsResponseKind.Pending, AbsKwpResponseMatcher.Match([0x21, 4], [0x7f, 0x21, 0x78], out var pending));
        Assert.Equal(new byte[] { 0x7f, 0x21, 0x78 }, pending.RawResponse);
        Assert.False(pending.Ok);
        Assert.Equal(AbsResponseKind.Complete, AbsKwpResponseMatcher.Match([0x21, 4], [0x7f, 0x21], out var malformed));
        Assert.False(malformed.Ok);
        Assert.Equal(new byte[] { 0x7f, 0x21 }, malformed.RawResponse);
        Assert.Equal(AbsResponseKind.Complete, AbsKwpResponseMatcher.Match([0x21, 4], [0x61], out var shortReply));
        Assert.False(shortReply.Ok);
        Assert.Equal(AbsResponseKind.Complete, AbsKwpResponseMatcher.Match([0x21, 1], [0x61, 1, 7, 0x41], out var valid));
        Assert.Equal(new byte[] { 0x61, 1, 7, 0x41 }, valid.RawResponse);
        Assert.Equal(new byte[] { 1, 7, 0x41 }, valid.Payload);
    }

    [Fact]
    public void ApplicationSecurityHasExactlyTwoBigEndianBytes()
    {
        Assert.Equal(new byte[] { 0x40, 0x14 }, AbsProtocol.ComputeKey([0x12, 0x34]));
        Assert.Equal(new byte[] { 0x52, 0x20 }, AbsProtocol.ComputeKey([0, 0]));
        Assert.Throws<ArgumentException>(() => AbsProtocol.ComputeKey([0x11, 0x22, 0x33, 0x44]));
        Assert.Throws<ArgumentException>(() => AbsProtocol.ComputeKey([]));
        Assert.Equal(0x81, AbsProtocol.SessionDefault);
        Assert.Equal(0x89, AbsProtocol.SessionTester);
    }

    [Fact]
    public void DeviceLeaseRejectsOverlapAndDoesNotReleaseAnotherOwnersLease()
    {
        var first = J2534DeviceLease.Acquire();
        try { Assert.Throws<InvalidOperationException>(() => J2534DeviceLease.Acquire()); }
        finally { first.Dispose(); }
        using var second = J2534DeviceLease.Acquire();
        first.Dispose();
        Assert.Throws<InvalidOperationException>(() => J2534DeviceLease.Acquire());
    }
}
