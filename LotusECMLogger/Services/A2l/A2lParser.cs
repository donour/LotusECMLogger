using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services.A2l
{
    /// <summary>
    /// A tolerant reader for the A2L subset live tuning needs (CCP-client-plan.md §6). The file is first read into a
    /// generic tree of <c>/begin … /end</c> blocks, so unknown blocks and vendor <c>IF_DATA</c> are skipped whole.
    /// Items whose addresses, layout, scaling or byte order use something unsupported keep a <c>Problem</c> and are
    /// unavailable; they never fall back to defaults that would put an edit at the wrong address.
    /// </summary>
    public static class A2lParser
    {
        public static A2lModule ParseFile(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

        /// <summary>Parses the first MODULE of an A2L document.</summary>
        public static A2lModule Parse(string text, string? source = null)
        {
            var tree = new Tree(Tokenize(text));
            var diagnostics = new List<A2lDiagnostic>(tree.Diagnostics);
            var root = tree.Root;
            var project = root.Children.FirstOrDefault(c => c.Keyword == "PROJECT") ?? root;
            var modules = project.Children.Where(c => c.Keyword == "MODULE").ToList();
            if (modules.Count == 0) throw new InvalidDataException($"{source ?? "The A2L"} has no MODULE.");
            if (modules.Count > 1)
                diagnostics.Add(new(modules[1].Line, $"{modules.Count} modules: only {modules[0].Name(0)} is read."));
            return new ModuleBuilder(modules[0], diagnostics).Build();
        }

        // ── Tokens and the block tree ────────────────────────────────────────────────────────────────────────

        internal enum TokenKind { Word, String, Begin, End }

        internal readonly record struct Token(TokenKind Kind, string Text, int Line);

        internal static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int line = 1, i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\n') { line++; i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                    line += CountLines(text, i, end);
                    i = end;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '"')
                {
                    int startLine = line;
                    var value = new StringBuilder();
                    i++;
                    while (i < text.Length)
                    {
                        char s = text[i];
                        if (s == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i += 2; continue; }
                            i++;
                            break;
                        }
                        if (s == '\\' && i + 1 < text.Length)
                        {
                            char e = text[i + 1];
                            value.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
                            i += 2;
                            continue;
                        }
                        if (s == '\n') line++;
                        value.Append(s);
                        i++;
                    }
                    tokens.Add(new(TokenKind.String, value.ToString(), startLine));
                    continue;
                }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"' &&
                       !(text[i] == '/' && i + 1 < text.Length && text[i + 1] is '*' or '/' && i > start))
                    i++;
                string word = text[start..i];
                tokens.Add(word switch
                {
                    "/begin" => new(TokenKind.Begin, word, line),
                    "/end" => new(TokenKind.End, word, line),
                    _ => new(TokenKind.Word, word, line),
                });
            }
            return tokens;
        }

        private static int CountLines(string text, int start, int end)
        {
            int n = 0;
            for (int i = start; i < end; i++) if (text[i] == '\n') n++;
            return n;
        }

        /// <summary>A <c>/begin KEYWORD … /end KEYWORD</c> block: its own tokens in order, and nested blocks.</summary>
        internal sealed class Node(string keyword, int line)
        {
            public string Keyword { get; } = keyword;
            public int Line { get; } = line;
            public List<Token> Tokens { get; } = [];
            public List<Node> Children { get; } = [];

            public string Name(int index) => index < Tokens.Count ? Tokens[index].Text : "";

            /// <summary>The arguments after the first top-level word <paramref name="keyword"/>, or null.</summary>
            public Token[]? Option(string keyword, int count)
            {
                for (int i = 0; i < Tokens.Count; i++)
                {
                    if (Tokens[i].Kind != TokenKind.Word || Tokens[i].Text != keyword) continue;
                    return i + count < Tokens.Count ? Tokens.GetRange(i + 1, count).ToArray() : null;
                }
                return null;
            }

            public bool Has(string keyword) => Tokens.Any(t => t.Kind == TokenKind.Word && t.Text == keyword);
        }

        private sealed class Tree
        {
            private readonly List<Token> _tokens;
            private readonly List<A2lDiagnostic> _diagnostics = [];
            private int _at;

            public Node Root { get; } = new("", 0);
            public IReadOnlyList<A2lDiagnostic> Diagnostics => _diagnostics;

            public Tree(List<Token> tokens)
            {
                _tokens = tokens;
                Fill(Root);
            }

            private void Fill(Node node)
            {
                while (_at < _tokens.Count)
                {
                    var token = _tokens[_at++];
                    if (token.Kind == TokenKind.Begin)
                    {
                        if (_at >= _tokens.Count) break;
                        var keyword = _tokens[_at++];
                        var child = new Node(keyword.Text, keyword.Line);
                        node.Children.Add(child);
                        Fill(child);
                    }
                    else if (token.Kind == TokenKind.End)
                    {
                        string closing = _at < _tokens.Count ? _tokens[_at].Text : "";
                        if (closing != node.Keyword)
                            _diagnostics.Add(new(token.Line, $"/end {closing} closes /begin {node.Keyword} (line {node.Line})."));
                        _at++;
                        return;
                    }
                    else
                    {
                        if (token.Kind == TokenKind.Word && token.Text == "/include")
                            _diagnostics.Add(new(token.Line, "/include is not followed; the included file is not read."));
                        node.Tokens.Add(token);
                    }
                }
                if (node != Root) _diagnostics.Add(new(node.Line, $"/begin {node.Keyword} is never closed."));
            }
        }

        // ── Building the module ──────────────────────────────────────────────────────────────────────────────

        /// <param name="earlier">Diagnostics from tokenizing and the tree, which the module's own follow.</param>
        private sealed class ModuleBuilder(Node module, IEnumerable<A2lDiagnostic> earlier)
        {
            private readonly List<A2lDiagnostic> diagnostics = [.. earlier];

            private static readonly Dictionary<A2lDataType, int> DefaultAlignment = new()
            {
                [A2lDataType.UByte] = 1, [A2lDataType.SByte] = 1, [A2lDataType.UWord] = 2, [A2lDataType.SWord] = 2,
                [A2lDataType.ULong] = 4, [A2lDataType.SLong] = 4, [A2lDataType.Float32] = 4,
                [A2lDataType.UInt64] = 8, [A2lDataType.Int64] = 8, [A2lDataType.Float64] = 8,
            };

            private readonly Dictionary<string, Node> _vtabs = [];

            public A2lModule Build()
            {
                var (byteOrder, alignment) = ReadModCommon(module.Children.FirstOrDefault(c => c.Keyword == "MOD_COMMON"));
                var modPar = module.Children.FirstOrDefault(c => c.Keyword == "MOD_PAR");
                foreach (var vtab in module.Children.Where(c => c.Keyword is "COMPU_VTAB"))
                    _vtabs[vtab.Name(0)] = vtab;

                var compu = Collect("COMPU_METHOD", ReadCompuMethod, m => m.Name);
                var layouts = Collect("RECORD_LAYOUT", n => ReadRecordLayout(n, alignment), l => l.Name);
                var measurements = Collect("MEASUREMENT", ReadMeasurement, m => m.Name);
                var axisPts = Collect("AXIS_PTS", ReadAxisPts, a => a.Name);
                var characteristics = Collect("CHARACTERISTIC", ReadCharacteristic, c => c.Name);

                return new A2lModule
                {
                    Name = module.Name(0),
                    ByteOrder = byteOrder,
                    Epk = modPar?.Option("EPK", 1)?[0].Text,
                    EpkAddress = modPar?.Option("ADDR_EPK", 1) is [var epk] ? Address(epk) : null,
                    MemorySegments = modPar?.Children.Where(c => c.Keyword == "MEMORY_SEGMENT").Select(ReadSegment)
                        .OfType<A2lMemorySegment>().ToList() ?? [],
                    CompuMethods = compu,
                    RecordLayouts = layouts,
                    Measurements = measurements,
                    AxisPts = axisPts,
                    Characteristics = characteristics,
                    Alignment = alignment,
                    Groups = module.Children.Where(c => c.Keyword == "GROUP").Select(ReadGroup).ToList(),
                    Diagnostics = diagnostics,
                };
            }

            private static A2lGroup ReadGroup(Node node) => new(node.Name(0),
                node.Tokens.Count > 1 && node.Tokens[1].Kind == TokenKind.String ? node.Tokens[1].Text : "", node.Has("ROOT"),
                Names(node, "REF_CHARACTERISTIC"), Names(node, "SUB_GROUP"));

            private static List<string> Names(Node node, string block) =>
                node.Children.Where(c => c.Keyword == block).SelectMany(c => c.Tokens).Select(t => t.Text).ToList();

            private Dictionary<string, T> Collect<T>(string keyword, Func<Node, T?> read, Func<T, string> key) where T : class
            {
                var items = new Dictionary<string, T>(StringComparer.Ordinal);
                foreach (var node in module.Children.Where(c => c.Keyword == keyword))
                {
                    T? item;
                    try { item = read(node); }
                    catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                    {
                        diagnostics.Add(new(node.Line, $"{keyword} {node.Name(0)} skipped: {e.Message}"));
                        continue;
                    }
                    if (item is null) continue;
                    if (!items.TryAdd(key(item), item))
                        diagnostics.Add(new(node.Line, $"{keyword} {key(item)} is defined twice; the first is kept."));
                }
                return items;
            }

            private (A2lByteOrder, IReadOnlyDictionary<A2lDataType, int>) ReadModCommon(Node? common)
            {
                if (common is null)
                {
                    diagnostics.Add(new(module.Line, "No MOD_COMMON: assuming BYTE_ORDER MSB_FIRST (the MPC5534 is big-endian)."));
                    return (A2lByteOrder.MsbFirst, DefaultAlignment);
                }
                var alignment = WithAlignment(common, DefaultAlignment);
                if (common.Option("BYTE_ORDER", 1) is not [var order])
                {
                    diagnostics.Add(new(common.Line, "MOD_COMMON has no BYTE_ORDER: assuming MSB_FIRST (the MPC5534 is big-endian)."));
                    return (A2lByteOrder.MsbFirst, alignment);
                }
                var parsed = ParseByteOrder(order.Text);
                if (parsed is null)
                {
                    diagnostics.Add(new(order.Line, $"BYTE_ORDER {order.Text} is not supported: assuming MSB_FIRST."));
                    return (A2lByteOrder.MsbFirst, alignment);
                }
                return (parsed.Value, alignment);
            }

            /// <summary>The alignments a node's ALIGNMENT_* options set, over the ones it inherits.</summary>
            private static Dictionary<A2lDataType, int> WithAlignment(Node node, IReadOnlyDictionary<A2lDataType, int> inherited)
            {
                var alignment = new Dictionary<A2lDataType, int>(inherited);
                foreach (var (keyword, types) in new (string, A2lDataType[])[]
                {
                    ("ALIGNMENT_BYTE", [A2lDataType.UByte, A2lDataType.SByte]),
                    ("ALIGNMENT_WORD", [A2lDataType.UWord, A2lDataType.SWord]),
                    ("ALIGNMENT_LONG", [A2lDataType.ULong, A2lDataType.SLong]),
                    ("ALIGNMENT_INT64", [A2lDataType.UInt64, A2lDataType.Int64]),
                    ("ALIGNMENT_FLOAT32_IEEE", [A2lDataType.Float32]),
                    ("ALIGNMENT_FLOAT64_IEEE", [A2lDataType.Float64]),
                })
                {
                    if (node.Option(keyword, 1) is [var value])
                        foreach (var type in types) alignment[type] = Math.Max(1, (int)Integer(value));
                }
                return alignment;
            }

            private static A2lByteOrder? ParseByteOrder(string text) => text switch
            {
                "MSB_FIRST" or "BIG_ENDIAN" => A2lByteOrder.MsbFirst,
                "MSB_LAST" or "LITTLE_ENDIAN" => A2lByteOrder.MsbLast,
                _ => null,
            };

            private static A2lByteOrder? ItemByteOrder(Node node, ref string? problem)
            {
                if (node.Option("BYTE_ORDER", 1) is not [var order]) return null;
                var parsed = ParseByteOrder(order.Text);
                if (parsed is null) problem ??= $"BYTE_ORDER {order.Text} is not supported.";
                return parsed;
            }

            private A2lMemorySegment? ReadSegment(Node node)
            {
                // Name LongIdentifier PrgType MemoryType Attribute Address Size Offset×5
                if (node.Tokens.Count < 7)
                {
                    diagnostics.Add(new(node.Line, $"MEMORY_SEGMENT {node.Name(0)} has too few parameters."));
                    return null;
                }
                return new A2lMemorySegment(node.Name(0), node.Name(2), node.Name(3), Address(node.Tokens[5]), Address(node.Tokens[6]));
            }

            private A2lCompuMethod ReadCompuMethod(Node node)
            {
                // Name LongIdentifier ConversionType Format Unit
                string name = node.Name(0), type = node.Name(2);
                string? problem = null;
                var kind = A2lConversionKind.Unsupported;
                double[] coefficients = [];
                var verbal = new Dictionary<long, string>();
                string? defaultText = null;
                A2lFormula? formula = null, inverse = null;
                switch (type)
                {
                    case "IDENTICAL":
                        kind = A2lConversionKind.Identical;
                        break;
                    case "LINEAR":
                        kind = A2lConversionKind.Linear;
                        if (node.Option("COEFFS_LINEAR", 2) is [var a, var b]) coefficients = [Number(a), Number(b)];
                        else problem = "LINEAR without COEFFS_LINEAR.";
                        break;
                    case "RAT_FUNC":
                        kind = A2lConversionKind.RationalFunction;
                        if (node.Option("COEFFS", 6) is { Length: 6 } c)
                        {
                            coefficients = c.Select(Number).ToArray();
                            if (coefficients[0] != 0 || coefficients[3] != 0)
                                problem = "RAT_FUNC with quadratic terms (a or d non-zero) is not supported.";
                            else if (coefficients[1] == 0 && coefficients[4] == 0)
                                problem = "RAT_FUNC with b = e = 0 has no physical value.";
                        }
                        else problem = "RAT_FUNC without six COEFFS.";
                        break;
                    case "TAB_VERB":
                        kind = A2lConversionKind.VerbalTable;
                        if (node.Option("COMPU_TAB_REF", 1) is [var reference] && _vtabs.TryGetValue(reference.Text, out var vtab))
                            (verbal, defaultText) = ReadVerbalTable(vtab);
                        else diagnostics.Add(new(node.Line, $"COMPU_METHOD {name}: its COMPU_VTAB is missing; raw values are shown."));
                        break;
                    case "FORM":
                        kind = A2lConversionKind.Formula;
                        var block = node.Children.FirstOrDefault(c => c.Keyword == "FORMULA");
                        if (block is null || block.Tokens.Count == 0 || block.Tokens[0].Kind != TokenKind.String)
                        {
                            problem = "FORM without a FORMULA.";
                            break;
                        }
                        if (!A2lFormula.TryParse(block.Tokens[0].Text, out formula, out var error)) problem = error;
                        if (block.Option("FORMULA_INV", 1) is [var inv])
                        {
                            if (A2lFormula.TryParse(inv.Text, out inverse, out var inverseError)) { }
                            else diagnostics.Add(new(block.Line, $"COMPU_METHOD {name}: FORMULA_INV ignored: {inverseError}"));
                        }
                        break;
                    default:
                        problem = $"Conversion type {type} is not supported.";
                        break;
                }
                return new A2lCompuMethod
                {
                    Name = name, LongIdentifier = node.Name(1), Kind = kind, Format = node.Name(3), Unit = node.Name(4),
                    Coefficients = coefficients, VerbalTable = verbal, DefaultText = defaultText,
                    Formula = formula, InverseFormula = inverse, Problem = problem,
                };
            }

            private static (Dictionary<long, string>, string?) ReadVerbalTable(Node vtab)
            {
                // Name LongIdentifier ConversionType NumberValuePairs (InVal OutVal)×n
                var table = new Dictionary<long, string>();
                int pairs = (int)Integer(vtab.Tokens[3]);
                for (int i = 0; i < pairs && 5 + 2 * i < vtab.Tokens.Count; i++)
                    table[(long)Number(vtab.Tokens[4 + 2 * i])] = vtab.Tokens[5 + 2 * i].Text;
                return (table, vtab.Option("DEFAULT_VALUE", 1)?[0].Text);
            }

            private static A2lRecordLayout ReadRecordLayout(Node node, IReadOnlyDictionary<A2lDataType, int> moduleAlignment)
            {
                var alignment = WithAlignment(node, moduleAlignment);
                string? problem = null;
                A2lLayoutItem? values = null, axisX = null, axisY = null;
                var mode = A2lIndexMode.RowDir;
                int? fixX = null, fixY = null;
                var tokens = node.Tokens;
                for (int i = 1; i < tokens.Count; i++)
                {
                    if (tokens[i].Kind != TokenKind.Word) continue;
                    switch (tokens[i].Text)
                    {
                        case "FNC_VALUES": // Position Datatype IndexMode AddressType
                            values = Item(i, out var fncType);
                            mode = tokens[i + 3].Text switch
                            {
                                "ROW_DIR" => A2lIndexMode.RowDir,
                                "COLUMN_DIR" => A2lIndexMode.ColumnDir,
                                var other => Refuse($"FNC_VALUES index mode {other}", A2lIndexMode.RowDir),
                            };
                            if (tokens[i + 4].Text != "DIRECT") problem ??= $"FNC_VALUES address type {tokens[i + 4].Text} is not supported.";
                            i += 4;
                            break;
                        case "AXIS_PTS_X":
                        case "AXIS_PTS_Y": // Position Datatype IndexIncr Addressing
                            var axis = Item(i, out _);
                            if (tokens[i].Text == "AXIS_PTS_X") axisX = axis; else axisY = axis;
                            if (tokens[i + 3].Text != "INDEX_INCR") problem ??= $"{tokens[i].Text} with {tokens[i + 3].Text} is not supported.";
                            if (tokens[i + 4].Text != "DIRECT") problem ??= $"{tokens[i].Text} address type {tokens[i + 4].Text} is not supported.";
                            i += 4;
                            break;
                        case "FIX_NO_AXIS_PTS_X": fixX = (int)Integer(tokens[++i]); break;
                        case "FIX_NO_AXIS_PTS_Y": fixY = (int)Integer(tokens[++i]); break;
                        case "ALIGNMENT_BYTE" or "ALIGNMENT_WORD" or "ALIGNMENT_LONG" or "ALIGNMENT_INT64"
                            or "ALIGNMENT_FLOAT32_IEEE" or "ALIGNMENT_FLOAT64_IEEE":
                            i++;
                            break;
                        case "STATIC_RECORD_LAYOUT" or "STATIC_ADDRESS_OFFSETS":
                            break;
                        default:
                            if (IsKeyword(tokens[i].Text)) problem ??= $"{tokens[i].Text} is not supported.";
                            break;
                    }
                }
                if (values is null && axisX is null) problem ??= "it has neither FNC_VALUES nor AXIS_PTS_X.";
                return new A2lRecordLayout
                {
                    Name = node.Name(0), FncValues = values, IndexMode = mode, AxisPtsX = axisX, AxisPtsY = axisY,
                    FixNoAxisPtsX = fixX, FixNoAxisPtsY = fixY, Alignment = alignment,
                    Problem = problem is null ? null : $"RECORD_LAYOUT {node.Name(0)}: {problem}",
                };

                A2lLayoutItem Item(int at, out A2lDataType type)
                {
                    var parsed = A2lDataTypes.Parse(tokens[at + 2].Text);
                    if (parsed is null) problem ??= $"data type {tokens[at + 2].Text} is not supported.";
                    type = parsed ?? A2lDataType.UByte;
                    return new A2lLayoutItem((int)Integer(tokens[at + 1]), type);
                }

                T Refuse<T>(string what, T fallback)
                {
                    problem ??= $"{what} is not supported.";
                    return fallback;
                }
            }

            // Record layout items are upper-case keywords; anything else on the line is a parameter.
            private static bool IsKeyword(string word) =>
                word.Length > 2 && word.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_') && char.IsUpper(word[0]);

            private A2lMeasurement ReadMeasurement(Node node)
            {
                // Name LongIdentifier Datatype Conversion Resolution Accuracy LowerLimit UpperLimit
                string? problem = null;
                var type = A2lDataTypes.Parse(node.Name(2));
                if (type is null) problem = $"data type {node.Name(2)} is not supported.";
                var byteOrder = ItemByteOrder(node, ref problem);
                ulong? mask = node.Option("BIT_MASK", 1) is [var m] ? (ulong)Integer(m) : null;
                if (node.Option("ECU_ADDRESS_EXTENSION", 1) is [var ext] && Integer(ext) != 0)
                    problem ??= "ECU_ADDRESS_EXTENSION is not supported.";
                if (node.Option("ARRAY_SIZE", 1) is not null || node.Option("MATRIX_DIM", 1) is not null)
                    problem ??= "array measurements are not supported.";
                var inputs = node.Children.FirstOrDefault(c => c.Keyword == "VIRTUAL")?.Tokens.Select(t => t.Text).ToList() ?? [];
                uint? address = node.Option("ECU_ADDRESS", 1) is [var a] ? Address(a) : null;
                if (address is null && inputs.Count == 0) problem ??= "no ECU_ADDRESS.";
                return new A2lMeasurement
                {
                    Name = node.Name(0), LongIdentifier = node.Name(1), DataType = type ?? A2lDataType.UByte, Conversion = node.Name(3),
                    LowerLimit = Number(node.Tokens[6]), UpperLimit = Number(node.Tokens[7]), Address = address,
                    VirtualInputs = inputs, BitMask = mask, ByteOrder = byteOrder, Problem = problem,
                };
            }

            private A2lAxisPts ReadAxisPts(Node node)
            {
                // Name LongIdentifier Address InputQuantity DepositAttr MaxDiff Conversion MaxAxisPoints LowerLimit UpperLimit
                string? problem = null;
                var byteOrder = ItemByteOrder(node, ref problem);
                CommonProblems(node, ref problem);
                return new A2lAxisPts
                {
                    Name = node.Name(0), LongIdentifier = node.Name(1), Address = Address(node.Tokens[2]),
                    InputQuantity = InputQuantity(node.Name(3)), RecordLayout = node.Name(4), Conversion = node.Name(6),
                    MaxAxisPoints = (int)Integer(node.Tokens[7]), LowerLimit = Number(node.Tokens[8]), UpperLimit = Number(node.Tokens[9]),
                    ReadOnly = node.Has("READ_ONLY"), ByteOrder = byteOrder, Problem = problem,
                };
            }

            private A2lCharacteristic ReadCharacteristic(Node node)
            {
                // Name LongIdentifier Type Address Deposit MaxDiff Conversion LowerLimit UpperLimit
                string? problem = null;
                var kind = node.Name(2) switch
                {
                    "VALUE" => A2lCharacteristicKind.Value,
                    "VAL_BLK" => A2lCharacteristicKind.ValBlk,
                    "CURVE" => A2lCharacteristicKind.Curve,
                    "MAP" => A2lCharacteristicKind.Map,
                    "ASCII" => A2lCharacteristicKind.Ascii,
                    var other => Unsupported(other),
                };
                int number = 0;
                if (node.Option("NUMBER", 1) is [var n]) number = (int)Integer(n);
                else if (MatrixDim(node) is { } dims) number = dims;
                if (kind is A2lCharacteristicKind.ValBlk or A2lCharacteristicKind.Ascii && number <= 0)
                    problem ??= $"{node.Name(2)} without NUMBER or MATRIX_DIM.";
                var byteOrder = ItemByteOrder(node, ref problem);
                CommonProblems(node, ref problem);
                if (node.Children.Any(c => c.Keyword == "VIRTUAL_CHARACTERISTIC"))
                    problem ??= "VIRTUAL_CHARACTERISTIC values are computed, not stored.";
                var axes = node.Children.Where(c => c.Keyword == "AXIS_DESCR").Select(ReadAxisDescr).ToList();
                int expectedAxes = kind switch { A2lCharacteristicKind.Curve => 1, A2lCharacteristicKind.Map => 2, _ => 0 };
                if (axes.Count != expectedAxes)
                    problem ??= $"{node.Name(2)} has {axes.Count} AXIS_DESCR, expected {expectedAxes}.";
                return new A2lCharacteristic
                {
                    Name = node.Name(0), LongIdentifier = node.Name(1), Kind = kind, Address = Address(node.Tokens[3]),
                    RecordLayout = node.Name(4), Conversion = node.Name(6), LowerLimit = Number(node.Tokens[7]),
                    UpperLimit = Number(node.Tokens[8]), Number = number, Axes = axes, ReadOnly = node.Has("READ_ONLY"),
                    ByteOrder = byteOrder, Problem = problem,
                };

                A2lCharacteristicKind Unsupported(string type)
                {
                    problem = $"characteristic type {type} is not supported.";
                    return A2lCharacteristicKind.ValBlk;
                }
            }

            private static int? MatrixDim(Node node)
            {
                int index = node.Tokens.FindIndex(t => t.Kind == TokenKind.Word && t.Text == "MATRIX_DIM");
                if (index < 0) return null;
                int product = 1, count = 0;
                for (int i = index + 1; i < node.Tokens.Count && count < 3 && TryInteger(node.Tokens[i], out long d); i++, count++)
                    product *= (int)Math.Max(1, d);
                return count == 0 ? null : product;
            }

            private A2lAxisDescr ReadAxisDescr(Node node)
            {
                // Attribute InputQuantity Conversion MaxAxisPoints LowerLimit UpperLimit
                string? problem = null;
                var kind = node.Name(0) switch
                {
                    "COM_AXIS" => A2lAxisKind.Common,
                    "STD_AXIS" => A2lAxisKind.Standard,
                    "FIX_AXIS" => A2lAxisKind.Fixed,
                    var other => Refuse(other),
                };
                int max = (int)Integer(node.Tokens[3]);
                IReadOnlyList<double>? fixedPoints = null;
                if (node.Option("FIX_AXIS_PAR", 3) is [var off, var shift, var count])
                    fixedPoints = Enumerable.Range(0, (int)Integer(count)).Select(i => Number(off) + i * Math.Pow(2, Integer(shift))).ToList();
                else if (node.Option("FIX_AXIS_PAR_DIST", 3) is [var off2, var dist, var count2])
                    fixedPoints = Enumerable.Range(0, (int)Integer(count2)).Select(i => Number(off2) + i * Number(dist)).ToList();
                else if (node.Children.FirstOrDefault(c => c.Keyword == "FIX_AXIS_PAR_LIST") is { } list)
                    fixedPoints = list.Tokens.Select(Number).ToList();
                if (kind == A2lAxisKind.Fixed && fixedPoints is null) problem ??= "FIX_AXIS without FIX_AXIS_PAR.";
                string? reference = node.Option("AXIS_PTS_REF", 1)?[0].Text;
                if (kind == A2lAxisKind.Common && reference is null) problem ??= "COM_AXIS without AXIS_PTS_REF.";
                if (node.Option("CURVE_AXIS_REF", 1) is not null) problem ??= "CURVE_AXIS_REF is not supported.";
                if (node.Option("DEPOSIT", 1) is [var deposit] && deposit.Text != "ABSOLUTE") problem ??= $"DEPOSIT {deposit.Text} is not supported.";
                if (node.Option("BYTE_ORDER", 1) is [var order] && ParseByteOrder(order.Text) is null)
                    problem ??= $"BYTE_ORDER {order.Text} is not supported.";
                return new A2lAxisDescr
                {
                    Kind = kind, InputQuantity = InputQuantity(node.Name(1)), Conversion = node.Name(2), MaxAxisPoints = max,
                    LowerLimit = Number(node.Tokens[4]), UpperLimit = Number(node.Tokens[5]), AxisPtsRef = reference,
                    FixedRawPoints = fixedPoints, ReadOnly = node.Has("READ_ONLY"), Problem = problem,
                };

                A2lAxisKind Refuse(string attribute)
                {
                    problem = $"axis type {attribute} is not supported.";
                    return A2lAxisKind.Standard;
                }
            }

            private static void CommonProblems(Node node, ref string? problem)
            {
                if (node.Option("ECU_ADDRESS_EXTENSION", 1) is [var ext] && Integer(ext) != 0)
                    problem ??= "ECU_ADDRESS_EXTENSION is not supported.";
                if (node.Option("BIT_MASK", 1) is not null) problem ??= "BIT_MASK is not supported for calibration items.";
                if (node.Option("DEPOSIT", 1) is [var deposit] && deposit.Text != "ABSOLUTE")
                    problem ??= $"DEPOSIT {deposit.Text} is not supported.";
            }

            private static string? InputQuantity(string name) => name == "NO_INPUT_QUANTITY" ? null : name;
        }

        // ── Numbers ──────────────────────────────────────────────────────────────────────────────────────────

        private static bool TryInteger(Token token, out long value)
        {
            var text = token.Text;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == Math.Floor(d))
            {
                value = (long)d;
                return true;
            }
            return false;
        }

        private static long Integer(Token token) => TryInteger(token, out long value) ? value
            : throw new FormatException($"'{token.Text}' (line {token.Line}) is not an integer.");

        private static uint Address(Token token)
        {
            long value = Integer(token);
            return value is >= 0 and <= uint.MaxValue ? (uint)value
                : throw new FormatException($"'{token.Text}' (line {token.Line}) is not a 32-bit address.");
        }

        private static double Number(Token token)
        {
            if (token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Integer(token);
            return double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value
                : throw new FormatException($"'{token.Text}' (line {token.Line}) is not a number.");
        }
    }
}
