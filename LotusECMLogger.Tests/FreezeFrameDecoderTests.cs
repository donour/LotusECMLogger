using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the pure Mode 02 (freeze frame) decode helpers against canned response
    /// buffers: SID/frame-byte normalization, reuse of the Mode 01 parser, the
    /// frame-shifted supported-PID bitmask, and triggering-DTC extraction.
    /// </summary>
    public sealed class FreezeFrameDecoderTests
    {
        /// <summary>Prepends the ECM response header the J2534 channel delivers.</summary>
        private static byte[] Response(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        // ── NormalizeToMode01 ─────────────────────────────────────────────────────────────

        [Fact]
        public void Normalize_RewritesSidAndDropsFrameByte()
        {
            byte[] normalized = FreezeFrameDecoder.NormalizeToMode01(Response(0x42, 0x05, 0x00, 0x7B));

            Assert.Equal(Response(0x41, 0x05, 0x7B), normalized);
        }

        [Fact]
        public void Normalize_ThrowsOnWrongSid()
        {
            Assert.Throws<ArgumentException>(
                () => FreezeFrameDecoder.NormalizeToMode01(Response(0x41, 0x05, 0x00, 0x7B)));
        }

        [Fact]
        public void Normalize_ThrowsOnShortBuffer()
        {
            Assert.Throws<ArgumentException>(
                () => FreezeFrameDecoder.NormalizeToMode01(Response(0x42, 0x05)));
        }

        // ── DecodePidResponse ─────────────────────────────────────────────────────────────

        [Fact]
        public void Decode_KnownOneBytePid_CoolantTemperature()
        {
            var entries = FreezeFrameDecoder.DecodePidResponse(Response(0x42, 0x05, 0x00, 0x7B));

            var entry = Assert.Single(entries);
            Assert.Equal("Coolant Temperature", entry.Name);
            Assert.Equal("83", entry.Value); // 0x7B - 40 °C
            Assert.Equal("7B", entry.RawHex);
            Assert.True(entry.IsDecoded);
        }

        [Fact]
        public void Decode_KnownTwoBytePid_EngineSpeed()
        {
            var entries = FreezeFrameDecoder.DecodePidResponse(Response(0x42, 0x0C, 0x00, 0x1A, 0xF8));

            var entry = Assert.Single(entries);
            Assert.Equal("Engine Speed", entry.Name);
            Assert.Equal("1726", entry.Value); // 0x1AF8 / 4 rpm
            Assert.Equal("1A F8", entry.RawHex);
        }

        [Fact]
        public void Decode_UnknownPid_FallsBackToRawEntry()
        {
            var entries = FreezeFrameDecoder.DecodePidResponse(Response(0x42, 0x21, 0x00, 0x12, 0x34));

            var entry = Assert.Single(entries);
            Assert.Equal("PID 0x21", entry.Name);
            Assert.Null(entry.Value);
            Assert.Equal("12 34", entry.RawHex);
            Assert.False(entry.IsDecoded);
        }

        // ── ParseSupportedPids ────────────────────────────────────────────────────────────

        [Fact]
        public void SupportedPids_ReadsBitmaskShiftedByFrameByte()
        {
            // 0xBE 0x1F 0xA8 0x13 — the last bit set marks PID 0x20, the continuation page.
            var pids = FreezeFrameDecoder.ParseSupportedPids(
                Response(0x42, 0x00, 0x00, 0xBE, 0x1F, 0xA8, 0x13), basePid: 0x00);

            Assert.Equal(
                new[] { 0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x13, 0x15, 0x1C, 0x1F, 0x20 },
                pids);
        }

        [Fact]
        public void SupportedPids_ContinuationPageOffsetsFromBasePid()
        {
            var pids = FreezeFrameDecoder.ParseSupportedPids(
                Response(0x42, 0x20, 0x00, 0x80, 0x00, 0x00, 0x00), basePid: 0x20);

            Assert.Equal(new[] { 0x21 }, pids);
        }

        [Fact]
        public void SupportedPids_MismatchedBasePidYieldsEmpty()
        {
            var pids = FreezeFrameDecoder.ParseSupportedPids(
                Response(0x42, 0x00, 0x00, 0xFF), basePid: 0x20);

            Assert.Empty(pids);
        }

        // ── ParseTriggeringDtc ────────────────────────────────────────────────────────────

        [Fact]
        public void TriggeringDtc_DecodesCode()
        {
            var dtc = FreezeFrameDecoder.ParseTriggeringDtc(Response(0x42, 0x02, 0x00, 0x01, 0x71));

            Assert.NotNull(dtc);
            Assert.Equal("P0171", dtc.Code);
            Assert.Equal(DtcCategory.Powertrain, dtc.Category);
        }

        [Fact]
        public void TriggeringDtc_ZeroedCodeMeansNoFrameStored()
        {
            Assert.Null(FreezeFrameDecoder.ParseTriggeringDtc(Response(0x42, 0x02, 0x00, 0x00, 0x00)));
        }
    }
}
