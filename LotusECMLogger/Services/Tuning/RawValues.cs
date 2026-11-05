using LotusECMLogger.Services.A2l;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// The latest raw values of ECU variables, by address: what the lookups compare with raw axis points. Live from a
    /// DAQ stream (the variable watch or a running recording), or from a saved recording at one moment of its replay.
    /// </summary>
    public interface IRawValueSource
    {
        /// <summary>
        /// The variable's <paramref name="size"/> bytes read big-endian as an unsigned number, or null while it has no
        /// value (not streamed, not sampled yet, or not recorded).
        /// </summary>
        ulong? Bits(uint address, int size);
    }

    public static class RawValues
    {
        /// <summary>Bytes read big-endian as an unsigned number.</summary>
        public static ulong Bits(ReadOnlySpan<byte> bytes)
        {
            ulong value = 0;
            foreach (byte b in bytes) value = value << 8 | b;
            return value;
        }

        /// <summary>The bits of a value <paramref name="size"/> bytes wide, two's complement when negative.</summary>
        public static ulong BitsOf(long raw, int size) => size >= 8 ? (ulong)raw : (ulong)raw & Mask(size);

        /// <summary>The raw number the bits hold: sign-extended when <paramref name="signed"/>.</summary>
        public static double ToRaw(ulong bits, int size, bool signed)
        {
            if (!signed || size >= 8) return signed ? (long)bits : bits;
            ulong sign = 1UL << (size * 8 - 1);
            return (bits & sign) != 0 ? (long)(bits | ~Mask(size)) : (long)bits;
        }

        private static ulong Mask(int size) => size >= 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;

        /// <summary>A measurement's raw value from <paramref name="source"/>, or null when it has none.</summary>
        public static double? Read(this IRawValueSource source, A2lMeasurement measurement)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(measurement);
            if (measurement.Address is not uint address) return null;
            int size = measurement.DataType.Size();
            return source.Bits(address, size) is ulong bits ? ToRaw(bits, size, measurement.DataType.IsSigned()) : null;
        }
    }
}
