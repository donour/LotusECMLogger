using System.Buffers.Binary;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Logging;

namespace LotusECMLogger.Tests;

public sealed class AbsPressureTests
{
    private static byte[] Reply(short raw = 120)
    {
        byte[] data = new byte[22];
        data[0] = 0x61; data[1] = 0x04;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), raw);
        return data;
    }

    private static IEnumerable<byte[]> Frames(byte[] payload)
    {
        yield return [0x10, 22, .. payload.Take(6)];
        int sequence = 1;
        foreach (byte[] part in payload.Skip(6).Chunk(7))
            yield return [(byte)(0x20 | sequence++), .. part, .. new byte[7 - part.Length]];
    }

    // Complete replies from stationary 2011 Evora captures, including applied pedal and idle negatives.
    [Theory]
    [InlineData("61040000000000000000000000000000000002950000", 0)]
    [InlineData("61040000000000000000000000003E00000092950000", 62)]
    [InlineData("61040000000000000000000000009100000092950000", 145)]
    [InlineData("61040000000000000000000000008500000092950000", 133)]
    [InlineData("6104000000000000000000000000FEFF000092950000", -2)]
    [InlineData("6104000000000000000000000000FFFF000091940000", -1)]
    public void CapturedPayloadDecodesIdenticallyOnBothTransports(string hex, short expected)
    {
        byte[] payload = Convert.FromHexString(hex);
        var iso = new AbsPressureProtocol(0);
        var raw = new AbsPressureProtocol(0);
        iso.Tick(0); raw.Tick(0);
        iso.ReceivePayload(payload, 50);
        int index = 0;
        foreach (byte[] frame in Frames(payload))
        {
            byte[]? flow = raw.ReceiveCan(frame, 20 + index * 10);
            if (index++ == 0) Assert.Equal(new byte[] { 0x30, 0, 0, 0, 0, 0, 0, 0 }, flow);
            else Assert.Null(flow);
        }
        Assert.Equal(expected, iso.Snapshot(50).Raw);
        Assert.Equal(expected * 0.3255, iso.Snapshot(50).Bar!.Value, 8);
        Assert.Equal(iso.Snapshot(50), raw.Snapshot(50));
    }

    [Theory]
    [InlineData(short.MinValue)] [InlineData(short.MaxValue)]
    public void SignedExtremesDoNotOverflow(short value)
    {
        var client = new AbsPressureProtocol(0);
        client.Tick(0);
        client.ReceivePayload(Reply(value), 10);
        Assert.Equal(value, client.Snapshot(10).Raw);
        Assert.Equal(value * 0.3255, client.Snapshot(10).Bar);
    }

    [Fact]
    public void RequestIntervalIsBoundedWithoutStoppingAfterSixtySeconds()
    {
        var client = new AbsPressureProtocol(0);
        for (long now = 0; now <= 65_000; now += 200)
        {
            Assert.Equal(new byte[] { 0x21, 0x04 }, client.Tick(now));
            Assert.Null(client.Tick(now + 1)); // One request in flight.
            client.ReceivePayload(Reply(), now + 10);
            Assert.Null(client.Tick(now + 199)); // At most 5 Hz.
        }
        Assert.True(client.Polling);
    }

    [Fact]
    public void GenuineZeroIsValidAndStalePressureBecomesBlank()
    {
        var client = new AbsPressureProtocol(0);
        client.Tick(0);
        client.ReceivePayload(Reply(0), 10);
        Assert.True(client.Snapshot(1010).Valid);
        Assert.Equal(0, client.Snapshot(1010).Bar);
        var stale = client.Snapshot(1011);
        Assert.False(stale.Valid);
        Assert.Null(stale.Raw);
        Assert.Null(stale.Bar);
        Assert.Equal(1001, stale.AgeMs);
        Assert.True(double.IsNaN(stale.Values[0]));
    }

    [Theory]
    [InlineData(0x10)] [InlineData(0x12)] [InlineData(0x21)] [InlineData(0x31)] [InlineData(0x33)]
    public void RejectionClearsEarlierValueAndNeverRetriesOrChangesSession(byte nrc)
    {
        var client = new AbsPressureProtocol(0);
        client.Tick(0);
        client.ReceivePayload(Reply(), 20);
        client.Tick(200);
        client.ReceivePayload([0x7F, 0x21, nrc], 210);
        Assert.False(client.Snapshot(211).Valid);
        Assert.Contains($"0x{nrc:X2}", client.Status);
        Assert.Null(client.Tick(10_000));
    }

    [Fact]
    public void PendingDoesNotExtendDeadlineAndLateFirstFrameCannotGetFlowControl()
    {
        var client = new AbsPressureProtocol(0);
        Assert.Null(client.ReceiveCan(Frames(Reply()).First(), 0)); // Unsolicited.
        client.Tick(0);
        client.ReceivePayload([0x7F, 0x21, 0x78], 700);
        Assert.True(client.Pending);
        Assert.Null(client.ReceiveCan(Frames(Reply()).First(), 750));
        Assert.False(client.Polling);
        Assert.False(client.Snapshot(751).Valid);
        Assert.Null(client.Tick(10_000));
    }

    [Theory]
    [InlineData("1015610400000000")]
    [InlineData("1017610400000000")]
    [InlineData("1016610500000000")]
    [InlineData("10166104")]
    [InlineData("0061040000000000")]
    [InlineData("0761040000000000")]
    [InlineData("2100000000000000")]
    [InlineData("3000000000000000")]
    public void MalformedFramesStopWithoutFlowControl(string hex)
    {
        var client = new AbsPressureProtocol(0);
        client.Tick(0);
        Assert.Null(client.ReceiveCan(Convert.FromHexString(hex), 1));
        Assert.False(client.Polling);
    }

    [Theory]
    [InlineData("2200000000000000")]
    [InlineData("2100")]
    [InlineData("1016610400000000")]
    public void WrongSequenceShortConsecutiveOrDuplicateFirstFrameStops(string hex)
    {
        var client = new AbsPressureProtocol(0);
        client.Tick(0);
        Assert.NotNull(client.ReceiveCan(Frames(Reply()).First(), 1));
        client.ReceiveCan(Convert.FromHexString(hex), 2);
        Assert.False(client.Polling);
        Assert.Null(client.Tick(1000));
    }

    [Fact]
    public void DriverPayloadMustHaveExactLengthAndHeader()
    {
        foreach (var payload in new[] { Reply()[..21], new byte[22], Reply().Concat(new byte[1]).ToArray() })
        {
            var client = new AbsPressureProtocol(0);
            client.Tick(0);
            client.ReceivePayload(payload, 10);
            Assert.False(client.Polling);
            Assert.False(client.Snapshot(11).Valid);
        }
    }

    [Fact]
    public void RouterLeavesEngineAndAemTrafficAndIgnoresDriverMetadata()
    {
        long now = 0;
        List<byte[]> sent = [];
        var client = new AbsPressurePoller(sent.Add, true, () => now);
        client.Tick();
        Assert.Equal(Convert.FromHexString("000006F40221040000000000"), Assert.Single(sent));
        Assert.False(client.Receive(0x351, [0, 1, 2, 3]));
        Assert.False(client.Receive(0x7E8, [0x41, 0x0C, 0x10, 0]));
        Assert.False(client.Receive(0x7E9, [6, 0x41, 0x24, 0, 0, 0, 0]));
        Assert.True(client.Receive(0x6F4, [2, 0x10, 0x89], isData: false));
        foreach (var frame in Frames(Reply(145)))
        {
            now += 10;
            client.Receive(0x6F5, frame, isData: false);
            client.Receive(0x6F5, frame);
        }
        Assert.Equal((short)145, client.Snapshot().Raw);
        Assert.Equal(Convert.FromHexString("000006F43000000000000000"), sent[1]);
        Assert.Equal(2, sent.Count); // Only read and CTS, no session/security/routine commands.
    }

    [Fact]
    public void DriverReassemblySendsOnlyApplicationReadAndOtherTesterStopsPolling()
    {
        long now = 0;
        List<byte[]> sent = [];
        var client = new AbsPressurePoller(sent.Add, false, () => now);
        client.Tick();
        Assert.Equal(Convert.FromHexString("000006F42104"), Assert.Single(sent));
        now = 40;
        client.Receive(0x6F5, Reply(62));
        Assert.True(client.Snapshot().Valid);
        client.Receive(0x6F4, [2, 0x10, 0x89]);
        now = 200;
        client.Tick();
        Assert.Single(sent);
        Assert.False(client.Snapshot().Valid);
        Assert.Contains("Another ABS", client.Snapshot().Status);
    }

    [Fact]
    public void CancellationPreventsRequestAndFlowControl()
    {
        bool stopping = false;
        List<byte[]> sent = [];
        var client = new AbsPressurePoller(sent.Add, true, () => 0, () => stopping);
        client.Tick();
        stopping = true;
        client.Receive(0x6F5, Frames(Reply()).First());
        client.Tick();
        Assert.Single(sent);
    }

    [Fact]
    public void SendFailureRemainsUnavailableWithoutRetry()
    {
        int attempted = 0;
        long now = 0;
        var client = new AbsPressurePoller(_ => { attempted++; throw new IOException("Adapter disconnected"); }, false, () => now);
        client.Tick();
        now = 10_000;
        client.Tick();
        Assert.Equal(1, attempted);
        Assert.False(client.Snapshot().Valid);
        Assert.Contains("Adapter disconnected", client.Snapshot().Status);
    }

    [Fact]
    public void CsvKeepsProducerSnapshotAndClearsPressureOnTimeoutWhileEngineValuesContinue()
    {
        string file = Path.Combine(Path.GetTempPath(), $"abs-pressure-{Guid.NewGuid()}.csv");
        try
        {
            var client = new AbsPressureProtocol(0);
            client.Tick(0);
            client.ReceivePayload(Reply(62), 50);
            var queued = client.Snapshot(100);
            client.Tick(200);
            client.Tick(950); // Missing next response, like the vehicle timeout captures.
            using (var sink = new CsvSampleSink(file, new SampleLogHeader("Replay"),
                new[] { SampleColumn.Number("Engine") }.Concat(AbsPressureProtocol.Columns.Select(SampleColumn.Number))
                    .Append(SampleColumn.Text(AbsPressureProtocol.StatusColumn))))
            {
                sink.Set("Engine", 1); queued.WriteTo(sink); sink.WriteRow(DateTime.Now, 100);
                sink.Set("Engine", 2); client.Snapshot(950).WriteTo(sink); sink.WriteRow(DateTime.Now, 950);
            }
            string[] lines = File.ReadAllLines(file).Where(l => !l.StartsWith('#')).ToArray();
            Assert.Contains(",1,20.181,62,1,50,Pressure received", lines[1]);
            Assert.Contains(",2,,,0,,21 04 response timeout; ABS polling stopped", lines[2]);
        }
        finally { File.Delete(file); }
    }
}
