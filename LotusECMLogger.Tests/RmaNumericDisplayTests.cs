using LotusECMLogger.Controls;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the numeric interpretations shown on the T6 RMA tab. ECU RAM is big-endian and the
    /// RMA read returns it byte-for-byte, so the values must be decoded most significant byte
    /// first to match what the ECU itself reads at that address.
    /// </summary>
    public sealed class RmaNumericDisplayTests
    {
        private static string[] Lines(params byte[] data) =>
            T6RMAControl.FormatNumericInterpretations(data)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        [Fact]
        public void DecodesIntegersBigEndian()
        {
            // Little-endian decoding would show 13330 and 2018915346.
            var lines = Lines(0x12, 0x34, 0x56, 0x78);

            Assert.Contains("  Byte (unsigned): 18", lines);
            Assert.Contains("  Int16 (BE): 4660", lines);        // 0x1234
            Assert.Contains("  Int32 (BE): 305419896", lines);   // 0x12345678
        }

        [Fact]
        public void DecodesSignedValuesBigEndian()
        {
            var lines = Lines(0xFF, 0xFE, 0xFF, 0xFE);

            Assert.Contains("  Int16 (BE): -2", lines);          // 0xFFFE
            Assert.Contains("  Int32 (BE): -65538", lines);      // 0xFFFEFFFE
        }

        [Fact]
        public void DecodesFloatBigEndian()
        {
            // 0x3F800000 is 1.0f; read little-endian it would be a denormal near 4.6e-41.
            var lines = Lines(0x3F, 0x80, 0x00, 0x00);

            Assert.Contains($"  Float (BE): {1.0f:F6}", lines);
        }

        [Fact]
        public void ShortReads_ShowOnlyTheValuesThatFit()
        {
            Assert.Equal(["Numeric Interpretations:", "  Byte (unsigned): 18"], Lines(0x12));
            Assert.Equal(
                ["Numeric Interpretations:", "  Byte (unsigned): 18", "  Int16 (BE): 4660"],
                Lines(0x12, 0x34, 0x56));
        }
    }
}
