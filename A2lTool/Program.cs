using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

const string Usage = """
    Usage:
      A2lTool generate <build>... [--out-dir <dir>] [--force]
          Drafts <build>.a2l from the build's symbol catalog. Existing files are kept unless --force:
          they may carry hand-added InputQuantity bindings.
      A2lTool check <file.a2l>...
          Parses each file as the app will and lists what is unavailable or read-only, and why.
    Builds:
    """;

try
{
    if (args.Length < 2) return Fail(Usage + string.Join(", ", CcpBuildProfile.Known.Select(b => b.Build)));
    return args[0] switch
    {
        "generate" => Generate(args[1..]),
        "check" => Check(args[1..]),
        _ => Fail(Usage + string.Join(", ", CcpBuildProfile.Known.Select(b => b.Build))),
    };
}
catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
{
    return Fail(e.Message);
}

static int Generate(string[] args)
{
    string outDir = ".";
    bool force = false;
    var builds = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--out-dir" when i + 1 < args.Length: outDir = args[++i]; break;
            case "--force": force = true; break;
            default: builds.Add(args[i]); break;
        }
    }
    Directory.CreateDirectory(outDir);
    int failures = 0;
    foreach (var name in builds)
    {
        var build = CcpBuildProfile.Known.FirstOrDefault(b => b.Build == name);
        var catalog = build is null ? null : SymbolCatalogLoader.LoadByName(build.Catalog);
        if (build is null || catalog is null)
        {
            Console.Error.WriteLine($"{name}: {(build is null ? "unknown build" : $"no symbol catalog {build.Catalog}")}.");
            failures++;
            continue;
        }
        string path = Path.Combine(outDir, build.Build + ".a2l");
        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"{path} exists; not overwritten (use --force).");
            failures++;
            continue;
        }
        var result = A2lGenerator.Generate(catalog, build);
        File.WriteAllText(path, result.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Console.WriteLine($"{path}: {result.Maps} maps, {result.Curves} curves, {result.AxisPts} axes, {result.Blocks} blocks, " +
            $"{result.Values} values, {result.Measurements} measurements, {result.Warnings.Count} warning(s).");
        foreach (var warning in result.Warnings) Console.WriteLine("  " + warning);
    }
    return failures == 0 ? 0 : 1;
}

static int Check(string[] files)
{
    foreach (var file in files)
    {
        var module = A2lParser.ParseFile(file);
        var resolved = A2lLayout.Resolve(module);
        var items = resolved.Items.Values;
        Console.WriteLine($"{file}: module {module.Name}, EPK \"{module.Epk}\" at 0x{module.EpkAddress:X8}, {module.ByteOrder}, " +
            $"calibration segment {(module.CalibrationSegment is { } s ? $"0x{s.Address:X8}+0x{s.Size:X}" : "none")}");
        Console.WriteLine($"  {items.Count(i => i.Kind == A2lItemKind.Map)} maps, {items.Count(i => i.Kind == A2lItemKind.Curve)} curves, " +
            $"{items.Count(i => i.Kind == A2lItemKind.AxisPts)} axes, {items.Count(i => i.Kind is A2lItemKind.ValBlk or A2lItemKind.Value or A2lItemKind.Ascii)} values/blocks, " +
            $"{module.Measurements.Count} measurements; {items.Count(i => !i.IsEditable)} read-only, {resolved.Unavailable.Count} unavailable");
        int bound = items.Sum(i => i.Axes.Count(a => a.InputQuantity is not null));
        int axes = items.Sum(i => i.Axes.Count);
        Console.WriteLine($"  axis inputs bound: {bound} of {axes}");
        foreach (var axis in items.SelectMany(i => i.Axes.Select(a => (Item: i, Axis: a))).Where(x => x.Axis.InputQuantity is { } q && !module.Measurements.ContainsKey(q)))
            Console.WriteLine($"  {axis.Item.Name}: InputQuantity {axis.Axis.InputQuantity} is not a MEASUREMENT");
        if (module.CalibrationSegment is { } segment)
            foreach (var item in items)
                foreach (var (address, length) in item.Spans())
                    if (!segment.Contains(address, length))
                        Console.WriteLine($"  {item.Name}: 0x{address:X8}+{length} is outside the calibration segment");
        foreach (var d in module.Diagnostics) Console.WriteLine($"  {d}");
        foreach (var u in resolved.Unavailable) Console.WriteLine($"  unavailable {u.Name}: {u.Reason}");
        foreach (var r in items.Where(i => !i.IsEditable).OrderBy(i => i.Name, StringComparer.Ordinal))
            Console.WriteLine($"  read-only {r.Name}: {r.ReadOnlyReason}");
    }
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
