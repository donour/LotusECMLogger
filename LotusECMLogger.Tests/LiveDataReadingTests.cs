namespace LotusECMLogger.Tests
{
    public sealed class LiveDataReadingTests
    {
        private static byte[] Mode01Response(params byte[] payload) =>
            [0x00, 0x00, 0x07, 0xE8, 0x41, .. payload];

        [Fact]
        public void ParseMode01_UnknownPidDoesNotDecodeItsPayloadAsAnotherPid()
        {
            // PID 0x21 is not supported by the decoder. Its first payload byte looks like
            // PID 0x0C, followed by bytes that would otherwise produce a plausible RPM.
            var readings = LiveDataReading.ParseCanResponse(
                Mode01Response(0x21, 0x0C, 0x1A, 0xF8));

            Assert.Empty(readings);
        }

        [Fact]
        public void ParseMode01_UnknownPidStopsFrameAfterKeepingEarlierReadings()
        {
            // The bytes after unknown PID 0x21 look like a vehicle-speed response. Because
            // the unknown PID's width cannot be inferred, they must not be decoded.
            var readings = LiveDataReading.ParseCanResponse(
                Mode01Response(0x05, 0x7B, 0x21, 0x0D, 0x64));

            var reading = Assert.Single(readings);
            Assert.Equal("Coolant Temperature", reading.name);
            Assert.Equal(83, reading.value_f);
        }
    }
}
