using System.Buffers.Binary;
using System.Globalization;

namespace LotusECMLogger.Services.A2l
{
    public enum A2lDataType { UByte, SByte, UWord, SWord, ULong, SLong, UInt64, Int64, Float32, Float64 }

    public enum A2lByteOrder { MsbFirst, MsbLast }

    public static class A2lDataTypes
    {
        public static int Size(this A2lDataType type) => type switch
        {
            A2lDataType.UByte or A2lDataType.SByte => 1,
            A2lDataType.UWord or A2lDataType.SWord => 2,
            A2lDataType.ULong or A2lDataType.SLong or A2lDataType.Float32 => 4,
            _ => 8,
        };

        public static bool IsFloat(this A2lDataType type) => type is A2lDataType.Float32 or A2lDataType.Float64;

        public static string Keyword(this A2lDataType type) => type switch
        {
            A2lDataType.UByte => "UBYTE", A2lDataType.SByte => "SBYTE", A2lDataType.UWord => "UWORD",
            A2lDataType.SWord => "SWORD", A2lDataType.ULong => "ULONG", A2lDataType.SLong => "SLONG",
            A2lDataType.UInt64 => "A_UINT64", A2lDataType.Int64 => "A_INT64",
            A2lDataType.Float32 => "FLOAT32_IEEE", _ => "FLOAT64_IEEE",
        };

        public static A2lDataType? Parse(string keyword) => keyword switch
        {
            "UBYTE" => A2lDataType.UByte, "SBYTE" => A2lDataType.SByte, "UWORD" => A2lDataType.UWord,
            "SWORD" => A2lDataType.SWord, "ULONG" => A2lDataType.ULong, "SLONG" => A2lDataType.SLong,
            "A_UINT64" => A2lDataType.UInt64, "A_INT64" => A2lDataType.Int64,
            "FLOAT32_IEEE" => A2lDataType.Float32, "FLOAT64_IEEE" => A2lDataType.Float64,
            _ => null,
        };

        public static A2lDataType FromSize(int size, bool signed) => (size, signed) switch
        {
            (1, false) => A2lDataType.UByte, (1, true) => A2lDataType.SByte,
            (2, false) => A2lDataType.UWord, (2, true) => A2lDataType.SWord,
            (4, false) => A2lDataType.ULong, (4, true) => A2lDataType.SLong,
            (8, false) => A2lDataType.UInt64, (8, true) => A2lDataType.Int64,
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };

        public static (double Min, double Max) RawRange(this A2lDataType type) => type switch
        {
            A2lDataType.UByte => (byte.MinValue, byte.MaxValue), A2lDataType.SByte => (sbyte.MinValue, sbyte.MaxValue),
            A2lDataType.UWord => (ushort.MinValue, ushort.MaxValue), A2lDataType.SWord => (short.MinValue, short.MaxValue),
            A2lDataType.ULong => (uint.MinValue, uint.MaxValue), A2lDataType.SLong => (int.MinValue, int.MaxValue),
            A2lDataType.UInt64 => (ulong.MinValue, ulong.MaxValue), A2lDataType.Int64 => (long.MinValue, long.MaxValue),
            A2lDataType.Float32 => (float.MinValue, float.MaxValue), _ => (double.MinValue, double.MaxValue),
        };

        public static double ReadRaw(this A2lDataType type, ReadOnlySpan<byte> bytes, A2lByteOrder order)
        {
            bool big = order == A2lByteOrder.MsbFirst;
            return type switch
            {
                A2lDataType.UByte => bytes[0],
                A2lDataType.SByte => (sbyte)bytes[0],
                A2lDataType.UWord => big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                A2lDataType.SWord => big ? BinaryPrimitives.ReadInt16BigEndian(bytes) : BinaryPrimitives.ReadInt16LittleEndian(bytes),
                A2lDataType.ULong => big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                A2lDataType.SLong => big ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadInt32LittleEndian(bytes),
                A2lDataType.UInt64 => big ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                A2lDataType.Int64 => big ? BinaryPrimitives.ReadInt64BigEndian(bytes) : BinaryPrimitives.ReadInt64LittleEndian(bytes),
                A2lDataType.Float32 => big ? BinaryPrimitives.ReadSingleBigEndian(bytes) : BinaryPrimitives.ReadSingleLittleEndian(bytes),
                _ => big ? BinaryPrimitives.ReadDoubleBigEndian(bytes) : BinaryPrimitives.ReadDoubleLittleEndian(bytes),
            };
        }

