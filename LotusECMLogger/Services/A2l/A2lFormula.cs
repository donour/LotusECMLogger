using System.Globalization;

namespace LotusECMLogger.Services.A2l
{
    /// <summary>
    /// An ASAP2 FORMULA expression over X (or X1…Xn): arithmetic, the C bit and logic operators (<c>&gt;&gt;</c>,
    /// <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>~</c>, <c>!</c>, comparisons, <c>&amp;&amp;</c>, <c>||</c>) and the usual maths
    /// functions. Bit operators work on the operands truncated to 64-bit integers, as the firmware's C does.
    /// </summary>
    public sealed class A2lFormula
    {
        private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["abs"] = Math.Abs, ["sqrt"] = Math.Sqrt, ["exp"] = Math.Exp, ["log"] = Math.Log, ["ln"] = Math.Log,
            ["log10"] = Math.Log10, ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan, ["asin"] = Math.Asin,
            ["acos"] = Math.Acos, ["atan"] = Math.Atan, ["sinh"] = Math.Sinh, ["cosh"] = Math.Cosh, ["tanh"] = Math.Tanh,
            ["floor"] = Math.Floor, ["ceil"] = Math.Ceiling,
        };

        private readonly Func<IReadOnlyList<double>, double> _evaluate;

        public string Text { get; }
        /// <summary>The highest X index used (X alone counts as X1).</summary>
        public int InputCount { get; }

        private A2lFormula(string text, Func<IReadOnlyList<double>, double> evaluate, int inputs) =>
            (Text, _evaluate, InputCount) = (text, evaluate, inputs);

        public double Evaluate(IReadOnlyList<double> inputs)
        {
            if (inputs.Count < InputCount)
                throw new ArgumentException($"The formula reads X{InputCount} but {inputs.Count} input(s) were given.");
            return _evaluate(inputs);
        }

        /// <summary>Parses a formula, or throws <see cref="FormatException"/> naming what it could not read.</summary>
        public static A2lFormula Parse(string text)
        {
            var parser = new Parser(text);
            var expression = parser.ParseExpression(0);
            parser.ExpectEnd();
            return new A2lFormula(text, expression, parser.MaxInput);
        }

        public static bool TryParse(string text, out A2lFormula? formula, out string? error)
        {
            try { (formula, error) = (Parse(text), null); return true; }
            catch (FormatException e) { (formula, error) = (null, e.Message); return false; }
        }

        private sealed class Parser(string text)
        {
            private int _at;
            public int MaxInput { get; private set; }

            // Binary operators by C precedence, lowest first.
            private static readonly string[][] Levels =
            [
                ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<=", ">=", "<", ">"], ["<<", ">>"], ["+", "-"], ["*", "/", "%"],
            ];

            public Func<IReadOnlyList<double>, double> ParseExpression(int level)
            {
                if (level == Levels.Length) return ParseUnary();
                var left = ParseExpression(level + 1);
                while (true)
                {
                    SkipSpace();
                    string? op = Levels[level].FirstOrDefault(o => string.CompareOrdinal(text, _at, o, 0, o.Length) == 0
                        // "&" and "|" must not swallow "&&" and "||"; "<" must not swallow "<<".
                        && !(o.Length == 1 && _at + 1 < text.Length && text[_at + 1] == o[0] && o[0] is '&' or '|' or '<' or '>'));
                    if (op is null) return left;
                    _at += op.Length;
                    var right = ParseExpression(level + 1);
                    var l = left;
                    left = op switch
                    {
                        "||" => x => l(x) != 0 || right(x) != 0 ? 1 : 0,
                        "&&" => x => l(x) != 0 && right(x) != 0 ? 1 : 0,
                        "|" => x => (long)l(x) | (long)right(x),
                        "^" => x => (long)l(x) ^ (long)right(x),
                        "&" => x => (long)l(x) & (long)right(x),
                        "==" => x => l(x) == right(x) ? 1 : 0,
                        "!=" => x => l(x) != right(x) ? 1 : 0,
                        "<=" => x => l(x) <= right(x) ? 1 : 0,
                        ">=" => x => l(x) >= right(x) ? 1 : 0,
                        "<" => x => l(x) < right(x) ? 1 : 0,
                        ">" => x => l(x) > right(x) ? 1 : 0,
                        "<<" => x => (long)l(x) << (int)right(x),
                        ">>" => x => (long)l(x) >> (int)right(x),
                        "+" => x => l(x) + right(x),
                        "-" => x => l(x) - right(x),
                        "*" => x => l(x) * right(x),
                        "/" => x => l(x) / right(x),
                        _ => x => l(x) % right(x),
                    };
                }
            }

