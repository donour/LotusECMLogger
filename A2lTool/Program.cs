using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;

const string Usage = """
    Usage:
      A2lTool generate <build>... [--out-dir <dir>] [--force]
          Drafts <build>.a2l from the build's symbol catalog. Existing files are kept unless --force: they may
          carry hand edits. Axis inputs come from <out-dir>/<build>.inputs.csv when that file exists.
      A2lTool extract-inputs <decompile.c> <build> [--aliases <older catalog.csv>] [--out-dir <dir>] [--force]
          Drafts <build>.inputs.csv: each axis's input as its lookup calls pass it, where every call site agrees
          and the input is plain RAM variables. Review it against the decompile, then regenerate the A2L.
          --aliases: the catalog the decompile was named from, when the catalog has renamed variables since;
          each decompile name is read as whatever the catalog now calls its address.
      A2lTool check <file.a2l>... [--image <calibration .bin or .CRP>]
          Parses each file as the app will and lists what is unavailable or read-only, and why. With --image, also
          checks the A2L against that calibration (EPK, segment) and lists axes whose points decrease in it.
    Builds:
    """;

try
{
    if (args.Length < 2) return Fail(Usage + string.Join(", ", CcpBuildProfile.Known.Select(b => b.Build)));
    return args[0] switch
    {
        "generate" => Generate(args[1..]),
        "check" => Check(args[1..]),
        "extract-inputs" => ExtractInputs(args[1..]),
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
        string inputsPath = Path.Combine(outDir, build.Build + ".inputs.csv");
        var inputs = File.Exists(inputsPath) ? AxisInputs.Load(inputsPath) : null;
        var result = A2lGenerator.Generate(catalog, build, inputs);
        File.WriteAllText(path, result.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Console.WriteLine($"{path}: {result.Maps} maps, {result.Curves} curves, {result.AxisPts} axes, {result.Blocks} blocks, " +
            $"{result.Values} values, {result.Measurements} measurements, {result.Inputs} axis inputs, {result.Warnings.Count} warning(s).");
        foreach (var warning in result.Warnings) Console.WriteLine("  " + warning);
    }
    return failures == 0 ? 0 : 1;
}

static int ExtractInputs(string[] args)
{
    string outDir = ".";
    bool force = false;
    string? aliasesPath = null;
    var positional = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--out-dir" when i + 1 < args.Length: outDir = args[++i]; break;
            case "--aliases" when i + 1 < args.Length: aliasesPath = args[++i]; break;
            case "--force": force = true; break;
            default: positional.Add(args[i]); break;
        }
    }
    if (positional.Count != 2) return Fail(Usage);
    var build = CcpBuildProfile.Known.FirstOrDefault(b => b.Build == positional[1]);
    var catalog = build is null ? null : SymbolCatalogLoader.LoadByName(build.Catalog);
    if (build is null || catalog is null) return Fail($"{positional[1]}: unknown build or no symbol catalog.");
    string path = Path.Combine(outDir, build.Build + ".inputs.csv");
    if (File.Exists(path) && !force) return Fail($"{path} exists; not overwritten (use --force): it may hold your review.");

    // The axes are the generated A2L's AXIS_PTS, so every binding names one.
    var axes = A2lParser.Parse(A2lGenerator.Generate(catalog, build).Text).AxisPts.Keys.ToHashSet(StringComparer.Ordinal);
    // An older catalog's names, for a decompile named before the catalog's renames: each name is read by address.
    var aliases = aliasesPath is null ? null : AxisInputs.Aliases(SymbolCatalogLoader.LoadFromFile(aliasesPath), catalog);
    var (bound, skipped) = AxisInputs.Extract(File.ReadAllText(positional[0]), axes, AxisInputs.Variables(catalog), aliases);
    Directory.CreateDirectory(outDir);
    File.WriteAllText(path, AxisInputs.Format(bound, Path.GetFileName(positional[0])));
    Console.WriteLine($"{path}: {bound.Count} of {axes.Count} axes bound; {skipped.Count} looked up but left unbound:");
    foreach (var s in skipped) Console.WriteLine($"  {s.Axis}: {s.Reason}");
    return 0;
}

static int Check(string[] args)
{
    CalibrationImage? image = null;
    var files = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--image" && i + 1 < args.Length) image = CalibrationImage.Load(args[++i]);
        else files.Add(args[i]);
    }
    int failures = 0;
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
        if (image is not null) failures += CheckImage(resolved, image);
    }
    return failures == 0 ? 0 : 1;
}

// Stock axes must not decrease (the lookups walk them upwards), so a decreasing axis in a real calibration points at
// a wrong address, size or byte order in the A2L, or at an axis the firmware does not use.
static int CheckImage(A2lResolvedModule resolved, CalibrationImage image)
{
    Console.WriteLine($"  image {image.SourcePath} ({image.Format}): {image.CalibrationIdText}");
    var problems = image.CheckAgainst(resolved);
    foreach (var problem in problems) Console.WriteLine($"  image: {problem}");
    if (problems.Count > 0) return 1;
    var axes = resolved.Items.Values.Where(i => i.Kind == A2lItemKind.AxisPts).OrderBy(i => i.Address).ToList();
    int decreasing = 0;
    foreach (var axis in axes)
    {
        var points = Enumerable.Range(0, axis.CountX).Select(i => image.ReadRaw(axis, i)).ToArray();
        if (!points.Zip(points.Skip(1)).Any(p => p.Second < p.First)) continue;
        decreasing++;
        Console.WriteLine($"  decreasing axis {axis.Name} @0x{axis.Address:X8}: raw [{string.Join(", ", points)}]");
    }
    Console.WriteLine($"  axes: {axes.Count - decreasing} of {axes.Count} non-decreasing in this calibration");
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