        /// <summary>A raw value's bytes, rounding integers to nearest and refusing values the type cannot hold.</summary>
        public static byte[] EncodeRaw(this A2lDataType type, double raw, A2lByteOrder order)
        {
            if (double.IsNaN(raw)) throw new ArgumentOutOfRangeException(nameof(raw), "NaN is not a raw value.");
            double value = type.IsFloat() ? raw : Math.Round(raw, MidpointRounding.AwayFromZero);
            if (!type.IsFloat())
            {
                var (min, max) = type.RawRange();
                if (value < min || value > max)
                    throw new ArgumentOutOfRangeException(nameof(raw),
                        string.Create(CultureInfo.InvariantCulture, $"{value} is outside {type.Keyword()} ({min}..{max})."));
            }
            bool big = order == A2lByteOrder.MsbFirst;
            var bytes = new byte[type.Size()];
            switch (type)
            {
                case A2lDataType.UByte: bytes[0] = (byte)value; break;
                case A2lDataType.SByte: bytes[0] = (byte)(sbyte)value; break;
                case A2lDataType.UWord: if (big) BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value); else BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value); break;
                case A2lDataType.SWord: if (big) BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value); else BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)value); break;
                case A2lDataType.ULong: if (big) BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value); else BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value); break;
                case A2lDataType.SLong: if (big) BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value); else BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)value); break;
                case A2lDataType.UInt64: if (big) BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)value); else BinaryPrimitives.WriteUInt64LittleEndian(bytes, (ulong)value); break;
                case A2lDataType.Int64: if (big) BinaryPrimitives.WriteInt64BigEndian(bytes, (long)value); else BinaryPrimitives.WriteInt64LittleEndian(bytes, (long)value); break;
                case A2lDataType.Float32: if (big) BinaryPrimitives.WriteSingleBigEndian(bytes, (float)value); else BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)value); break;
                default: if (big) BinaryPrimitives.WriteDoubleBigEndian(bytes, value); else BinaryPrimitives.WriteDoubleLittleEndian(bytes, value); break;
            }
            return bytes;
        }
    }

    public sealed record A2lMemorySegment(string Name, string PrgType, string MemoryType, uint Address, uint Size)
    {
        public bool Contains(uint address, int length) => length > 0 &&
            address >= Address && (ulong)address + (uint)length <= (ulong)Address + Size;
    }

    public enum A2lConversionKind { Identical, Linear, RationalFunction, VerbalTable, Formula, Unsupported }

    /// <summary>A COMPU_METHOD. Raw-to-physical always works when <see cref="Problem"/> is null; editing also
    /// needs <see cref="CanInvert"/>.</summary>
    public sealed class A2lCompuMethod
    {
        /// <summary>Stands in for ASAP2's NO_COMPU_METHOD and for a reference that names no COMPU_METHOD.</summary>
        public static A2lCompuMethod Identity { get; } = new() { Name = "NO_COMPU_METHOD", Kind = A2lConversionKind.Identical };

        public required string Name { get; init; }
        public string LongIdentifier { get; init; } = "";
        public required A2lConversionKind Kind { get; init; }
        public string Format { get; init; } = "";
        public string Unit { get; init; } = "";
        /// <summary>RAT_FUNC coefficients a..f: raw = (a·p² + b·p + c) / (d·p² + e·p + f). LINEAR is stored as a, b
        /// in p = a·raw + b.</summary>
        public double[] Coefficients { get; init; } = [];
        public IReadOnlyDictionary<long, string> VerbalTable { get; init; } = new Dictionary<long, string>();
        public string? DefaultText { get; init; }
        public A2lFormula? Formula { get; init; }
        public A2lFormula? InverseFormula { get; init; }
        /// <summary>Why values cannot be converted; such items are unavailable.</summary>
        public string? Problem { get; init; }

        public bool CanInvert => Problem is null && Kind switch
        {
            A2lConversionKind.Identical or A2lConversionKind.VerbalTable => true,
            A2lConversionKind.Linear => Coefficients[0] != 0,
            A2lConversionKind.RationalFunction => true, // RAT_FUNC is already physical → raw
            A2lConversionKind.Formula => InverseFormula is not null,
            _ => false,
        };

        public double ToPhysical(double raw)
        {
            if (Problem is not null) throw new InvalidOperationException($"{Name}: {Problem}");
            switch (Kind)
            {
                case A2lConversionKind.Identical:
                case A2lConversionKind.VerbalTable:
                    return raw;
                case A2lConversionKind.Linear:
                    return Coefficients[0] * raw + Coefficients[1];
                case A2lConversionKind.RationalFunction:
                {
                    // Quadratic forms (a or d non-zero) are refused when parsed, leaving raw = (b·p + c)/(e·p + f),
                    // so p = (f·raw − c)/(b − e·raw). (Written this way round, raw 0 gives +0, not −0.)
                    var (b, c, e, f) = (Coefficients[1], Coefficients[2], Coefficients[4], Coefficients[5]);
                    double denominator = b - e * raw;
                    return denominator == 0 ? double.NaN : (f * raw - c) / denominator;
                }
                case A2lConversionKind.Formula:
                    return Formula!.Evaluate([raw]);
                default:
                    throw new InvalidOperationException($"{Name}: unsupported conversion.");
            }
        }

        public double ToRaw(double physical)
        {
            if (!CanInvert) throw new InvalidOperationException($"{Name}: this conversion has no inverse, so values are read-only.");
            return Kind switch
            {
                A2lConversionKind.Identical or A2lConversionKind.VerbalTable => physical,
                A2lConversionKind.Linear => (physical - Coefficients[1]) / Coefficients[0],
                A2lConversionKind.RationalFunction => (Coefficients[1] * physical + Coefficients[2]) /
                    (Coefficients[4] * physical + Coefficients[5]),
                _ => InverseFormula!.Evaluate([physical]),
            };
        }

        /// <summary>The verbal text for a raw value, or null when this is not a verbal table.</summary>
        public string? ToText(double raw) => Kind != A2lConversionKind.VerbalTable ? null
            : VerbalTable.TryGetValue((long)Math.Round(raw), out var text) ? text : DefaultText;
    }

    public enum A2lIndexMode { RowDir, ColumnDir }

    /// <summary>The subset of RECORD_LAYOUT the app reads: one FNC_VALUES item, or one AXIS_PTS_X item, or (for
    /// STD_AXIS curves and maps) axis points followed by values, all with DIRECT addressing.</summary>
    public sealed class A2lRecordLayout
    {
        public required string Name { get; init; }
        public A2lLayoutItem? FncValues { get; init; }
        public A2lIndexMode IndexMode { get; init; } = A2lIndexMode.RowDir;
        public A2lLayoutItem? AxisPtsX { get; init; }
        public A2lLayoutItem? AxisPtsY { get; init; }
        public int? FixNoAxisPtsX { get; init; }
        public int? FixNoAxisPtsY { get; init; }
        public IReadOnlyDictionary<A2lDataType, int> Alignment { get; init; } = new Dictionary<A2lDataType, int>();
        public string? Problem { get; init; }
    }

    public sealed record A2lLayoutItem(int Position, A2lDataType Type);

    public enum A2lCharacteristicKind { Value, ValBlk, Curve, Map, Ascii }

    public enum A2lAxisKind { Common, Standard, Fixed }

    public sealed class A2lAxisDescr
    {
        public required A2lAxisKind Kind { get; init; }
        /// <summary>The MEASUREMENT driving this axis, or null for NO_INPUT_QUANTITY.</summary>
        public string? InputQuantity { get; init; }
        public required string Conversion { get; init; }
        public int MaxAxisPoints { get; init; }
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        public string? AxisPtsRef { get; init; }
        /// <summary>FIX_AXIS_PAR / FIX_AXIS_PAR_DIST, as raw axis values; <c>FIX_AXIS_PAR_LIST</c> gives them listed.</summary>
        public IReadOnlyList<double>? FixedRawPoints { get; init; }
        public bool ReadOnly { get; init; }
        public string? Problem { get; init; }
    }

    public sealed class A2lAxisPts
    {
        public required string Name { get; init; }
        public string LongIdentifier { get; init; } = "";
        public uint Address { get; init; }
        public string? InputQuantity { get; init; }
        public required string RecordLayout { get; init; }
        public required string Conversion { get; init; }
        public int MaxAxisPoints { get; init; }
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        public bool ReadOnly { get; init; }
        public A2lByteOrder? ByteOrder { get; init; }
        public string? Problem { get; init; }
    }

    public sealed class A2lCharacteristic
    {
        public required string Name { get; init; }
        public string LongIdentifier { get; init; } = "";
        public required A2lCharacteristicKind Kind { get; init; }
        public uint Address { get; init; }
        public required string RecordLayout { get; init; }
        public required string Conversion { get; init; }
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        /// <summary>Element count for VAL_BLK and ASCII (NUMBER or the product of MATRIX_DIM).</summary>
        public int Number { get; init; }
        public IReadOnlyList<A2lAxisDescr> Axes { get; init; } = [];
        public bool ReadOnly { get; init; }
        public A2lByteOrder? ByteOrder { get; init; }
        public string? Problem { get; init; }
    }

    public sealed class A2lMeasurement
    {
        public required string Name { get; init; }
        public string LongIdentifier { get; init; } = "";
        public required A2lDataType DataType { get; init; }
        public required string Conversion { get; init; }
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        public uint? Address { get; init; }
        /// <summary>For a VIRTUAL measurement, the measurements its formula reads as X1, X2, ….</summary>
        public IReadOnlyList<string> VirtualInputs { get; init; } = [];
        public ulong? BitMask { get; init; }
        public A2lByteOrder? ByteOrder { get; init; }
        public string? Problem { get; init; }

        public bool IsVirtual => VirtualInputs.Count > 0;
    }

    /// <summary>A GROUP: a named folder of calibration items and other groups; ROOT groups are top-level.</summary>
    public sealed record A2lGroup(string Name, string LongIdentifier, bool Root, IReadOnlyList<string> Characteristics,
        IReadOnlyList<string> SubGroups);

    /// <summary>A parser finding that does not stop the file loading.</summary>
    public sealed record A2lDiagnostic(int Line, string Message)
    {
        public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : Message;
    }

    /// <summary>One A2L MODULE with the items the app uses, plus everything the parser skipped or refused.</summary>
    public sealed class A2lModule
    {
        public required string Name { get; init; }
        public A2lByteOrder ByteOrder { get; init; } = A2lByteOrder.MsbFirst;
        public string? Epk { get; init; }
        public uint? EpkAddress { get; init; }
        public IReadOnlyList<A2lMemorySegment> MemorySegments { get; init; } = [];
        public IReadOnlyDictionary<string, A2lCharacteristic> Characteristics { get; init; } = new Dictionary<string, A2lCharacteristic>();
        public IReadOnlyDictionary<string, A2lAxisPts> AxisPts { get; init; } = new Dictionary<string, A2lAxisPts>();
        public IReadOnlyDictionary<string, A2lMeasurement> Measurements { get; init; } = new Dictionary<string, A2lMeasurement>();
        public IReadOnlyDictionary<string, A2lCompuMethod> CompuMethods { get; init; } = new Dictionary<string, A2lCompuMethod>();
        public IReadOnlyDictionary<string, A2lRecordLayout> RecordLayouts { get; init; } = new Dictionary<string, A2lRecordLayout>();
        public IReadOnlyDictionary<A2lDataType, int> Alignment { get; init; } = new Dictionary<A2lDataType, int>();
        /// <summary>GROUPs in file order.</summary>
        public IReadOnlyList<A2lGroup> Groups { get; init; } = [];
        public IReadOnlyList<A2lDiagnostic> Diagnostics { get; init; } = [];

        /// <summary>The conversion a name refers to; unknown names resolve to an unusable placeholder.</summary>
        public A2lCompuMethod Compu(string name) => name == "NO_COMPU_METHOD" ? A2lCompuMethod.Identity
            : CompuMethods.TryGetValue(name, out var method) ? method
            : new A2lCompuMethod { Name = name, Kind = A2lConversionKind.Unsupported, Problem = $"COMPU_METHOD {name} is not defined." };

        /// <summary>The calibration segment writes are limited to: DATA in RAM, else the first DATA segment.</summary>
        public A2lMemorySegment? CalibrationSegment =>
            MemorySegments.FirstOrDefault(s => s.PrgType == "DATA" && s.MemoryType == "RAM")
            ?? MemorySegments.FirstOrDefault(s => s.PrgType is "DATA" or "CALIBRATION_VARIABLES");
    }
}
