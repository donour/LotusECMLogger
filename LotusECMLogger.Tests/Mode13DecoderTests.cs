using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the pure service 0x13 ("read all DTCs") decode against canned response buffers,
    /// including the worked examples in T6-mode13-programming.md §8. The service has no count
    /// byte and concatenates the current, confirmed and TPMS sets, so the decoder must read
    /// codes straight after the SID and collapse the repeats that overlap produces.
    /// </summary>
    public sealed class Mode13DecoderTests
    {
        /// <summary>Prepends the ECM response header the J2534 channel delivers.</summary>
        private static byte[] Response(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        [Fact]
        public void Decode_WorkedExample_TwoCodes()
        {
            // §8 example 1: payload 53 03 01 04 20.
            var result = Mode13Decoder.Decode(Response(0x53, 0x03, 0x01, 0x04, 0x20));

            Assert.Equal(["P0301", "P0420"], result.Codes.Select(c => c.Code));
            Assert.Equal(2, result.ReportedCodeCount);
            Assert.Equal("53 03 01 04 20", result.RawHex);
        }

        [Fact]
        public void Decode_WorkedExample_MultiFrameReassembled()
        {
            // §8 example 3: the four codes the ECU splits across a first and consecutive frame,
            // as the J2534 ISO15765 layer hands them back reassembled.
            var result = Mode13Decoder.Decode(
                Response(0x53, 0x03, 0x01, 0x03, 0x02, 0x04, 0x20, 0x05, 0x00));

            Assert.Equal(["P0301", "P0302", "P0420", "P0500"], result.Codes.Select(c => c.Code));
            Assert.Equal(4, result.ReportedCodeCount);
        }

        [Fact]
        public void Decode_NoCodes_ReturnsEmpty()
        {
            var result = Mode13Decoder.Decode(Response(0x53));

            Assert.Empty(result.Codes);
            Assert.Equal(0, result.ReportedCodeCount);
            Assert.Equal("53", result.RawHex);
        }

        [Fact]
        public void Decode_CollapsesDuplicatesKeepingFirstSeenOrder()
        {
            // A fault present in both the current and confirmed sets arrives twice.
            var result = Mode13Decoder.Decode(
                Response(0x53, 0x03, 0x01, 0x04, 0x20, 0x03, 0x01));

            Assert.Equal(["P0301", "P0420"], result.Codes.Select(c => c.Code));
            Assert.Equal(3, result.ReportedCodeCount);
        }

        [Fact]
        public void Decode_SkipsZeroPadding()
        {
            var result = Mode13Decoder.Decode(Response(0x53, 0x03, 0x01, 0x00, 0x00));

            Assert.Equal("P0301", Assert.Single(result.Codes).Code);
            Assert.Equal(1, result.ReportedCodeCount);
        }

        [Fact]
        public void Decode_IgnoresDanglingOddByte()
        {
            // A truncated response leaves a byte with no pair; it cannot form a code.
            var result = Mode13Decoder.Decode(Response(0x53, 0x03, 0x01, 0x04));

            Assert.Equal("P0301", Assert.Single(result.Codes).Code);
        }

        [Fact]
        public void Decode_ReadsCodesStraightAfterSid_NoCountByte()
        {
            // Service 0x03 would treat a leading 0x02 as a DTC count; service 0x13 must not,
            // so an odd-length payload still starts a code at the byte after the SID.
            var result = Mode13Decoder.Decode(Response(0x53, 0x02, 0x03, 0x01, 0x04, 0x20));

            Assert.Equal(["P0203", "P0104"], result.Codes.Select(c => c.Code));
        }

        [Fact]
        public void Decode_DecodesEveryCategoryPrefix()
        {
            // Top two bits select P/C/B/U: 0x03 01, 0x43 01, 0x83 01, 0xC3 01.
            var result = Mode13Decoder.Decode(
                Response(0x53, 0x03, 0x01, 0x43, 0x01, 0x83, 0x01, 0xC3, 0x01));

            Assert.Equal(["P0301", "C0301", "B0301", "U0301"], result.Codes.Select(c => c.Code));
            Assert.Equal(
                [DtcCategory.Powertrain, DtcCategory.Chassis, DtcCategory.Body, DtcCategory.Network],
                result.Codes.Select(c => c.Category));
        }

        [Fact]
        public void Decode_ThrowsOnWrongSid()
        {
            Assert.Throws<ArgumentException>(() => Mode13Decoder.Decode(Response(0x43, 0x03, 0x01)));
        }

        [Fact]
        public void Decode_ThrowsOnShortBuffer()
        {
            Assert.Throws<ArgumentException>(() => Mode13Decoder.Decode([0x00, 0x00, 0x07, 0xE8]));
        }
    }
}