            private Func<IReadOnlyList<double>, double> ParseUnary()
            {
                SkipSpace();
                if (Peek('-')) { _at++; var inner = ParseUnary(); return x => -inner(x); }
                if (Peek('+')) { _at++; return ParseUnary(); }
                if (Peek('~')) { _at++; var inner = ParseUnary(); return x => ~(long)inner(x); }
                if (Peek('!')) { _at++; var inner = ParseUnary(); return x => inner(x) == 0 ? 1 : 0; }
                return ParsePrimary();
            }

            private Func<IReadOnlyList<double>, double> ParsePrimary()
            {
                SkipSpace();
                if (Peek('('))
                {
                    _at++;
                    var inner = ParseExpression(0);
                    Expect(')');
                    return inner;
                }
                if (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) return ParseNumber();
                if (_at < text.Length && (char.IsLetter(text[_at]) || text[_at] == '_'))
                {
                    int start = _at;
                    while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] == '_')) _at++;
                    string name = text[start.._at];
                    if (name is "X" or "x") return Input(1);
                    if (name.Length > 1 && name[0] is 'X' or 'x' && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index > 0)
                        return Input(index);
                    if (Functions.TryGetValue(name, out var function))
                    {
                        Expect('(');
                        var argument = ParseExpression(0);
                        Expect(')');
                        return x => function(argument(x));
                    }
                    if (name.Equals("pow", StringComparison.OrdinalIgnoreCase))
                    {
                        Expect('(');
                        var a = ParseExpression(0);
                        Expect(',');
                        var b = ParseExpression(0);
                        Expect(')');
                        return x => Math.Pow(a(x), b(x));
                    }
                    throw new FormatException($"Unknown name '{name}' at {start + 1} in \"{text}\".");
                }
                throw new FormatException($"Expected a value at {_at + 1} in \"{text}\".");
            }

            private Func<IReadOnlyList<double>, double> Input(int index)
            {
                MaxInput = Math.Max(MaxInput, index);
                return x => x[index - 1];
            }

            private Func<IReadOnlyList<double>, double> ParseNumber()
            {
                int start = _at;
                double value;
                if (text.Length - _at > 2 && text[_at] == '0' && text[_at + 1] is 'x' or 'X')
                {
                    _at += 2;
                    while (_at < text.Length && Uri.IsHexDigit(text[_at])) _at++;
                    value = ulong.Parse(text.AsSpan(start + 2, _at - start - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                else
                {
                    while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) _at++;
                    if (_at < text.Length && text[_at] is 'e' or 'E')
                    {
                        _at++;
                        if (_at < text.Length && text[_at] is '+' or '-') _at++;
                        while (_at < text.Length && char.IsDigit(text[_at])) _at++;
                    }
                    if (!double.TryParse(text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        throw new FormatException($"Bad number at {start + 1} in \"{text}\".");
                }
                return _ => value;
            }

            private bool Peek(char c) => _at < text.Length && text[_at] == c;

            private void Expect(char c)
            {
                SkipSpace();
                if (!Peek(c)) throw new FormatException($"Expected '{c}' at {_at + 1} in \"{text}\".");
                _at++;
            }

            public void ExpectEnd()
            {
                SkipSpace();
                if (_at != text.Length) throw new FormatException($"Unexpected '{text[_at]}' at {_at + 1} in \"{text}\".");
            }

            private void SkipSpace()
            {
                while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
            }
        }
    }
}
