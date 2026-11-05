using LotusECMLogger.Services;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>The catalogs' encoded types: size, signedness and the physical = raw × scale + offset conversion.</summary>
public sealed class ChannelTypeParserTests
{
    [Theory]
    // Forms that were already read this way.
    [InlineData("u8_temp_5/8-40c", 1, false, 0.625, -40, "°C")]
    [InlineData("u16_rspeed_rpm", 2, false, 1, 0, "rpm")]
    [InlineData("u8_-127", 1, false, 1, -127, "")]
    [InlineData("u16_factor_1/1023", 2, false, 100.0 / 1023, 0, "%")]
    [InlineData("i16_angle_1/4deg", 2, true, 0.25, 0, "deg")]
    // Raw values stored biased by a constant.
    [InlineData("u8_raw-128", 1, false, 1, -128, "")]
    [InlineData("u16_raw-32768", 2, false, 1, -32768, "")]
    // An offset after the unit, possibly with the unit repeated after it.
    [InlineData("u16_rspeed_rpm-32768", 2, false, 1, -32768, "rpm")]
    [InlineData("u16_flow_10mg/s-327680mg/s", 2, false, 10, -327680, "mg/s")]
    // A fractional offset.
    [InlineData("u16_percent_1/20-1638.4", 2, false, 0.05, -1638.4, "%")]
    // Quantities that used to be shown raw.
    [InlineData("u8_tempdelta_5/8c", 1, false, 0.625, 0, "°C")]
    [InlineData("u16_lambda_1/32768", 2, false, 1.0 / 32768, 0, "λ")]
    [InlineData("u16_freq_hz", 2, false, 1, 0, "Hz")]
    public void ReadsEncodedTypes(string type, int size, bool signed, double scale, double offset, string unit)
    {
        var parsed = ChannelTypeParser.Parse(type);
        Assert.Equal(size, parsed.Size);
        Assert.Equal(signed, parsed.Signed);
        Assert.Equal(scale, parsed.Scale, 9);
        Assert.Equal(offset, parsed.Offset, 9);
        Assert.Equal(unit, parsed.Unit ?? "");
    }

    [Fact]
    public void ArraysKeepTheirElementType()
    {
        var parsed = ChannelTypeParser.Parse("u16_rspeed_rpm-32768[16]");
        Assert.Equal((2, 16, -32768.0, "rpm"), (parsed.Size, parsed.ArrayLength, parsed.Offset, parsed.Unit));
    }
}
