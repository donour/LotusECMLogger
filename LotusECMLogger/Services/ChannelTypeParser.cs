using System.Globalization;
using System.Text.RegularExpressions;

namespace LotusECMLogger.Services
{
    /// <summary>How much of a channel's metadata we were able to derive from its type string.</summary>
    public enum TypeConfidence
    {
        /// <summary>Size, scale, offset and unit parsed from an encoded type (e.g. <c>u8_temp_5/8-40c</c>).</summary>
        Derived,
        /// <summary>Size and signedness from a C base type (<c>uint16_t</c>, <c>int8_t</c>, …); raw scale.</summary>
        BaseType,
        /// <summary>Size inferred from the gap to the next symbol address; raw scale.</summary>
        Inferred,
        /// <summary>Unknown type; size from a numeric prefix if any, otherwise to be inferred; raw scale.</summary>
        Raw,
    }

    /// <summary>The channel metadata derived from a Ghidra "Data Type" string.</summary>
    public sealed record ParsedChannelType
    {
        /// <summary>Width in bytes (1/2/4). 0 means "unknown — infer from the address gap".</summary>
        public int Size { get; init; }
        public bool Signed { get; init; }
        public double Scale { get; init; } = 1.0;
        public double Offset { get; init; }
        public string Unit { get; init; } = "";
        /// <summary>Element count for array/table types; 0 for scalars.</summary>
        public int ArrayLength { get; init; }
        /// <summary>Quantity/kind hint (e.g. <c>temp</c>, <c>rspeed</c>, <c>enum</c>, <c>pointer</c>).</summary>
        public string Category { get; init; } = "";
        public TypeConfidence Confidence { get; init; }

        public bool IsArray => ArrayLength > 0;
        public bool IsScalar => ArrayLength == 0;
    }

    /// <summary>
    /// Parses Ghidra "Data Type" strings from the ECU symbol exports into channel metadata
    /// (<see cref="ParsedChannelType"/>). Pure and total — never throws; unrecognized types fall back
    /// to a raw descriptor. See the type-vocabulary analysis in the project plan.
    /// </summary>
    public static class ChannelTypeParser
    {
        // Quantities whose encoded scale/offset/unit we trust enough to mark "Derived".
        // Derived from the actual type vocabulary across both ECU symbol exports (excludes "obd2level",
        // whose encoding we don't know, so it stays raw).
        private static readonly HashSet<string> KnownQuantities = new(StringComparer.Ordinal)
        {
            "rspeed", "temp", "voltage", "torque", "pressure", "angle", "speed", "time",
            "flow", "load", "factor", "count", "gear", "current", "mass", "power", "ratio",
            "level", "distance", "accel", "frequency",
            "afr", "volume", "slip", "dutycycle", "percent", "gain", "dt",
        };

        // Default unit when an encoded type omits one (the scale/offset still come from the spec).
        private static readonly Dictionary<string, string> DefaultUnits = new(StringComparer.Ordinal)
        {
            ["angle"] = "deg", ["rspeed"] = "rpm", ["temp"] = "°C", ["voltage"] = "V",
            ["torque"] = "Nm", ["pressure"] = "mbar", ["speed"] = "kph",
            ["afr"] = "AFR", ["dutycycle"] = "%", ["percent"] = "%", ["slip"] = "%",
        };

        // Light prettifying of the raw unit token for display.
        private static readonly Dictionary<string, string> PrettyUnits = new(StringComparer.Ordinal)
        {
            ["c"] = "°C", ["v"] = "V", ["nm"] = "Nm", ["kph"] = "km/h", ["pct"] = "%",
        };

        private static readonly Regex EncodedRe =
            new(@"^(?<sz>[ui])(?<bits>8|16|32)_(?<qty>[a-z0-9]+)(?:_(?<spec>.+))?$", RegexOptions.Compiled);

        private static readonly Regex SpecRe =
            new(@"^(?<num>\d+)?(?:/(?<den>\d+))?(?<off>[+-]\d+)?(?<unit>.*)$", RegexOptions.Compiled);

