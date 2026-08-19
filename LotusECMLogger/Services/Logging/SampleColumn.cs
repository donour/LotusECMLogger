using System.Globalization;

namespace LotusECMLogger.Services.Logging
{
    /// <summary>How a column's cells are rendered.</summary>
    public enum SampleFormat
    {
        /// <summary>Shortest round-trippable decimal. The default, and right for measured quantities.</summary>
        Number,

        /// <summary>
        /// Zero-padded, <c>0x</c>-prefixed hexadecimal. For raw values whose bit pattern is the
        /// point — memory bytes, registers, bitfields — where a decimal rendering has to be
        /// converted back by hand before it means anything.
        /// </summary>
        Hex,

        /// <summary>Arbitrary text, escaped if it contains a comma or a quote.</summary>
        Text,
    }

    /// <summary>
    /// One column of a sample log: its name and how its values are written. A plain string converts
    /// implicitly to a <see cref="Number"/> column, so the common case stays terse.
    /// </summary>
    public sealed class SampleColumn
    {
        public string Name { get; }
        public SampleFormat Format { get; }

        /// <summary>Hex digits each value is padded to; 0 for non-hex columns.</summary>
        public int HexDigits { get; }

        /// <summary>Precomputed so rendering a hex cell does not build a format string per row.</summary>
        internal string HexFormat { get; }

        private SampleColumn(string name, SampleFormat format, int hexDigits)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            Name = name;
            Format = format;
            HexDigits = hexDigits;
            HexFormat = "X" + hexDigits.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A decimal column.</summary>
        public static SampleColumn Number(string name) => new(name, SampleFormat.Number, 0);

        /// <summary>
        /// A hexadecimal column. <paramref name="digits"/> is the width each value is padded to —
        /// 2 for a byte, 4 for a word, 8 for a dword.
        /// </summary>
        public static SampleColumn Hex(string name, int digits = 2)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(digits, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 16);
            return new SampleColumn(name, SampleFormat.Hex, digits);
        }

        /// <summary>A text column, written with <see cref="ISampleSink.SetText"/>.</summary>
        public static SampleColumn Text(string name) => new(name, SampleFormat.Text, 0);

        public static implicit operator SampleColumn(string name) => Number(name);

        public override string ToString() => Name;
    }
}
