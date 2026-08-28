using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    public sealed class Zt3WidebandDecoderTests
    {
        [Fact]
        public void TryDecode_ValidBroadcast_DecodesAllDocumentedSignals()
        {
            // 0x03E8 × .001 = 1.000 lambda; byte 2 = 1.00 lambda; byte 3 = 14.7 AFR.
            byte[] payload = [0x03, 0xE8, 0x64, 0x93, 0xAA, 0xBB, 0xCC, 0x05];

            bool decoded = Zt3WidebandDecoder.TryDecode(payload, out var sample);

            Assert.True(decoded);
            Assert.Equal(1.000, sample.Lambda, 3);
            Assert.Equal(1.00, sample.LambdaCoarse, 2);
            Assert.Equal(14.7, sample.Afr, 1);
            Assert.Equal(0x05, sample.OxygenSensorStatus);
        }

        [Fact]
        public void TryDecode_TruncatedBroadcast_IsRejected()
        {
            Assert.False(Zt3WidebandDecoder.TryDecode(new byte[7], out _));
        }
    }
}
