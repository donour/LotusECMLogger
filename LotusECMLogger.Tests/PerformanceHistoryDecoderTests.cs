using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    public sealed class PerformanceHistoryDecoderTests
    {
        [Fact]
        public void DecodeSupportedPids_UsesStandardMostSignificantBitOrdering()
        {
            IReadOnlySet<ushort> pids = PerformanceHistoryDecoder.DecodeSupportedPids(
                0x0300, [0x81, 0x00, 0x00, 0x01]);

            Assert.Equal(new ushort[] { 0x0301, 0x0308, 0x0320 }, pids.Order());
        }

        [Theory]
        [InlineData("B13200091", "EvoraS1")]
        [InlineData("cal C132E0271 US", "Evora400")]
        [InlineData("C132E0278", "EvoraGt430")]
        [InlineData("E132E0288_2021", "EvoraGt")]
        [InlineData("other", "Unknown")]
        public void ProfileResolution_RecognisesAnalysedFirmware(string calibration, string expected)
        {
            Assert.Equal(expected, PerformanceHistoryProfile.Resolve(calibration).Kind.ToString());
        }

        [Fact]
        public void Decode_CommonFieldsAndEvents_ApplyFirmwareScaling()
        {
            var payloads = new Dictionary<ushort, byte[]>
            {
                [0x0301] = U32(25),
                [0x031E] = U32(7_200),
                [0x0324] = U32(160), // Last byte 160 => 60 C.
                [0x0325] = U32(123),
                [0x0333] = U16(270),
                [0x0334] = [42],
                [0x0338] = U32(36_000),
                [0x0339] = U16(12),
                [0x0342] = [5],
                [0x0343] = [80],
                [0x0344] = U16(7_000),
                [0x0345] = I16(-12),
                [0x0346] = U32(100),
                [0x0361] = [3],
            };

            PerformanceHistorySnapshot result = PerformanceHistoryDecoder.Decode("E132E0288", payloads);

            Assert.Equal(TimeSpan.FromHours(1), result.EngineRuntime);
            Assert.Equal(12, result.StandingStartCount);
            Assert.Equal(4.2, result.FastestZeroTo100Seconds);
            Assert.Equal(3, result.LowOilPressureEventCount);

            PerformanceUsageBucket throttle = Assert.Single(result.Usage);
            Assert.Equal("Throttle position", throttle.Category);
            Assert.Equal(TimeSpan.FromSeconds(2.5), throttle.Duration);

            PerformanceHistoryEvent topRpm = Assert.Single(result.Events, x => x.Category == "Highest engine speed");
            Assert.Equal(7_200, topRpm.Value);
            Assert.Equal(60, topRpm.ContextValue);
            Assert.Equal(TimeSpan.FromSeconds(12.3), topRpm.EngineRuntime);

            PerformanceHistoryEvent topSpeed = Assert.Single(result.Events, x => x.Category == "Highest vehicle speed");
            Assert.Equal(270, topSpeed.Value);
            Assert.Equal(1, topSpeed.Rank);

            PerformanceHistoryEvent lowOil = Assert.Single(result.Events, x => x.Category == "Low oil pressure");
            Assert.Equal(0.5, lowOil.Value);
            Assert.Equal(80, lowOil.VehicleSpeedKph);
            Assert.Equal(7_000, lowOil.EngineSpeedRpm);
            Assert.Equal(-1.2, lowOil.ContextValue);
            Assert.Equal(TimeSpan.FromSeconds(10), lowOil.EngineRuntime);
        }

        [Fact]
        public void Decode_S2DistanceAndSixthLateralBand_UseVariantSpecificPids()
        {
            var payloads = new Dictionary<ushort, byte[]>
            {
                [0x033A] = [0x03, 0x00, 0x01, 0xE2, 0x40], // Prefix + 123456 km.
                [0x0341] = U32(90),
            };

            PerformanceHistorySnapshot result = PerformanceHistoryDecoder.Decode("C132E0278", payloads);

            Assert.Equal(123_456, result.DistanceKm);
            PerformanceUsageBucket sixth = Assert.Single(result.Usage);
            Assert.Equal("Lateral acceleration", sixth.Category);
            Assert.Equal("ECU band 6", sixth.Band);
            Assert.Equal(TimeSpan.FromSeconds(9), sixth.Duration);
        }

        [Fact]
        public void Decode_LateGtUses0341ForDistance_NotForLateralUsage()
        {
            var payloads = new Dictionary<ushort, byte[]> { [0x0341] = U32(54_321) };

            PerformanceHistorySnapshot result = PerformanceHistoryDecoder.Decode("E132E0288", payloads);

            Assert.Equal(54_321, result.DistanceKm);
            Assert.Empty(result.Usage);
        }

        [Fact]
        public void Decode_S1DoesNotPresent0341AsDistance()
        {
            var payloads = new Dictionary<ushort, byte[]> { [0x0341] = U32(10) };

            PerformanceHistorySnapshot result = PerformanceHistoryDecoder.Decode("B13200091", payloads);

            Assert.Null(result.DistanceKm);
            Assert.Equal("ECU band 6", Assert.Single(result.Usage).Band);
        }

        private static byte[] U16(ushort value) => [(byte)(value >> 8), (byte)value];
        private static byte[] I16(short value) => U16(unchecked((ushort)value));
        private static byte[] U32(uint value) =>
            [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }
}