        public static ParsedChannelType Parse(string? rawType)
        {
            string t = (rawType ?? string.Empty).Trim();

            int arrayLen = 0;
            var arr = Regex.Match(t, @"\[(\d+)\]$");
            if (arr.Success)
            {
                arrayLen = int.Parse(arr.Groups[1].Value, CultureInfo.InvariantCulture);
                t = t[..arr.Index].Trim();
            }

            ParsedChannelType result = ParseBase(t);
            return arrayLen > 0 ? result with { ArrayLength = arrayLen } : result;
        }

        private static ParsedChannelType ParseBase(string t)
        {
            if (t.EndsWith("*", StringComparison.Ordinal) || t.Equals("pointer", StringComparison.OrdinalIgnoreCase))
                return new ParsedChannelType { Size = 4, Unit = "raw", Category = "pointer", Confidence = TypeConfidence.Raw };

            switch (t)
            {
                case "uint8_t": case "undefined1": case "bool": case "byte": case "char":
                    return new ParsedChannelType { Size = 1, Unit = "raw", Confidence = TypeConfidence.BaseType };
                case "uint16_t": case "undefined2":
                    return new ParsedChannelType { Size = 2, Unit = "raw", Confidence = TypeConfidence.BaseType };
                case "uint32_t": case "undefined4": case "undefined":
                    return new ParsedChannelType { Size = 4, Unit = "raw", Confidence = TypeConfidence.BaseType };
                case "int8_t":
                    return new ParsedChannelType { Size = 1, Signed = true, Unit = "raw", Confidence = TypeConfidence.BaseType };
                case "int16_t":
                    return new ParsedChannelType { Size = 2, Signed = true, Unit = "raw", Confidence = TypeConfidence.BaseType };
                case "int32_t":
                    return new ParsedChannelType { Size = 4, Signed = true, Unit = "raw", Confidence = TypeConfidence.BaseType };
            }

            if (t.StartsWith("enum_", StringComparison.Ordinal))
                return new ParsedChannelType { Size = 1, Category = "enum", Confidence = TypeConfidence.BaseType };

            var enc = EncodedRe.Match(t);
            if (enc.Success)
                return ParseEncoded(enc);

            // Unknown named type — size to be inferred from the address gap by the catalog.
            return new ParsedChannelType { Size = 0, Unit = "raw", Category = "unknown", Confidence = TypeConfidence.Raw };
        }

        private static ParsedChannelType ParseEncoded(Match enc)
        {
            int size = int.Parse(enc.Groups["bits"].Value, CultureInfo.InvariantCulture) / 8;
            bool signed = enc.Groups["sz"].Value == "i";
            string qty = enc.Groups["qty"].Value;
            string spec = enc.Groups["spec"].Success ? enc.Groups["spec"].Value : string.Empty;

            // Only claim a calibrated scale/unit for quantities we recognize; others stay raw.
            if (!KnownQuantities.Contains(qty))
                return new ParsedChannelType
                {
                    Size = size,
                    Signed = signed,
                    Unit = "raw",
                    Category = qty,
                    Confidence = TypeConfidence.Raw,
                };

            var m = SpecRe.Match(spec);
            double num = m.Groups["num"].Success ? double.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture) : 1.0;
            double den = m.Groups["den"].Success ? double.Parse(m.Groups["den"].Value, CultureInfo.InvariantCulture) : 1.0;
            double offset = m.Groups["off"].Success ? double.Parse(m.Groups["off"].Value, CultureInfo.InvariantCulture) : 0.0;
            string unit = m.Groups["unit"].Value.Trim();
            double scale = den != 0 ? num / den : num;

            // "factor" is a normalized 0..1 fraction whose denominator is the full-scale count
            // (e.g. u16_factor_1/1023). Express it as a percentage so full scale reads 100%, unless the
            // type names its own unit (e.g. i16_factor_1/10pct is already scaled to percent).
            if (qty == "factor" && unit.Length == 0)
            {
                scale *= 100.0;
                unit = "%";
            }

            if (unit.Length == 0)
                DefaultUnits.TryGetValue(qty, out unit!);
            unit ??= string.Empty;
            if (PrettyUnits.TryGetValue(unit, out var pretty))
                unit = pretty;

            return new ParsedChannelType
            {
                Size = size,
                Signed = signed,
                Scale = scale,
                Offset = offset,
                Unit = unit,
                Category = qty,
                Confidence = TypeConfidence.Derived,
            };
        }
    }
}
