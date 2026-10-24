using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LotusECMLogger.Services.A2l
{
    /// <summary>
    /// One axis's input as the ECU's lookup sees it: an expression over RAM variables, already truncated the way the
    /// lookup variant truncates its argument (e.g. <c>(engine_speed_16bit) &amp; 0xff</c> for an 8-bit lookup).
    /// </summary>
    public sealed record AxisInput(string Axis, string Expression, string Lookup, string Lines);

    /// <summary>A lookup call whose input could not be bound, with the reason, for the extraction report.</summary>
    public sealed record SkippedAxisInput(string Axis, string Reason);

    /// <summary>
    /// Drafts axis input bindings from a Ghidra decompile (Phase 6e). Every <c>lookup_*</c> call names its axis
    /// arrays, so each binding belongs to an AXIS_PTS by name. A binding is kept only when every call site gives
    /// the same input and that input uses nothing but the catalog's RAM variables, numbers and simple operators:
    /// locals, array or struct accesses, calls, conditionals and offset axis pointers are left unbound. The result is a
    /// reviewable CSV (<c>config/a2l/&lt;build&gt;.inputs.csv</c>) that the A2L generator turns into InputQuantity.
    /// </summary>
    public static class AxisInputs
    {
        /// <summary>Per lookup variant: input argument positions, axis argument positions, and the input mask.</summary>
        private static readonly Dictionary<string, (int[] Inputs, int[] Axes, long? Mask)> Signatures = new(StringComparer.Ordinal)
        {
            // uint8_t lookup_2D_uint8_interpolated(uint8_t size_x, uint8_t input_x, uint8_t *lut, uint8_t *x_axis)
            ["lookup_2D_uint8_interpolated"] = ([1], [3], 0xFF),
            // uint16_t lookup_2D_uint16_interpolated(uint8_t size_x, uint16_t input_x, uint16_t *lut, uint16_t *x_axis)
            ["lookup_2D_uint16_interpolated"] = ([1], [3], 0xFFFF),
            // uint8_t lookup_3D_uint8_interpolated(size_x, size_y, uint16_t input_x, uint16_t input_y, lut, x_axis, y_axis);
            // the body uses input & 0xff
            ["lookup_3D_uint8_interpolated"] = ([2, 3], [5, 6], 0xFF),
            // uint8_t lookup_3D_uint8(size_x, size_y, byte input_x, byte input_y, byte *x_axis, byte *y_axis, byte *lut)
            ["lookup_3D_uint8"] = ([2, 3], [4, 5], 0xFF),
            // uint lookup_3D_uint32_interpolated(size_x, size_y, uint input_x, uint input_y, ushort *lut, ushort *x_axis, ushort *y_axis)
            ["lookup_3D_uint32_interpolated"] = ([2, 3], [5, 6], null),
        };

        private static readonly Regex Call = new(@"\b(lookup_\w+)\s*\(", RegexOptions.Compiled);
        private static readonly Regex Cast = new(@"\(\s*(?:u?int\d+_t|ushort|uint|uchar|byte|short|char|int|long|ulong|longlong|ulonglong|bool|undefined\d?)\s*\*?\s*\)", RegexOptions.Compiled);
        private static readonly Regex Token = new(@"\s*(0x[0-9A-Fa-f]+|\d+|[A-Za-z_]\w*|>>|<<|[&|^+\-*/~()])", RegexOptions.Compiled);
        private static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

        /// <summary>The RAM variables an input may use: catalog names that are unique, scalar, 1/2/4 bytes and not calibration.</summary>
        public static Dictionary<string, SymbolEntry> Variables(SymbolCatalog catalog)
        {
            var byName = catalog.Entries.GroupBy(e => e.Name, StringComparer.Ordinal)
                .Where(g => g.Select(e => e.Address).Distinct().Count() == 1).Select(g => g.First());
            return byName.Where(e => e.Type.IsScalar && e.Type.Size is 1 or 2 or 4 &&
                    !e.Name.StartsWith("CAL_", StringComparison.Ordinal))
                .ToDictionary(e => e.Name, StringComparer.Ordinal);
        }

        public static (IReadOnlyList<AxisInput> Bound, IReadOnlyList<SkippedAxisInput> Skipped) Extract(string decompile,
            IReadOnlySet<string> axes, IReadOnlyDictionary<string, SymbolEntry> variables)
        {
            var found = new Dictionary<string, List<(string Expression, string Lookup, int Line)>>(StringComparer.Ordinal);
            var skipped = new List<SkippedAxisInput>();
            var lineStarts = LineStarts(decompile);
            foreach (Match m in Call.Matches(decompile))
            {
                if (!Signatures.TryGetValue(m.Groups[1].Value, out var signature)) continue;
                int lineStart = decompile.LastIndexOf('\n', m.Index) + 1;
                if (lineStart < decompile.Length && !char.IsWhiteSpace(decompile[lineStart])) continue; // a definition
                if (Arguments(decompile, m.Index + m.Length) is not { } args) continue;
                int line = LineOf(lineStarts, m.Index);
                for (int k = 0; k < signature.Inputs.Length; k++)
                {
                    if (signature.Axes[k] >= args.Count || signature.Inputs[k] >= args.Count) continue;
                    string axisArg = Cast.Replace(args[signature.Axes[k]], "").Trim().TrimStart('&').Trim();
                    if (!axes.Contains(axisArg)) continue; // offset pointers, DAT_ addresses, unknown axes
                    if (!found.TryGetValue(axisArg, out var list)) found[axisArg] = list = [];
                    string? expression = Normalize(args[signature.Inputs[k]], signature.Mask, variables, out string? why);
                    list.Add((expression ?? "?" + why, m.Groups[1].Value, line));
                }
            }

            var bound = new List<AxisInput>();
            foreach (var (axis, uses) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var distinct = uses.Select(u => u.Expression).Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count > 1)
                    skipped.Add(new(axis, $"{distinct.Count} different inputs: {string.Join(" | ", distinct.Select(d => d.TrimStart('?')))}"));
                else if (distinct[0].StartsWith('?'))
                    skipped.Add(new(axis, distinct[0][1..]));
                else
                    bound.Add(new(axis, distinct[0], string.Join(" ", uses.Select(u => u.Lookup).Distinct()),
                        string.Join(" ", uses.Select(u => u.Line.ToString(CultureInfo.InvariantCulture)))));
            }
            return (bound, skipped);
        }

        /// <summary>
        /// The input as the lookup sees it, or null (with the reason) when it is not a plain expression over RAM
        /// variables. Casts are dropped; the lookup's own truncation is added unless the variable already fits.
        /// </summary>
        public static string? Normalize(string argument, long? mask, IReadOnlyDictionary<string, SymbolEntry> variables, out string? reason)
        {
            reason = null;
            // Casts are dropped, which is only safe when the lookup truncates at least as narrowly as the cast.
            int lookupBits = mask is { } m0 ? BitWidth(m0) : 32;
            foreach (Match cast in Cast.Matches(argument))
            {
                string type = cast.Value.Trim('(', ')', ' ', '*');
                int castBits = type switch
                {
                    "byte" or "char" or "uchar" or "uint8_t" or "int8_t" or "bool" or "undefined1" => 8,
                    "ushort" or "short" or "uint16_t" or "int16_t" or "undefined2" => 16,
                    _ => 32,
                };
                if (castBits < lookupBits)
                {
                    reason = $"'{argument.Trim()}' narrows with a ({type}) cast";
                    return null;
                }
            }
            string e = Cast.Replace(argument, "").Trim();
            while (e.Length > 1 && e[0] == '(' && MatchingParen(e, 0) == e.Length - 1) e = e[1..^1].Trim();
            var tokens = new List<string>();
            int at = 0;
            while (at < e.Length)
            {
                var m = Token.Match(e, at);
                if (!m.Success || m.Index != at)
                {
                    reason = $"'{argument.Trim()}' is not a plain expression";
                    return null;
                }
                tokens.Add(m.Groups[1].Value);
                at += m.Length;
            }
            var names = tokens.Where(t => Identifier.IsMatch(t)).ToList();
            if (names.Count == 0)
            {
                reason = $"'{argument.Trim()}' is a constant";
                return null;
            }
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!Identifier.IsMatch(tokens[i])) continue;
                if (i + 1 < tokens.Count && tokens[i + 1] == "(")
                {
                    reason = $"'{argument.Trim()}' calls a function";
                    return null;
                }
                if (!variables.ContainsKey(tokens[i]))
                {
                    reason = $"'{tokens[i]}' is not a RAM variable in the catalog (a local or unnamed)";
                    return null;
                }
            }
            string expression = string.Join(" ", tokens).Replace("( ", "(").Replace(" )", ")").Replace("~ ", "~");
            if (mask is not { } bits) return expression;
            string maskText = "0x" + bits.ToString("x", CultureInfo.InvariantCulture);
            if (names.Count == 1 && tokens.Count == 1)
            {
                var type = variables[names[0]].Type;
                if (!type.Signed && type.Size * 8 <= BitWidth(bits)) return expression; // already fits the lookup
            }
            else if (tokens.Count >= 2 && tokens[^2] == "&" && tokens[^1].Equals(maskText, StringComparison.OrdinalIgnoreCase))
                return expression;
            return tokens.Count == 1 ? $"{expression} & {maskText}" : $"({expression}) & {maskText}";
        }

        private static int BitWidth(long mask) => 64 - (int)ulong.LeadingZeroCount((ulong)mask);

        private static int MatchingParen(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')' && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>The comma-separated arguments of a call whose '(' ends just before <paramref name="start"/>.</summary>
        private static List<string>? Arguments(string text, int start)
        {
            var args = new List<string>();
            var current = new StringBuilder();
            int depth = 1;
            bool quoted = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    current.Append(c);
                    if (c == '\\' && i + 1 < text.Length) current.Append(text[++i]);
                    else if (c == '\'') quoted = false;
                    continue;
                }
                switch (c)
                {
                    case '\'': quoted = true; current.Append(c); break;
                    case '(' or '[': depth++; current.Append(c); break;
                    case ')' or ']':
                        if (--depth == 0)
                        {
                            args.Add(current.ToString().Trim());
                            return args;
                        }
                        current.Append(c);
                        break;
                    case ',' when depth == 1:
                        args.Add(current.ToString().Trim());
                        current.Clear();
                        break;
                    default: current.Append(c); break;
                }
            }
            return null;
        }

        private static List<int> LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
            return starts;
        }

        private static int LineOf(List<int> starts, int position)
        {
            int index = starts.BinarySearch(position);
            return (index >= 0 ? index : ~index - 1) + 1;
        }

        // ── The reviewable file ──────────────────────────────────────────────────────────────────────────

        public const string Header = "axis,input,lookup,lines";

        public static string Format(IEnumerable<AxisInput> inputs, string source)
        {
            var text = new StringBuilder();
            text.Append("# Axis inputs for the A2L generator: the expression each lookup passes for this axis, as the ECU sees it\n");
            text.Append("# (raw values, after the lookup's own truncation). Drafted by A2lTool extract-inputs from ").Append(source).Append(".\n");
            text.Append("# Review each row against the decompile and delete any that are wrong; regenerate the A2L afterwards.\n");
            text.Append(Header).Append('\n');
            foreach (var input in inputs)
                text.Append(input.Axis).Append(',').Append(Quote(input.Expression)).Append(',').Append(input.Lookup)
                    .Append(',').Append(input.Lines).Append('\n');
            return text.ToString();

            static string Quote(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        /// <summary>Reads the file: axis name → expression. Lines starting with # are comments.</summary>
        public static Dictionary<string, string> Load(string path)
        {
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line == Header) continue;
                var fields = SplitCsv(line);
                if (fields.Count < 2 || fields[0].Length == 0 || fields[1].Length == 0)
                    throw new InvalidDataException($"{Path.GetFileName(path)}: '{line}' needs an axis and an input.");
                inputs[fields[0]] = fields[1];
            }
            return inputs;
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString().Trim());
            return fields;
        }
    }
}
