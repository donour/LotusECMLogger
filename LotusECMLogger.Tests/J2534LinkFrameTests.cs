using LotusECMLogger.Services.Broker;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Pins the bytes the broker's channels hand to the adapter against the literal messages the
    /// coding write and the VIN write built before the link interfaces existed (commit 1492a2a), so
    /// moving them onto the links and then the broker cannot have changed what reaches the car.
    /// </summary>
    public sealed class J2534LinkFrameTests
    {
        [Fact]
        public void CanFrame_MatchesTheOldCodingWriteMessage()
        {
            byte[] high = [0x11, 0x22, 0x33, 0x44];
            byte[] low = [0x55, 0x66, 0x77, 0x88];

            for (byte arbIdLow = 0x00; arbIdLow <= 0x05; arbIdLow++)
            {
                // The old WriteRawCanCoding: 00 00 05 <id low>, then the high word, then the low word.
                byte[] old = new byte[12];
                old[2] = 0x05;
                old[3] = arbIdLow;
                Array.Copy(high, 0, old, 4, 4);
                Array.Copy(low, 0, old, 8, 4);

                Assert.Equal(old, SharedCanChannel.Frame(0x500u + arbIdLow, [.. high, .. low]));
            }
        }

        [Fact]
        public void CanFrame_RejectsExtendedIdsAndLongData()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SharedCanChannel.Frame(0x800, [0x00]));
            Assert.Throws<ArgumentOutOfRangeException>(() => SharedCanChannel.Frame(0x500, new byte[9]));
        }

        [Fact]
        public void DiagnosticFrame_MatchesTheOldVinChunkMessage()
        {
            // The old SendVinChunk: the ECM header 00 00 07 E0, 0x3B, the sub-function, the VIN bytes.
            byte[] old = [0x00, 0x00, 0x07, 0xE0, 0x3B, 0x01, (byte)'L', (byte)'M', (byte)'D', (byte)'T'];

            Assert.Equal(old, SharedIsoChannel.Frame(ECUDefinition.ECM, [0x3B, 0x01, .. "LMDT"u8]));
        }
    }
}
