using System.Security.Cryptography;
using LotusECMLogger.Services;

namespace LotusECMLogger.Tests;

public sealed class AbsFirmwareFlasherTests
{
    [Fact]
    public void BootloaderKeyMatchesCapturedSeedKeyPair()
    {
        Assert.Equal("C36DB630", Convert.ToHexString(AbsProtocol.ComputeBootloaderKey([0x12, 0x34, 0x56, 0x78])));
        Assert.Throws<ArgumentException>(() => AbsProtocol.ComputeBootloaderKey([1, 2]));
    }

    [Fact]
    public void IntelHexParserRejectsChecksumAndAddressGaps()
    {
        string[] valid = [Record(0x0000, 0x04, [0, 0]), Record(0x8000, 0x00, Enumerable.Range(0, 4).Select(i => (byte)i).ToArray()), ":00000001FF"];
        var parsed = new IntelHexParser().Parse(valid);
        Assert.Equal(0x8000u, parsed.StartAddress);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, parsed.Bytes);
        Assert.Throws<FormatException>(() => new IntelHexParser().Parse(valid[..^1].Append(":0480000000010203F6")));
        Assert.Throws<FormatException>(() => new IntelHexParser().Parse([Record(0, 4, [0, 0]), Record(0x8000, 0, [1]), Record(0x8002, 0, [2]), ":00000001FF"]));
        Assert.Throws<FormatException>(() => new IntelHexParser().Parse([Record(1, 4, [0, 0]), ":00000001FF"]));
        Assert.Throws<FormatException>(() => new IntelHexParser().Parse([Record(0x8000, 0, [1]), ":00000001FF", Record(0x8001, 0, [2])]));
    }

    [Fact]
    public void FlashUsesExactFlowAndEchoesEachBlockCounter()
    {
        var bytes = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        var image = Image(bytes);
        var requests = new List<byte[]>();
        KwpResponse Reply(byte[] request, CancellationToken _, int __)
        {
            requests.Add(request.ToArray());
            return request[0] switch
            {
                0x10 => KwpResponse.Positive([0x50, 0x85]),
                0x22 => KwpResponse.Positive([0x62, 0xf1, 0x86, 0x02]),
                0x27 when request[1] == 0x11 => KwpResponse.Positive([0x67, 0x11, 0x12, 0x34, 0x56, 0x78]),
                0x27 => KwpResponse.Positive([0x67, 0x12]),
                0x2e => KwpResponse.Positive([0x6e, 0xf1, 0x5a]),
                0x31 when request[3] == 0 => KwpResponse.Positive([0x71, 0x01, 0xff, 0, 1, 1]),
                0x31 when request[2] == 0xff => KwpResponse.Positive([0x71, 0x01, 0xff, 1]),
                0x31 => KwpResponse.Positive([0x71, 0x01, 2, 2]),
                0x34 => KwpResponse.Positive([0x74, 0x20, 1, 0]),
                0x36 => KwpResponse.Positive([0x76, request[1]]),
                0x37 => KwpResponse.Positive([0x77]),
                _ => KwpResponse.Failure("unexpected"),
            };
        }
        var flasher = new AbsFirmwareFlasher(Reply, () => (true, 13.2, ""), _ => { }, enforceProductionGeometry: false);
        var result = flasher.Flash(image, new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.True(result.Completed, string.Join(";", result.Rows.Select(x => x.Value)));
        Assert.Equal(2, result.BlocksSent);
        Assert.Equal(new byte[] { 1, 2 }, requests.Where(x => x[0] == 0x36).Select(x => x[1]));
        Assert.Equal(12, result.Exchanges.Count);
    }

    [Fact]
    public void ZeroSeedSkipsKeyRequestAndCounterWraps()
    {
        var bytes = new byte[256 * 257];
        var image = Image(bytes);
        var requests = new List<byte[]>();
        KwpResponse Reply(byte[] request, CancellationToken _, int __)
        {
            requests.Add(request.ToArray());
            return request[0] switch
            {
                0x10 => KwpResponse.Positive([0x50, 0x85]), 0x22 => KwpResponse.Positive([0x62, 0xf1, 0x86, 2]),
                0x27 => KwpResponse.Positive([0x67, 0x11, 0, 0, 0, 0]), 0x2e => KwpResponse.Positive([0x6e, 0xf1, 0x5a]),
                0x31 when request[3] == 0 => KwpResponse.Positive([0x71, 1, 0xff, 0, 1, 1]),
                0x31 when request[2] == 0xff => KwpResponse.Positive([0x71, 1, 0xff, 1]),
                0x31 => KwpResponse.Positive([0x71, 1, 2, 2]), 0x34 => KwpResponse.Positive([0x74, 0x20, 1, 0]),
                0x36 => KwpResponse.Positive([0x76, request[1]]), 0x37 => KwpResponse.Positive([0x77]), _ => KwpResponse.Failure("unexpected")
            };
        }
        var result = new AbsFirmwareFlasher(Reply, () => (true, 13, ""), _ => { }, enforceProductionGeometry: false).Flash(image, new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.True(result.Completed);
        Assert.DoesNotContain(requests, request => request[0] == 0x27 && request[1] == 0x12);
        Assert.Equal(new byte[] { 0xff, 0, 1 }, requests.Where(x => x[0] == 0x36).Skip(254).Take(3).Select(x => x[1]));
    }

    [Fact]
    public void CapturedImageGeometryProduces2944BlocksAndFinal240Bytes()
    {
        var image = Image(new byte[753648]);
        image = image with { EndAddressExclusive = 0xBFFF0, Manifest = image.Manifest with { AddressEndExclusive = 0xBFFF0 } };
        var counters = new List<byte>();
        var timeouts = new List<int>();
        int delays = 0;
        KwpResponse Reply(byte[] request, CancellationToken _, int timeout)
        {
            timeouts.Add(timeout);
            return request[0] switch
            {
                0x10 => KwpResponse.Positive([0x50, 0x85]), 0x22 => KwpResponse.Positive([0x62, 0xf1, 0x86, 2]),
                0x27 => KwpResponse.Positive(request[1] == 0x11 ? [0x67, 0x11, 0, 0, 0, 1] : [0x67, 0x12]),
                0x2e => KwpResponse.Positive([0x6e, 0xf1, 0x5a]), 0x31 when request[3] == 0 => KwpResponse.Positive([0x71, 1, 0xff, 0, 1, 1]),
                0x31 when request[2] == 0xff => KwpResponse.Positive([0x71, 1, 0xff, 1]), 0x31 => KwpResponse.Positive([0x71, 1, 2, 2]),
                0x34 => KwpResponse.Positive([0x74, 0x20, 1, 0]), 0x36 => BlockReply(request, counters), 0x37 => KwpResponse.Positive([0x77]), _ => KwpResponse.Failure("unexpected")
            };
        }
        var result = new AbsFirmwareFlasher(Reply, () => (true, 13, ""), _ => delays++, enforceProductionGeometry: false).Flash(image, new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.True(result.Completed);
        Assert.Equal(2944, result.BlocksSent);
        Assert.Equal(753648, result.BytesSent);
        Assert.Equal(2944, counters.Count);
        Assert.Equal(242, result.Exchanges.Last(x => x.RequestHex.StartsWith("36", StringComparison.Ordinal)).RequestHex.Length / 2);
        Assert.Equal(2944, delays);
        Assert.All(timeouts.Where((_, index) => index >= 7 && index < 2951), timeout => Assert.Equal(500, timeout));
    }

    [Fact]
    public void WrongCounterStopsBefore37AndRecordsFailure()
    {
        var requests = new List<byte[]>();
        var flasher = new AbsFirmwareFlasher((request, _, _) => { requests.Add(request); return ProtocolReply(request, wrongCounter: request[0] == 0x36); }, () => (true, 13, ""), _ => { }, enforceProductionGeometry: false);
        var result = flasher.Flash(Image(new byte[256]), new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.False(result.Completed);
        Assert.DoesNotContain(requests, request => request[0] == 0x37);
        Assert.Contains(result.Rows, row => row.Field == "Error" && row.Value.Contains("counter", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0xff, 1)]
    [InlineData(0x02, 2)]
    public void CompletionRejectionStopsAndIsNotReportedComplete(byte routine, byte subroutine)
    {
        var requests = new List<byte[]>();
        var flasher = new AbsFirmwareFlasher((request, _, _) => { requests.Add(request); return ProtocolReply(request, rejectRoutine: (routine, subroutine)); }, () => (true, 13, ""), _ => { }, enforceProductionGeometry: false);
        var result = flasher.Flash(Image(new byte[256]), new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.False(result.Completed);
        Assert.Equal($"3101{routine:X2}{subroutine:X2}", result.Exchanges.Last().RequestHex);
    }

    [Fact]
    public void CancelledBeforeTransmissionSendsNothing()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        int requests = 0;
        var flasher = new AbsFirmwareFlasher((_, _, _) => { requests++; return KwpResponse.Positive([0x50, 0x85]); }, () => (true, 13, ""), _ => { }, enforceProductionGeometry: false);
        var result = flasher.Flash(Image(new byte[256]), new AbsFlashOptions { ConfirmUnresolvedIntegrity = true }, cancellationToken: cancellation.Token);
        Assert.True(result.Cancelled); Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(11.9)]
    [InlineData(double.NaN)]
    public void LowOrInvalidVoltageSendsNoSessionRequest(double voltage)
    {
        int requests = 0;
        var flasher = new AbsFirmwareFlasher((_, _, _) => { requests++; return KwpResponse.Positive([0x50, 0x85]); }, () => (true, voltage, ""), _ => { }, enforceProductionGeometry: false);
        var result = flasher.Flash(Image(new byte[256]), new AbsFlashOptions { ConfirmUnresolvedIntegrity = true });
        Assert.False(result.Completed); Assert.Equal(0, requests);
    }

    private static KwpResponse BlockReply(byte[] request, List<byte> counters)
    {
        counters.Add(request[1]);
        return KwpResponse.Positive([0x76, request[1]]);
    }

    private static KwpResponse ProtocolReply(byte[] request, bool wrongCounter = false, (byte routine, byte subroutine)? rejectRoutine = null) => request[0] switch
    {
        0x10 => KwpResponse.Positive([0x50, 0x85]), 0x22 => KwpResponse.Positive([0x62, 0xf1, 0x86, 2]),
        0x27 when request[1] == 0x11 => KwpResponse.Positive([0x67, 0x11, 0, 0, 0, 1]), 0x27 => KwpResponse.Positive([0x67, 0x12]),
        0x2e => KwpResponse.Positive([0x6e, 0xf1, 0x5a]), 0x31 when request[3] == 0 => KwpResponse.Positive([0x71, 1, 0xff, 0, 1, 1]),
        0x31 when rejectRoutine is { } reject && request[2] == reject.routine && request[3] == reject.subroutine => KwpResponse.Positive([0x71, 1, request[2], unchecked((byte)(request[3] + 1))]),
        0x31 when request[2] == 0xff => KwpResponse.Positive([0x71, 1, 0xff, 1]), 0x31 => KwpResponse.Positive([0x71, 1, 2, 2]),
        0x34 => KwpResponse.Positive([0x74, 0x20, 1, 0]), 0x36 => KwpResponse.Positive([0x76, wrongCounter ? unchecked((byte)(request[1] + 1)) : request[1]]),
        0x37 => KwpResponse.Positive([0x77]), _ => KwpResponse.Failure("unexpected")
    };

    private static AbsFirmwareImage Image(byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new AbsFirmwareImage { SourcePath = "test.hex", Bytes = bytes, StartAddress = 0x8000, EndAddressExclusive = (uint)(0x8000 + bytes.Length), Sha256 = hash, Manifest = new AbsFirmwareManifest { Sha256 = hash, AddressStart = 0x8000, AddressEndExclusive = (uint)(0x8000 + bytes.Length) } };
    }

    private static string Record(ushort address, byte type, byte[] data)
    {
        byte[] record = [(byte)data.Length, (byte)(address >> 8), (byte)address, type, .. data];
        byte checksum = unchecked((byte)(0 - record.Sum(x => x)));
        return ":" + Convert.ToHexString([.. record, checksum]);
    }
}
