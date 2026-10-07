using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

public class CcpFrameTests
{
    [Fact]
    public void EncodesDocumentedSessionWithDlc8AndBigEndianAddresses()
    {
        byte[][] actual =
        [
            CcpFrame.Connect(0), CcpFrame.GetVersion(1), CcpFrame.GetDaqSize(2, CcpTarget.T6, 0),
            CcpFrame.SetDaqPointer(3, CcpTarget.T6, 0, 0, 0), CcpFrame.WriteDaq(4, 0x400017DA, 2),
            CcpFrame.SetDaqPointer(5, CcpTarget.T6, 0, 0, 1), CcpFrame.WriteDaq(6, 0x400018AE, 1),
            CcpFrame.StartStop(7, CcpTarget.T6, CcpStartStopMode.Prepare, 0),
            CcpFrame.StartStopAll(8, true), CcpFrame.StartStopAll(9, false), CcpFrame.Disconnect(10),
        ];
        string[] expected =
        [
            "0100000000000000", "1B01020100000000", "1402000000000351",
            "1503000000000000", "16040200400017DA", "1505000001000000", "16060100400018AE",
            "0607020000000001", "0808010000000000", "0809000000000000", "070A010000000000",
        ];
        Assert.Equal(expected, actual.Select(Convert.ToHexString));
        Assert.All(actual, frame => Assert.Equal(8, frame.Length));
    }

