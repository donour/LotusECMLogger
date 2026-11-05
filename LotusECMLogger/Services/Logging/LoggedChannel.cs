using System.Globalization;
using System.Text.RegularExpressions;
using LotusECMLogger.Models;

namespace LotusECMLogger.Services.Logging
{
    /// <summary>
    /// One channel of a high-speed log as its CSV preamble describes it, so the log can be read back exactly: the
    /// column holds raw × <see cref="Scale"/> + <see cref="Offset"/> of the <see cref="Size"/> bytes at
    /// <see cref="Address"/>. Written as
    /// <c># channel: coolant_temp @0x40001836 size=1 signed=0 scale=0.625 offset=-40 rate=20Hz unit=°C</c>.
    /// Logs from before this line was added note only <c>name: 0xADDRESS size=N RATEHz unit=U</c>; for those the
    /// signedness, scale and offset are unknown (null).
    /// </summary>
    public sealed partial record LoggedChannel(string Name, uint Address, int Size, bool? Signed, double? Scale, double? Offset,
        int RateHz, string Unit)
    {
        private const string Prefix = "channel: ";

        [GeneratedRegex(@"^channel: (?<name>.+) @0x(?<address>[0-9A-Fa-f]{1,8}) size=(?<size>\d+) signed=(?<signed>[01]) " +
            @"scale=(?<scale>\S+) offset=(?<offset>\S+) rate=(?<rate>\d+)Hz unit=(?<unit>.*)$")]
        private static partial Regex Current();

        [GeneratedRegex(@"^(?<name>.+): 0x(?<address>[0-9A-Fa-f]{1,8}) size=(?<size>\d+) (?<rate>\d+)Hz unit=(?<unit>.*)$")]
        private static partial Regex Legacy();

        public static LoggedChannel From(HighSpeedChannel channel, int rateHz)
        {
            ArgumentNullException.ThrowIfNull(channel);
            return new(channel.Name, channel.Address, channel.Size, channel.Signed, channel.Scale, channel.Offset, rateHz, channel.Unit);
        }

        /// <summary>Whether a column value can be turned back into the exact raw value.</summary>
        public bool IsExact => Signed is not null && Scale is { } scale && scale != 0 && double.IsFinite(scale) && Offset is not null;

        public string ToNote() => string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{Name} @0x{Address:X8} size={Size} signed={(Signed == true ? 1 : 0)} scale={Scale ?? 1:R} offset={Offset ?? 0:R} " +
            $"rate={RateHz}Hz unit={Unit}");

        /// <summary>The channel a preamble line (without its "# ") describes, or null when it describes none.</summary>
        public static LoggedChannel? Parse(string note)
        {
            ArgumentNullException.ThrowIfNull(note);
            if (Current().Match(note) is { Success: true } m)
            {
                return TryNumber(m.Groups["scale"].Value, out double scale) && TryNumber(m.Groups["offset"].Value, out double offset)
                    ? new(m.Groups["name"].Value, Address(m), Size(m), m.Groups["signed"].Value == "1", scale, offset, Rate(m), m.Groups["unit"].Value)
                    : null;
            }
            if (!note.StartsWith(Prefix, StringComparison.Ordinal) && Legacy().Match(note) is { Success: true } legacy)
                return new(legacy.Groups["name"].Value, Address(legacy), Size(legacy), null, null, null, Rate(legacy), legacy.Groups["unit"].Value);
            return null;

            static uint Address(Match m) => uint.Parse(m.Groups["address"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            static int Size(Match m) => int.Parse(m.Groups["size"].Value, CultureInfo.InvariantCulture);
            static int Rate(Match m) => int.Parse(m.Groups["rate"].Value, CultureInfo.InvariantCulture);
            static bool TryNumber(string text, out double value) =>
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// The raw bits behind a column value (its <see cref="Size"/> bytes, unsigned, two's complement when negative),
        /// or null when the value is not one this channel can hold. Needs <see cref="IsExact"/>.
        /// </summary>
        public ulong? BitsOf(double value)
        {
            if (!IsExact) throw new InvalidOperationException($"{Name}: the log does not say how its values were scaled.");
            if (!double.IsFinite(value)) return null;
            double raw = Math.Round((value - Offset!.Value) / Scale!.Value);
            int bits = Size * 8;
            double min = Signed == true ? -Math.Pow(2, bits - 1) : 0, max = Signed == true ? Math.Pow(2, bits - 1) - 1 : Math.Pow(2, bits) - 1;
            if (raw < min || raw > max) return null;
            long whole = (long)raw;
            return Size >= 8 ? (ulong)whole : (ulong)whole & ((1UL << bits) - 1);
        }
    }
}
