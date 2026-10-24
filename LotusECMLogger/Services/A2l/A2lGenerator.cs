using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.A2l
{
    public sealed record A2lGenerationResult(string Text, IReadOnlyList<string> Warnings, int Values, int Blocks,
        int Curves, int Maps, int AxisPts, int Measurements, int Inputs);

    /// <summary>
    /// Drafts an A2L for one build from its symbol catalog (CCP-client-plan.md, "Before Phase 6"). Tables follow
    /// the catalog's naming: <c>CAL_x</c> is the data, <c>CAL_x_X_*</c> and <c>CAL_x_Y_*</c> its separately stored
    /// axes, X stored fastest (<c>lut[y * size_x + x]</c> in <c>lookup_3D_uint8_interpolated</c>), which is ASAP2
    /// <c>ROW_DIR</c>. Every axis gets <c>NO_INPUT_QUANTITY</c>: inputs need checking against the decompile, so the
    /// owner adds them by hand. The output is deterministic, so a regenerated file diffs cleanly against an edited one.
    /// </summary>
    public static class A2lGenerator
    {
        public const string Version = "1";

        private static readonly Regex NonIdentifier = new("[^A-Za-z0-9_]+", RegexOptions.Compiled);

        private sealed record Symbol(string Name, uint Address, SymbolEntry Entry)
        {
            public ParsedChannelType Type => Entry.Type;
            public int Count => Math.Max(1, Type.ArrayLength);
            public int Bytes => Count * Type.Size;
            public string BaseType => Regex.Replace(Entry.RawType, @"(\[\d+\])+$", "").Trim();
        }

        /// <param name="inputs">Reviewed axis inputs (<see cref="AxisInputs.Load"/>): AXIS_PTS name → the expression its
        /// lookup passes. A plain variable becomes the axis's InputQuantity; an expression becomes a VIRTUAL
        /// measurement whose FORM formula reads the variables' raw values as X1, X2, ….</param>
        public static A2lGenerationResult Generate(SymbolCatalog catalog, CcpBuildProfile build,
            IReadOnlyDictionary<string, string>? inputs = null)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(build);
            var warnings = new List<string>();
            var compu = new CompuMethods();

            var symbols = UniqueSymbols(catalog, warnings);
            var calibration = new List<Symbol>();
            var measurements = new List<Symbol>();
            foreach (var s in symbols)
            {
                bool cal = s.Name.StartsWith("CAL_", StringComparison.Ordinal);
                if (!cal)
                {
                    if (!build.ContainsCalibrationRange(s.Address, 1) && s.Type.IsScalar && s.Type.Size is 1 or 2 or 4)
                        measurements.Add(s);
                    continue;
                }
                if (s.Type.Size is not (1 or 2 or 4))
                    warnings.Add($"{s.Name}: type '{s.Entry.RawType}' has no known size; left out.");
                else if (s.Type.IsArray && s.Type.Confidence == TypeConfidence.Inferred)
                    warnings.Add($"{s.Name}: array of unknown type '{s.Entry.RawType}'; left out.");
                else if (!build.ContainsCalibrationRange(s.Address, s.Bytes))
                    warnings.Add($"{s.Name}: 0x{s.Address:X8}+{s.Bytes} is outside calibration RAM; left out.");
                else
                    calibration.Add(s);
            }

            // Pair each table with its axes. The longest prefix that is itself a symbol wins, so
            // CAL_a_X_b_X_c belongs to CAL_a_X_b when that exists.
            var byName = calibration.ToDictionary(s => s.Name, StringComparer.Ordinal);
            var axesOf = new Dictionary<string, (List<Symbol> X, List<Symbol> Y)>(StringComparer.Ordinal);
            var isAxis = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in calibration)
            {
                string? owner = null;
                char letter = ' ';
                foreach (Match m in Regex.Matches(s.Name, "_([XY])_"))
                {
                    string prefix = s.Name[..m.Index];
                    if (byName.ContainsKey(prefix)) (owner, letter) = (prefix, m.Groups[1].Value[0]);
                }
                if (owner is null) continue;
                isAxis.Add(s.Name);
                if (!axesOf.TryGetValue(owner, out var axes)) axesOf[owner] = axes = ([], []);
                (letter == 'X' ? axes.X : axes.Y).Add(s);
            }
            foreach (var name in isAxis)
                if (!byName[name].Type.IsArray) warnings.Add($"{name}: named like an axis but is a scalar.");

            var virtuals = new StringBuilder();
            var inputOf = BindInputs(inputs, isAxis, byName, measurements, virtuals, warnings);
            string Input(string axis) => inputOf.GetValueOrDefault(axis, "NO_INPUT_QUANTITY");

            var text = new StringBuilder();
            int values = 0, blocks = 0, curves = 0, maps = 0, axisCount = 0;
            var body = new StringBuilder();
            foreach (var s in calibration)
            {
                if (isAxis.Contains(s.Name) && s.Type.IsArray)
                {
                    WriteAxisPts(body, s, compu, Input(s.Name));
                    axisCount++;
                    continue;
                }
                if (axesOf.TryGetValue(s.Name, out var axes) && TableShape(s, axes, warnings) is { } shape)
                {
                    WriteTable(body, s, shape.X, shape.Y, compu, Input);
                    if (shape.Y is null) curves++; else maps++;
                    continue;
                }
                if (s.Type.IsArray) blocks++; else values++;
                WritePlain(body, s, compu);
            }
            foreach (var s in measurements) WriteMeasurement(body, s, compu);
            body.Append(virtuals);
            WriteGroups(body, calibration.Select(s => s.Name));

            text.Append("ASAP2_VERSION 1 61\n");
            text.Append("/*\n");
            text.Append(CultureInfo.InvariantCulture, $" * Drafted by LotusECMLogger A2lGenerator v{Version} from the {catalog.Name} symbol catalog for {build.Build}.\n");
            if (inputOf.Count == 0)
                text.Append(" * Every axis has NO_INPUT_QUANTITY: add an InputQuantity only after checking the lookup in the decompile\n");
            else
                text.Append(CultureInfo.InvariantCulture, $" * {inputOf.Count} axes have an InputQuantity from the reviewed <build>.inputs.csv; the rest have\n * NO_INPUT_QUANTITY. Bind more only after checking the lookup in the decompile\n");
            text.Append(" * (scope, conditions, input transforms; CCP-client-plan.md section 5). Inputs are raw values, as the lookups compare them.\n");
            text.Append(" * Maps are ROW_DIR: the first AXIS_DESCR is X, stored fastest, as the firmware reads lut[y * size_x + x].\n");
            if (warnings.Count > 0)
            {
                text.Append(" *\n * Generator warnings:\n");
                foreach (var w in warnings) text.Append(" *   ").Append(w.Replace("*/", "* /")).Append('\n');
            }
            text.Append(" */\n");
            text.Append(CultureInfo.InvariantCulture, $"/begin PROJECT {Identifier(build.Build)} \"Lotus T6 {build.Build}\"\n");
            text.Append(CultureInfo.InvariantCulture, $"  /begin HEADER \"\"\n    VERSION \"generated v{Version}\"\n  /end HEADER\n");
            text.Append(CultureInfo.InvariantCulture, $"  /begin MODULE {Identifier(build.Build)} \"\"\n");
            text.Append("    /begin MOD_COMMON \"\"\n      BYTE_ORDER MSB_FIRST\n      ALIGNMENT_BYTE 1\n      ALIGNMENT_WORD 2\n      ALIGNMENT_LONG 4\n    /end MOD_COMMON\n");
            text.Append("    /begin MOD_PAR \"\"\n");
            text.Append(CultureInfo.InvariantCulture, $"      EPK \"{build.Build}\"\n      ADDR_EPK 0x{build.CalibrationBase:X8}\n");
            text.Append(CultureInfo.InvariantCulture,
                $"      /begin MEMORY_SEGMENT CAL \"Calibration RAM\" DATA RAM INTERN 0x{build.CalibrationBase:X8} 0x{build.CalibrationSize:X} -1 -1 -1 -1 -1\n      /end MEMORY_SEGMENT\n");
            text.Append("    /end MOD_PAR\n");
            foreach (var type in Enum.GetValues<A2lDataType>().Where(t => t.Size() <= 4 && !t.IsFloat()))
            {
                text.Append(CultureInfo.InvariantCulture, $"    /begin RECORD_LAYOUT RL_VALUE_{type.Keyword()}\n      FNC_VALUES 1 {type.Keyword()} ROW_DIR DIRECT\n    /end RECORD_LAYOUT\n");
                text.Append(CultureInfo.InvariantCulture, $"    /begin RECORD_LAYOUT RL_AXIS_{type.Keyword()}\n      AXIS_PTS_X 1 {type.Keyword()} INDEX_INCR DIRECT\n    /end RECORD_LAYOUT\n");
            }
            compu.Write(text);
            text.Append(body);
            text.Append("  /end MODULE\n/end PROJECT\n");
            return new A2lGenerationResult(text.ToString(), warnings, values, blocks, curves, maps, axisCount, measurements.Count, inputOf.Count);
        }

        /// <summary>
        /// One ROOT GROUP per subsystem (<see cref="CalibrationGroups"/>), with a SUB_GROUP per name prefix when a
        /// subsystem spans several, so editors show a tree. Measurements are not grouped.
        /// </summary>
        private static void WriteGroups(StringBuilder text, IEnumerable<string> names)
        {
            var bySubsystem = names.GroupBy(CalibrationGroups.Classify).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var subsystem in CalibrationGroups.Subsystems.Where(bySubsystem.ContainsKey))
            {
                var byPrefix = bySubsystem[subsystem].GroupBy(CalibrationGroups.Prefix)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                string root = "G_" + subsystem.Name;
                text.Append(CultureInfo.InvariantCulture, $"    /begin GROUP {root} \"{subsystem.Title}\" ROOT\n");
                if (byPrefix.Count == 1) WriteReferences(text, byPrefix[0]);
                else
                {
                    text.Append("      /begin SUB_GROUP\n");
                    foreach (var prefix in byPrefix) text.Append(CultureInfo.InvariantCulture, $"        {root}_{Identifier(prefix.Key)}\n");
                    text.Append("      /end SUB_GROUP\n");
                }
                text.Append("    /end GROUP\n");
                if (byPrefix.Count == 1) continue;
                foreach (var prefix in byPrefix)
                {
                    text.Append(CultureInfo.InvariantCulture, $"    /begin GROUP {root}_{Identifier(prefix.Key)} \"{prefix.Key}\"\n");
                    WriteReferences(text, prefix);
                    text.Append("    /end GROUP\n");
                }
            }

            static void WriteReferences(StringBuilder text, IEnumerable<string> names)
            {
                text.Append("      /begin REF_CHARACTERISTIC\n");
                foreach (var name in names.Order(StringComparer.Ordinal)) text.Append("        ").Append(name).Append('\n');
                text.Append("      /end REF_CHARACTERISTIC\n");
            }
        }

        private static List<Symbol> UniqueSymbols(SymbolCatalog catalog, List<string> warnings)
        {
            var result = new List<Symbol>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.Entries.DistinctBy(e => (e.Name, e.Address)).OrderBy(e => e.Address).ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                string name = Identifier(entry.Name);
                if (!used.Add(name))
                {
                    string renamed = $"{name}_{entry.Address:X8}";
                    warnings.Add($"{entry.Name}: also at another address; this one (0x{entry.Address:X8}) is named {renamed}.");
                    name = renamed;
                    used.Add(name);
                }
                result.Add(new Symbol(name, entry.Address, entry));
            }
            return result;
        }

        private static (Symbol X, Symbol? Y)? TableShape(Symbol table, (List<Symbol> X, List<Symbol> Y) axes, List<string> warnings)
        {
            void Problem(string why) =>
                warnings.Add($"{table.Name}: {why}; written as a {(table.Type.IsArray ? "VAL_BLK" : "VALUE")} without axes.");
            var xs = axes.X.Where(a => a.Type.IsArray).ToList();
            var ys = axes.Y.Where(a => a.Type.IsArray).ToList();
            if (xs.Count == 0 && ys.Count == 0) return null;
            if (xs.Count != 1 || ys.Count > 1) { Problem($"{xs.Count} X and {ys.Count} Y axes"); return null; }
            var x = xs[0];
            var y = ys.FirstOrDefault();
            int cells = x.Count * (y?.Count ?? 1);
            if (cells != table.Count)
            {
                Problem(y is null ? $"{table.Count} values for a {x.Count}-point axis" : $"{table.Count} values for {x.Count} × {y.Count} axes");
                return null;
            }
            return (x, y);
        }

        private static void WriteAxisPts(StringBuilder text, Symbol s, CompuMethods compu, string input)
        {
            var (method, lower, upper) = Scaling(s, compu);
            text.Append(CultureInfo.InvariantCulture, $"    /begin AXIS_PTS {s.Name} \"{LongIdentifier(s)}\" 0x{s.Address:X8} {input} RL_AXIS_{DataType(s).Keyword()} 0 {method} {s.Count} {N(lower)} {N(upper)}\n");
            text.Append("    /end AXIS_PTS\n");
        }

        private static void WriteTable(StringBuilder text, Symbol s, Symbol x, Symbol? y, CompuMethods compu, Func<string, string> input)
        {
            var (method, lower, upper) = Scaling(s, compu);
            text.Append(CultureInfo.InvariantCulture, $"    /begin CHARACTERISTIC {s.Name} \"{LongIdentifier(s)}\" {(y is null ? "CURVE" : "MAP")} 0x{s.Address:X8} RL_VALUE_{DataType(s).Keyword()} 0 {method} {N(lower)} {N(upper)}\n");
            foreach (var axis in y is null ? new[] { x } : new[] { x, y })
            {
                var (axisMethod, axisLower, axisUpper) = Scaling(axis, compu);
                text.Append(CultureInfo.InvariantCulture, $"      /begin AXIS_DESCR COM_AXIS {input(axis.Name)} {axisMethod} {axis.Count} {N(axisLower)} {N(axisUpper)}\n");
                text.Append(CultureInfo.InvariantCulture, $"        AXIS_PTS_REF {axis.Name}\n      /end AXIS_DESCR\n");
            }
            text.Append("    /end CHARACTERISTIC\n");
        }

        /// <summary>
        /// Resolves reviewed axis inputs to InputQuantity names: a plain variable is its MEASUREMENT; an expression gets
        /// a VIRTUAL measurement <c>V_&lt;axis&gt;</c> with a FORM conversion over the variables' raw values. Inputs that
        /// name an unknown axis or variable, or do not parse, are reported and left unbound.
        /// </summary>
        private static Dictionary<string, string> BindInputs(IReadOnlyDictionary<string, string>? inputs, HashSet<string> isAxis,
            Dictionary<string, Symbol> byName, List<Symbol> measurements, StringBuilder virtuals, List<string> warnings)
        {
            var bound = new Dictionary<string, string>(StringComparer.Ordinal);
            if (inputs is null) return bound;
            var variables = measurements.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var (axis, expression) in inputs.OrderBy(i => i.Key, StringComparer.Ordinal))
            {
                if (!isAxis.Contains(axis) || !byName[axis].Type.IsArray)
                {
                    warnings.Add($"Input for {axis}: not an axis in this catalog; ignored.");
                    continue;
                }
                var names = Regex.Matches(expression, @"\b[A-Za-z_]\w*\b").Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
                var unknown = names.Where(n => !variables.Contains(n)).ToList();
                if (names.Count == 0 || unknown.Count > 0)
                {
                    warnings.Add($"Input for {axis}: '{expression}' {(names.Count == 0 ? "names no variable" : $"uses {string.Join(", ", unknown)}, not a measurement")}; left unbound.");
                    continue;
                }
                if (names.Count == 1 && expression.Trim() == names[0])
                {
                    bound[axis] = names[0];
                    continue;
                }
                string formula = expression;
                for (int i = 0; i < names.Count; i++)
                    formula = Regex.Replace(formula, $@"\b{Regex.Escape(names[i])}\b", $"X{i + 1}");
                if (!A2lFormula.TryParse(formula, out _, out var error))
                {
                    warnings.Add($"Input for {axis}: '{expression}' does not parse ({error}); left unbound.");
                    continue;
                }
                string name = "V_" + axis;
                bound[axis] = name;
                string described = expression.Replace("\"", "'");
                virtuals.Append(CultureInfo.InvariantCulture, $"    /begin MEASUREMENT {name} \"Input of {axis}: {described}\" SLONG CM_{name} 0 0 -2147483648 2147483647\n");
                virtuals.Append(CultureInfo.InvariantCulture, $"      /begin VIRTUAL {string.Join(" ", names)} /end VIRTUAL\n    /end MEASUREMENT\n");
                virtuals.Append(CultureInfo.InvariantCulture, $"    /begin COMPU_METHOD CM_{name} \"raw input of {axis}\" FORM \"%8.0\" \"\"\n");
                virtuals.Append(CultureInfo.InvariantCulture, $"      /begin FORMULA \"{formula}\" /end FORMULA\n    /end COMPU_METHOD\n");
            }
            return bound;
        }

        private static void WritePlain(StringBuilder text, Symbol s, CompuMethods compu)
        {
            bool ascii = s.BaseType == "char" && s.Type.IsArray;
            var (method, lower, upper) = ascii ? ("NO_COMPU_METHOD", 0.0, 255.0) : Scaling(s, compu);
            string kind = ascii ? "ASCII" : s.Type.IsArray ? "VAL_BLK" : "VALUE";
            text.Append(CultureInfo.InvariantCulture, $"    /begin CHARACTERISTIC {s.Name} \"{LongIdentifier(s)}\" {kind} 0x{s.Address:X8} RL_VALUE_{DataType(s).Keyword()} 0 {method} {N(lower)} {N(upper)}\n");
            if (s.Type.IsArray) text.Append(CultureInfo.InvariantCulture, $"      NUMBER {s.Count}\n");
            if (s.Type.Confidence == TypeConfidence.Inferred) text.Append("      READ_ONLY /* size inferred from the next symbol's address */\n");
            text.Append("    /end CHARACTERISTIC\n");
        }

        private static void WriteMeasurement(StringBuilder text, Symbol s, CompuMethods compu)
        {
            var (method, lower, upper) = Scaling(s, compu);
            text.Append(CultureInfo.InvariantCulture, $"    /begin MEASUREMENT {s.Name} \"{LongIdentifier(s)}\" {DataType(s).Keyword()} {method} 0 0 {N(lower)} {N(upper)}\n");
            text.Append(CultureInfo.InvariantCulture, $"      ECU_ADDRESS 0x{s.Address:X8}\n    /end MEASUREMENT\n");
        }

        private static A2lDataType DataType(Symbol s) => A2lDataTypes.FromSize(s.Type.Size, s.Type.Signed);

        /// <summary>The conversion and the physical limits of the full raw range.</summary>
        private static (string Method, double Lower, double Upper) Scaling(Symbol s, CompuMethods compu)
        {
            var (min, max) = DataType(s).RawRange();
            double a = s.Type.Scale * min + s.Type.Offset, b = s.Type.Scale * max + s.Type.Offset;
            return (compu.For(s), Math.Min(a, b), Math.Max(a, b));
        }

        private static string LongIdentifier(Symbol s)
        {
            string text = string.IsNullOrWhiteSpace(s.Entry.Comment) ? s.Entry.RawType : s.Entry.Comment;
            // The catalog exports escape commas inside comments as "\,".
            text = Regex.Replace(text.Replace("\\,", ","), @"\s+", " ").Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
            return text.Length > 250 ? text[..250] : text;
        }

        internal static string Identifier(string name)
        {
            string id = NonIdentifier.Replace(name, "_");
            return id.Length > 0 && char.IsDigit(id[0]) ? "_" + id : id;
        }

        private static string N(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

        /// <summary>One COMPU_METHOD per distinct (scale, offset, unit), named after the first catalog type using it.</summary>
        private sealed class CompuMethods
        {
            private readonly Dictionary<(double, double, string), string> _byScaling = [];
            private readonly SortedDictionary<string, (double Scale, double Offset, string Unit, string Type)> _methods = new(StringComparer.Ordinal);

            public string For(Symbol s)
            {
                var t = s.Type;
                string unit = t.Unit == "raw" ? "" : t.Unit;
                if (t.Scale == 1 && t.Offset == 0 && unit.Length == 0) return "NO_COMPU_METHOD";
                var key = (t.Scale, t.Offset, unit);
                if (_byScaling.TryGetValue(key, out var name)) return name;
                name = "CM_" + Identifier(s.BaseType).Trim('_');
                for (int i = 2; _methods.ContainsKey(name); i++) name = $"CM_{Identifier(s.BaseType).Trim('_')}_{i}";
                _byScaling[key] = name;
                _methods[name] = (t.Scale, t.Offset, unit, s.BaseType);
                return name;
            }

            public void Write(StringBuilder text)
            {
                foreach (var (name, (scale, offset, unit, type)) in _methods)
                {
                    // RAT_FUNC is physical → raw: raw = (0·p² + 1·p − offset) / (0·p² + 0·p + scale).
                    text.Append(CultureInfo.InvariantCulture, $"    /begin COMPU_METHOD {name} \"{type.Replace("\"", "\\\"")}\" RAT_FUNC \"{Format(scale, offset)}\" \"{unit.Replace("\"", "\\\"")}\"\n");
                    text.Append(CultureInfo.InvariantCulture, $"      COEFFS 0 1 {N(offset == 0 ? 0 : -offset)} 0 0 {N(scale)}\n    /end COMPU_METHOD\n");
                }
            }

            // Enough decimals to show one raw step exactly where it terminates, capped at four.
            private static string Format(double scale, double offset)
            {
                int decimals = 4;
                for (int d = 0; d <= 4; d++)
                {
                    double p = Math.Pow(10, d);
                    if (Math.Abs(scale * p - Math.Round(scale * p)) < 1e-9 && Math.Abs(offset * p - Math.Round(offset * p)) < 1e-9)
                    {
                        decimals = d;
                        break;
                    }
                }
                return string.Create(CultureInfo.InvariantCulture, $"%{8 + decimals}.{decimals}");
            }
        }
    }
}