    [Fact]
    public void EncodesMemoryTransfersWithoutUsingHostByteOrder()
    {
        Assert.Equal("0242000040008654", Convert.ToHexString(CcpFrame.SetMta(0x42, 0x40008654)));
        Assert.Equal("0F4305004000E028", Convert.ToHexString(CcpFrame.ShortUpload(0x43, 0x4000E028, 5)));
        Assert.Equal("0344031020300000", Convert.ToHexString(CcpFrame.Download(0x44, [0x10, 0x20, 0x30])));
        Assert.Equal("2345010203040506", Convert.ToHexString(CcpFrame.Download6(0x45, [1, 2, 3, 4, 5, 6])));
        Assert.Equal("0E460000FFFF0000", Convert.ToHexString(CcpFrame.BuildChecksum(0x46, 65535)));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(6)] [InlineData(255)]
    public void RejectsUnsafeTransferLengths(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.Upload(0, size));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.ShortUpload(0, 0x40000000, size));
        if (size >= 0) Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.Download(0, new byte[size]));
    }

    [Theory]
    [InlineData(0)] [InlineData(5)] [InlineData(7)]
    public void Download6RequiresExactlySixBytes(int size) =>
        Assert.Throws<ArgumentException>(() => CcpFrame.Download6(0, new byte[size]));

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(65536)] [InlineData(131072)]
    public void RejectsChecksumSizesThatNeverReplyOrTruncate(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.BuildChecksum(0, size));

    [Theory]
    [InlineData(-1)] [InlineData(7)] [InlineData(8)]
    public void RejectsElementAliasBug(int element) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.SetDaqPointer(0, CcpTarget.T6, 0, 0, element));

    [Fact]
    public void RejectsOutOfRangeDaqAndUnsupported2010Event()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.GetDaqSize(0, CcpTarget.Evora2010, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.SetDaqPointer(0, CcpTarget.T6, 10, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.SetDaqPointer(0, CcpTarget.T6, 0, 12, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.WriteDaq(0, 0x40000000, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.WriteDaq(0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.StartStop(0, CcpTarget.Evora2010,
            CcpStartStopMode.Prepare, 0, eventChannel: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.StartStop(0, CcpTarget.T6,
            CcpStartStopMode.Start, 0, prescaler: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CcpFrame.StartStop(0, CcpTarget.T6,
            (CcpStartStopMode)3, 0));
    }

    [Fact]
    public void ParsesCrmFieldsAsResourcesRatherThanMagic()
    {
        var packet = CcpFrame.ParseDto([0xFF, 0, 255, 6, 0, 3, 0, 0x8E], CcpTarget.T6);
        packet.EnsureSuccess(CcpCommandCode.ExchangeId, 255);
        var id = CcpIdentification.Parse(packet);
        Assert.Equal(new CcpIdentification(6, 0, 3, 0, 0x8E), id);
        Assert.Throws<InvalidOperationException>(() => packet.EnsureSuccess(CcpCommandCode.ExchangeId, 0));
    }

    [Theory]
    [InlineData(0x22)] [InlineData(0x30)] [InlineData(0x31)] [InlineData(0x32)] [InlineData(0x33)] [InlineData(0x7F)]
    public void PreservesKnownAndUnknownErrorCodes(byte error)
    {
        var packet = CcpFrame.ParseDto([0xFF, error, 7, 0, 0, 0, 0, 0], CcpTarget.T6);
        var exception = Assert.Throws<CcpException>(() => packet.EnsureSuccess(CcpCommandCode.Upload, 7));
        Assert.Equal(error, exception.ErrorCode);
        Assert.Equal((byte)7, exception.Counter);
    }

    [Fact]
    public void SeparatesEventsFromDaqAndDecodesOverrunPid()
    {
        var packet = CcpFrame.ParseDto([0xF7, 1, 2, 3, 4, 5, 6, 7], CcpTarget.T6);
        Assert.Equal((byte)119, packet.Pid);
        Assert.True(packet.Overrun);
        Assert.Equal(CcpPacketKind.Event, CcpFrame.ParseDto([0xFE, 1, 0, 0, 0, 0, 0, 0], CcpTarget.T6).Kind);
        Assert.Throws<ArgumentException>(() => CcpFrame.ParseDto([0xF8, 0, 0, 0, 0, 0, 0, 0], CcpTarget.T6));
        Assert.Throws<ArgumentException>(() => CcpFrame.ParseDto([10, 0, 0, 0, 0, 0, 0, 0], CcpTarget.Evora2010));
        Assert.Throws<ArgumentException>(() => CcpFrame.ParseDto([0xFF, 0, 0], CcpTarget.T6));
    }

    [Fact]
    public void PacketOwnsItsBytes()
    {
        byte[] source = [0, 10, 20, 30, 0, 0, 0, 0];
        var packet = CcpFrame.ParseDto(source, CcpTarget.T6);
        source[1] = 99;
        Assert.Equal((byte)10, packet.Data.Span[0]);
    }

    [Fact]
    public void ProfilesKeepFamilyLimitsAndOnlyVerifiedVersionOffsets()
    {
        Assert.Equal((uint)0x200, CcpTarget.Evora2010.CroId);
        Assert.Equal((uint)0x361, CcpTarget.IpsTcu.DtoId);
        Assert.Equal((byte)108, CcpTarget.T6.ExpectedFirstPid(9));
        Assert.Equal(100, CcpTarget.Evora2010.EventRatesHz[0]);
        Assert.Equal(1, CcpTarget.T6.EventRatesHz[9]);
        Assert.False(CcpTarget.IpsTcu.HasEnableByte);
        var profile = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        Assert.Equal((uint)0x4000E028, profile.CalibrationBase + profile.ProgramVersionOffset!.Value);
        Assert.Null(CcpBuildProfile.Known.Single(p => p.Build == "B13200091").ProgramVersionOffset);
        Assert.True(profile.ContainsCalibrationRange(profile.CalibrationBase, (int)profile.CalibrationSize));
        Assert.False(profile.ContainsCalibrationRange(profile.CalibrationBase, (int)profile.CalibrationSize + 1));
        Assert.False(profile.ContainsCalibrationRange(profile.CalibrationBase - 1, 2));
        Assert.False(profile.ContainsCalibrationRange(uint.MaxValue, 2));
        Assert.False(profile.ContainsCalibrationRange(profile.CalibrationBase, 0));
    }

    [Fact]
    public void AdditiveChecksumWrapsAtSixteenBits() =>
        Assert.Equal((ushort)254, CcpFrame.AdditiveChecksum(Enumerable.Repeat((byte)255, 258).ToArray()));
}
