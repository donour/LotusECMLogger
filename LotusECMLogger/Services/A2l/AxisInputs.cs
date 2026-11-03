using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LotusECMLogger.Services.A2l
{
    /// <summary>
    /// One axis's input as the ECU's lookup sees it: an expression over RAM variables, already truncated the way the
    /// lookup variant truncates its argument (e.g. <c>(engine_speed_16bit) &amp; 0xff</c> for an 8-bit lookup).
    /// </summary>
    /// <param name="Note">How the input was found when it took more than reading the call: a local followed to the
    /// variable it holds, an unnamed global named by address, or constant inputs that were ignored.</param>
    public sealed record AxisInput(string Axis, string Expression, string Lookup, string Lines, string Note = "");

    /// <summary>A lookup call whose input could not be bound, with the reason, for the extraction report.</summary>
    public sealed record SkippedAxisInput(string Axis, string Reason);

    /// <summary>
    /// Drafts axis input bindings from a Ghidra decompile (Phase 6e). Every <c>lookup_*</c> call names its axis
    /// arrays, so each binding belongs to an AXIS_PTS by name. A binding is kept only when every call site that does
    /// not pass a constant gives the same input, and that input comes down to the catalog's RAM variables, numbers and
    /// simple operators. Unnamed globals are named by address; a local of the calling function is followed to the one
    /// variable it holds (ignoring its constant defaults and fallbacks), including through a getter that returns it.
    /// Array or struct accesses, other calls, conditionals and offset axis pointers are left unbound. The result is a
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
        private static readonly Regex Cast = new(@"\(\s*(u?int\d+_t|ushort|uint|uchar|byte|short|char|int|long|ulong|longlong|ulonglong|bool|undefined\d?)\s*\*?\s*\)", RegexOptions.Compiled);
        // A catalog typedef used as a cast, e.g. (u8_factor_1_255)x. A parenthesised variable is followed by an operator, not an operand.
        private static readonly Regex TypedefCast = new(@"\(\s*([uis](?:8|16|32)_\w+)\s*\)(?=\s*[A-Za-z_(])", RegexOptions.Compiled);
        private static readonly Regex Typedef = new(@"^([uis])(8|16|32)_\w+$", RegexOptions.Compiled);
        private static readonly Regex Token = new(@"\s*(0x[0-9A-Fa-f]+|\d+|[A-Za-z_]\w*|>>|<<|[&|^+\-*/~()])", RegexOptions.Compiled);
        private static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);
        private static readonly Regex ShiftOrDivide = new(@"(>>|/)\s*(0x[0-9A-Fa-f]+|\d+)?", RegexOptions.Compiled);

        /// <summary>An input as normalized: the expression, or why there is none, and how it was found.</summary>
        /// <param name="IsConstant">The input is a number or a calibration value, so it says nothing about the operating point.</param>
        private sealed record Normalized(string? Expression, string? Reason, bool IsConstant = false, string Note = "")
        {
            public string Key => Expression ?? "?" + Reason;
        }

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
            var names = new NameResolver(decompile, variables);
            var found = new Dictionary<string, List<(Normalized Input, string Lookup, int Line)>>(StringComparer.Ordinal);
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
                    list.Add((names.Normalize(args[signature.Inputs[k]], signature.Mask, m.Index), m.Groups[1].Value, line));
                }
            }

            var bound = new List<AxisInput>();
            foreach (var (axis, uses) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                // A constant input evaluates the table at a fixed point (a default or a fault value): it is not the operating point.
                var varying = uses.Where(u => !u.Input.IsConstant).ToList();
                var distinct = varying.Select(u => u.Input.Key).Distinct(StringComparer.Ordinal).ToList();
                if (varying.Count == 0)
                    skipped.Add(new(axis, $"only constant inputs: {string.Join(" | ", uses.Select(u => u.Input.Reason).Distinct())}"));
                else if (distinct.Count > 1)
                    skipped.Add(new(axis, $"{distinct.Count} different inputs: {string.Join(" | ", distinct.Select(d => d.TrimStart('?')))}"));
                else if (varying[0].Input.Expression is not { } expression)
                    skipped.Add(new(axis, varying[0].Input.Reason!));
                else
                {
                    var notes = varying.Select(u => u.Input.Note).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    var constantLines = uses.Where(u => u.Input.IsConstant).Select(u => u.Line.ToString(CultureInfo.InvariantCulture)).ToList();
                    if (constantLines.Count > 0)
                        notes.Add($"constant input at line{(constantLines.Count == 1 ? "" : "s")} {string.Join(" ", constantLines)} ignored");
                    bound.Add(new(axis, expression, string.Join(" ", uses.Select(u => u.Lookup).Distinct()),
                        string.Join(" ", uses.Select(u => u.Line.ToString(CultureInfo.InvariantCulture))), string.Join("; ", notes)));
                }
            }
            return (bound, skipped);
        }

        /// <summary>
        /// The input as the lookup sees it, or null (with the reason) when it is not a plain expression over RAM
        /// variables. Casts are dropped; the lookup's own truncation is added unless the variable already fits.
        /// </summary>
        public static string? Normalize(string argument, long? mask, IReadOnlyDictionary<string, SymbolEntry> variables, out string? reason)
        {
            var result = Normalize(argument, mask, variables, null);
            reason = result.Reason;
            return result.Expression;
        }

        /// <param name="resolve">Turns a name that is not a catalog variable into an expression over catalog variables
        /// (given the mask it will be truncated to), or says why it cannot; null leaves every such name unbound.</param>
        private static Normalized Normalize(string argument, long? mask, IReadOnlyDictionary<string, SymbolEntry> variables,
            Func<string, long?, Normalized>? resolve)
        {
            Normalized Unbound(string why) => new(null, why);
            string text = TypedefCast.Replace(argument, c => $"({BuiltinCast(TypeWidth(c.Groups[1].Value)!.Value)})").Trim();
            // Casts are dropped, which is only safe when the lookup truncates at least as narrowly as the cast, and no
            // right shift or division after a narrowing cast brings in bits the cast removed.
            int lookupBits = mask is { } m0 ? BitWidth(m0) : 32;
            foreach (Match cast in Cast.Matches(text))
            {
                string type = cast.Groups[1].Value;
                int castBits = TypeWidth(type)?.Bits ?? 32;
                if (castBits >= 32) continue;
                if (castBits < lookupBits) return Unbound($"'{argument.Trim()}' narrows with a ({type}) cast");
                int discarded = ShiftAfter(text[OperandEnd(text, cast.Index + cast.Length)..]);
                if (discarded < 0 || castBits < lookupBits + discarded)
                    return Unbound($"'{argument.Trim()}' shifts or divides after a ({type}) cast");
            }
            string e = Cast.Replace(text, "").Trim();
            while (e.Length > 1 && e[0] == '(' && MatchingParen(e, 0) == e.Length - 1) e = e[1..^1].Trim();
            if (Tokenize(e) is not { } raw) return Unbound($"'{argument.Trim()}' is not a plain expression");
            var rawNames = raw.Where(t => Identifier.IsMatch(t)).ToList();
            if (rawNames.Count == 0) return new(null, $"'{argument.Trim()}' is a constant", IsConstant: true);
            if (rawNames.All(IsCalibration)) return new(null, $"'{argument.Trim()}' is a calibration value", IsConstant: true);

            var tokens = new List<string>();
            var notes = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string t = raw[i];
                if (!Identifier.IsMatch(t)) { tokens.Add(t); continue; }
                if (i + 1 < raw.Count && raw[i + 1] == "(") return Unbound($"'{argument.Trim()}' calls a function");
                if (variables.ContainsKey(t)) { tokens.Add(t); continue; }
                if (IsCalibration(t)) return Unbound($"'{argument.Trim()}' mixes a calibration value into the input");
                var resolved = resolve?.Invoke(t, mask) ?? Unbound($"'{t}' is not a RAM variable in the catalog (a local or unnamed)");
                if (resolved.Expression is not { } substitute) return resolved;
                notes.Add(resolved.Note);
                var inner = Tokenize(substitute)!;
                bool bare = inner.Count == 1 || raw.Count == 1;
                if (!bare) tokens.Add("(");
                tokens.AddRange(inner);
                if (!bare) tokens.Add(")");
            }
            string note = string.Join("; ", notes.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal));
            var names = tokens.Where(t => Identifier.IsMatch(t)).ToList();
            string expression = string.Join(" ", tokens).Replace("( ", "(").Replace(" )", ")").Replace("~ ", "~");
            if (mask is not { } bits) return new(expression, null, Note: note);
            string maskText = "0x" + bits.ToString("x", CultureInfo.InvariantCulture);
            if (names.Count == 1 && tokens.Count == 1)
            {
                var type = variables[names[0]].Type;
                if (!type.Signed && type.Size * 8 <= BitWidth(bits)) return new(expression, null, Note: note); // already fits the lookup
            }
            else if (MaskedWithin(tokens, bits))
                return new(expression, null, Note: note);
            return new(tokens.Count == 1 ? $"{expression} & {maskText}" : $"({expression}) & {maskText}", null, Note: note);
        }

        /// <summary>
        /// Whether the expression already ends in <c>&amp; K</c> with K inside <paramref name="mask"/>, and that '&amp;'
        /// applies to all of it: no '|' or '^', which bind more loosely, outside parentheses.
        /// </summary>
        private static bool MaskedWithin(List<string> tokens, long mask)
        {
            if (tokens.Count < 3 || tokens[^2] != "&" || ParseNumber(tokens[^1]) is not { } k || (k & ~mask) != 0) return false;
            int depth = 0;
            foreach (var t in tokens.Take(tokens.Count - 2))
            {
                if (t == "(") depth++;
                else if (t == ")") depth--;
                else if (depth == 0 && t is "|" or "^") return false;
            }
            return true;
        }

        private static long? ParseNumber(string token) =>
            token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex) ? hex : null
                : long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out long dec) ? dec : null;

        private static bool IsCalibration(string name) => name.StartsWith("CAL_", StringComparison.Ordinal);

        private static List<string>? Tokenize(string e)
        {
            var tokens = new List<string>();
            int at = 0;
            while (at < e.Length)
            {
                var m = Token.Match(e, at);
                if (!m.Success || m.Index != at) return null;
                tokens.Add(m.Groups[1].Value);
                at += m.Length;
            }
            return tokens;
        }

        /// <summary>A C type's width and signedness: built in, Ghidra's undefinedN, or a catalog typedef such as u8_factor_1_255.</summary>
        private static (int Bits, bool Signed)? TypeWidth(string type) => type switch
        {
            "byte" or "uchar" or "uint8_t" or "bool" or "undefined1" => (8, false),
            "char" or "int8_t" => (8, true),
            "ushort" or "uint16_t" or "undefined2" => (16, false),
            "short" or "int16_t" => (16, true),
            "uint" or "uint32_t" or "undefined4" or "ulong" or "undefined" => (32, false),
            "int" or "int32_t" or "long" => (32, true),
            "ulonglong" or "undefined8" => (64, false),
            "longlong" => (64, true),
            _ => Typedef.Match(type) is { Success: true } t
                ? (int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture), t.Groups[1].Value != "u") : null,
        };

        private static string BuiltinCast((int Bits, bool Signed) width) => width switch
        {
            (8, false) => "uchar",
            (8, true) => "char",
            (16, false) => "ushort",
            (16, true) => "short",
            (32, false) => "uint",
            (32, true) => "int",
            (_, false) => "ulonglong",
            _ => "longlong",
        };

        /// <summary>Where the operand of a cast ending at <paramref name="start"/> ends: a parenthesised group, a further cast's operand, or one token.</summary>
        private static int OperandEnd(string s, int start)
        {
            int i = start;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) return i;
            if (Cast.Match(s, i) is { Success: true } inner && inner.Index == i) return OperandEnd(s, i + inner.Length);
            if (s[i] is '~' or '-') return OperandEnd(s, i + 1);
            if (s[i] == '(') return MatchingParen(s, i) is >= 0 and var close ? close + 1 : s.Length;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            return i;
        }

        /// <summary>How many low bits the right shifts and divisions in <paramref name="rest"/> discard, or -1 when one is not by a constant.</summary>
        private static int ShiftAfter(string rest)
        {
            int total = 0;
            foreach (Match op in ShiftOrDivide.Matches(rest))
            {
                if (!op.Groups[2].Success || ParseNumber(op.Groups[2].Value) is not { } n) return -1;
                total +=op.Groups[1].Value == ">>" ? (int)Math.Min(n, 64) : n <= 1 ? 0 : BitWidth(n - 1);
            }
            return total;
        }

        private static int BitWidth(long mask) => 64 - (int)ulong.LeadingZeroCount((ulong)mask);

        /// <summary>
        /// Names in lookup arguments that are not catalog variables: unnamed globals (<c>DAT_40002148</c>, or Ghidra's
        /// type_address names) that the catalog names at that address, and locals of the calling function that hold one
        /// variable's value, possibly through a getter that returns it. A local's constant assignments (defaults, fault
        /// and saturation values) are ignored and noted; a local given two different values stays unbound.
        /// </summary>
        private sealed class NameResolver(string decompile, IReadOnlyDictionary<string, SymbolEntry> variables)
        {
            private const int MaxDepth = 3;
            private static readonly Regex Unnamed = new(@"(?:^DAT_|_)([0-9A-Fa-f]{8})$", RegexOptions.Compiled);
            private static readonly Regex GetterCall = new(@"^(\w+)\s*\(\s*\)$", RegexOptions.Compiled);
            private readonly Dictionary<uint, string> byAddress = variables.Values.GroupBy(v => v.Address)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Name);
            private readonly Dictionary<string, (string Cast, string Expression, int At)?> getters = new(StringComparer.Ordinal);

            /// <param name="at">A position in the calling function, whose locals the input may use.</param>
            public Normalized Normalize(string argument, long? mask, int at, int depth = 0) =>
                AxisInputs.Normalize(argument, mask, variables, (name, outer) => Resolve(name, outer, at, depth));

            private Normalized Resolve(string name, long? outerMask, int at, int depth)
            {
                static Normalized Unbound(string why) => new(null, why);
                if (Unnamed.Match(name) is { Success: true } unnamed
                    && byAddress.TryGetValue(uint.Parse(unnamed.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), out var named))
                    return new(named, null, Note: $"{name} is {named}");
                string unknown = $"'{name}' is not a RAM variable in the catalog (a local or unnamed)";
                if (depth >= MaxDepth) return Unbound(unknown);

                // The calling function: from the previous function's closing brace to its own.
                int start = decompile.LastIndexOf("\n}", at, StringComparison.Ordinal) + 1;
                int end = decompile.IndexOf("\n}", at, StringComparison.Ordinal);
                string body = decompile[start..(end < 0 ? decompile.Length : end)];
                string n = Regex.Escape(name);
                var declaration = Regex.Match(body, $@"(?m)^\s+(\w+)\s+{n};");
                if (!declaration.Success) return Unbound(unknown);
                if (TypeWidth(declaration.Groups[1].Value) is not (int bits, bool signed))
                    return Unbound($"local '{name}' has type {declaration.Groups[1].Value}");
                if (Regex.IsMatch(body, $@"(?<![\w.>]){n}\s*(?:\+\+|--|[-+*/%&|^]=|<<=|>>=)|(?:\+\+|--)\s*{n}\b|[(,=]\s*&\s*{n}\b"))
                    return Unbound($"local '{name}' is changed in place");
                // A narrower signed local is sign-extended into a wider lookup input, which no mask expresses.
                if (signed && bits < (outerMask is { } om ? BitWidth(om) : 32)) return Unbound($"local '{name}' is a signed {bits}-bit value");
                long? localMask = bits < 32 ? (1L << bits) - 1 : null;

                var values = new List<Normalized>();
                foreach (Match assignment in Regex.Matches(body, $@"(?<![\w.>]){n}\s*=(?!=)\s*([^;]+);"))
                {
                    string rhs = assignment.Groups[1].Value.Trim();
                    int rhsAt = start + assignment.Groups[1].Index;
                    string how = $"local {name}";
                    if (Regex.IsMatch(rhs, $@"\b{n}\b")) return Unbound($"local '{name}' depends on its previous value");
                    if (GetterCall.Match(rhs) is { Success: true } call)
                    {
                        if (Getter(call.Groups[1].Value) is not (string cast, string returned, int getterAt))
                            return Unbound($"local '{name}' = '{rhs}' calls a function");
                        (rhs, rhsAt, how) = ($"({cast})({returned})", getterAt, $"{how} = {call.Groups[1].Value}()");
                    }
                    var value = Normalize(rhs, localMask, rhsAt, depth + 1);
                    values.Add(value with { Note = value.Note.Length > 0 ? $"{how}, {value.Note}" : how });
                }
                if (values.Count == 0) return Unbound($"local '{name}' is never assigned in its function (a parameter?)");
                var varying = values.Where(v => !v.IsConstant).ToList();
                if (varying.Count == 0) return new(null, $"local '{name}' only holds constants", IsConstant: true);
                var distinct = varying.Select(v => v.Key).Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count > 1)
                    return Unbound($"local '{name}' takes {distinct.Count} different values: {string.Join(" | ", distinct.Select(d => d.TrimStart('?')))}");
                if (varying[0].Expression is null) return Unbound($"local '{name}': {varying[0].Reason}");
                return varying.Count < values.Count ? varying[0] with { Note = varying[0].Note + " (its constant values ignored)" } : varying[0];
            }

            /// <summary>A function that only returns an expression, e.g. <c>get_accel_pedal_pos</c>: its return type as a cast, and the expression.</summary>
            private (string Cast, string Expression, int At)? Getter(string function)
            {
                if (getters.TryGetValue(function, out var cached)) return cached;
                var m = Regex.Match(decompile, $@"(?m)^(\w+)\s+{Regex.Escape(function)}\s*\(void\)\s*\{{\s*return\s+([^;{{}}]+);\s*\}}");
                (string, string, int)? getter = m.Success && TypeWidth(m.Groups[1].Value) is { } width
                    ? (BuiltinCast(width), m.Groups[2].Value.Trim(), m.Groups[2].Index) : null;
                getters[function] = getter;
                return getter;
            }
        }

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

        public const string Header = "axis,input,lookup,lines,note";

        public static string Format(IEnumerable<AxisInput> inputs, string source)
        {
            var text = new StringBuilder();
            text.Append("# Axis inputs for the A2L generator: the expression each lookup passes for this axis, as the ECU sees it\n");
            text.Append("# (raw values, after the lookup's own truncation). Drafted by A2lTool extract-inputs from ").Append(source).Append(".\n");
            text.Append("# Review each row against the decompile and delete any that are wrong; regenerate the A2L afterwards.\n");
            text.Append("# A note says how a row was found beyond reading the call (a local followed, an address named, constants ignored).\n");
            text.Append(Header).Append('\n');
            foreach (var input in inputs)
                text.Append(input.Axis).Append(',').Append(Quote(input.Expression)).Append(',').Append(input.Lookup)
                    .Append(',').Append(input.Lines).Append(',').Append(Quote(input.Note)).Append('\n');
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
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("axis,input,", StringComparison.Ordinal)) continue;
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
