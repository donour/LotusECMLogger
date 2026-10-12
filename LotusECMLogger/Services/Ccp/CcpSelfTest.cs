using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services.Ccp;

public enum CcpCheckStatus { Pass, Warn, Fail, Skipped }

public sealed record CcpCheckResult(string Name, CcpCheckStatus Status, string Detail, TimeSpan Duration);

public sealed class CcpSelfTestOptions
{
    /// <summary>How long DAQ runs on every event channel; rates come from adapter timestamps, so 1 Hz needs ≥ 3 s.</summary>
    public TimeSpan DaqWindow { get; init; } = TimeSpan.FromSeconds(5);
    public int LatencySamples { get; init; } = 50;
    /// <summary>Bytes read and checksummed when the build (and so its calibration region) is unknown.</summary>
    public int UnknownBuildReadLength { get; init; } = 4096;
    /// <summary>Allowed relative error of a measured event rate.</summary>
    public double RateTolerance { get; init; } = 0.2;
    public double LatencyWarnMilliseconds { get; init; } = 20;
    /// <summary>Above this, the checksum timeout allowance (20 ms per 256-byte pass by default) is too tight.</summary>
    public double ChecksumPassWarnMilliseconds { get; init; } = 20;
    /// <summary>Profiles identification may match; the built-in ones by default.</summary>
    public IReadOnlyList<CcpBuildProfile>? Candidates { get; init; }
    /// <summary>Optional engine-speed symbol for the build, reported for context.</summary>
    public CcpEngineSpeedSymbol? EngineSpeed { get; init; }
}

public sealed record CcpSelfTestReport(DateTime Started, TimeSpan Duration, string? Build,
    IReadOnlyList<CcpCheckResult> Checks, CcpMasterStatistics Statistics)
{
    public CcpCheckStatus Overall =>
        Checks.Any(c => c.Status == CcpCheckStatus.Fail) ? CcpCheckStatus.Fail
        : Checks.Any(c => c.Status == CcpCheckStatus.Warn) ? CcpCheckStatus.Warn
        : CcpCheckStatus.Pass;

    /// <summary>A plain-text report; <paramref name="header"/> lines (app version, adapter…) go at the top.</summary>
    public string Format(IEnumerable<string>? header = null)
    {
        var text = new StringBuilder();
        text.AppendLine("CCP self-test report");
        foreach (var line in header ?? []) text.AppendLine(line);
        text.AppendLine(CultureInfo.InvariantCulture, $"Started:  {Started:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Duration: {Duration.TotalSeconds:F1} s");
        text.AppendLine($"Build:    {Build ?? "not recognised"}");
        text.AppendLine($"Overall:  {Overall.ToString().ToUpperInvariant()}");
        text.AppendLine();
        foreach (var check in Checks)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"[{check.Status.ToString().ToUpperInvariant(),-7}] {check.Name} ({check.Duration.TotalSeconds:F1} s): {check.Detail}");
        var s = Statistics;
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Session: {s.Commands} commands, {s.Replies} replies, {s.ErrorReplies} error replies, {s.Timeouts} timeouts, " +
            $"{s.StrayReplies} stray, {s.MalformedFrames} malformed, {s.DaqFrames} DAQ frames ({s.UnroutedDaqFrames} unrouted).");
        if (s.MeanLatencyMs is { } mean)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Latency: mean {mean:F2} ms, p95 {s.P95LatencyMs:F2} ms, max {s.MaxLatencyMs:F2} ms.");
        return text.ToString();
    }
}

/// <summary>
/// A read-only check of a connected CCP session against what this firmware should do, for running on a car and
/// sharing the result. It never writes ECU memory. The DAQ check needs every list free, so it is skipped while
/// another consumer (e.g. the high-speed logger) holds lists.
/// </summary>
public static class CcpSelfTest
{
    public const string DaqOwner = "Self-test";

    public static async Task<CcpSelfTestReport> RunAsync(CcpMaster master, CcpSelfTestOptions? options = null,
        IProgress<CcpCheckResult>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        options ??= new CcpSelfTestOptions();
        var started = DateTime.Now;
        var total = Stopwatch.StartNew();
        var checks = new List<CcpCheckResult>();
        var context = new Context(master, options);

        var steps = new (string Name, Func<Context, CancellationToken, Task<(CcpCheckStatus, string)>> Run)[]
        {
            ("Session", SessionAsync),
            ("Identification", IdentificationAsync),
            ("Variable table", VariableTableAsync),
            ("Command latency", LatencyAsync),
            ("Memory read", ReadAsync),
            ("Checksum", ChecksumAsync),
            ("DAQ event rates and concurrent access", DaqAsync),
            ("Engine speed", EngineSpeedAsync),
        };
        bool abandoned = false;
        foreach (var (name, run) in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            CcpCheckResult result;
            if (abandoned) result = new(name, CcpCheckStatus.Skipped, "The session could not be recovered.", TimeSpan.Zero);
            else
            {
                try
                {
                    var (status, detail) = await run(context, cancellationToken).ConfigureAwait(false);
                    result = new(name, status, detail, watch.Elapsed);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    result = new(name, CcpCheckStatus.Fail, ex.Message, watch.Elapsed);
                    abandoned = !await RecoverAsync(master, cancellationToken).ConfigureAwait(false);
                }
            }
            checks.Add(result);
            progress?.Report(result);
        }

        var counters = Counters(master.GetStatistics());
        checks.Add(counters);
        progress?.Report(counters);
        return new CcpSelfTestReport(started, total.Elapsed, context.Identification?.Build?.Build, checks, master.GetStatistics());
    }

    private sealed class Context(CcpMaster master, CcpSelfTestOptions options)
    {
        public CcpMaster Master { get; } = master;
        public CcpSelfTestOptions Options { get; } = options;
        public CcpBuildIdentification? Identification { get; set; }
        public byte[]? Region { get; set; }
        public uint RegionAddress { get; set; }
        public double? MeanLatencyMs { get; set; }
    }

    private static async Task<bool> RecoverAsync(CcpMaster master, CancellationToken ct)
    {
        if (master.State == CcpSessionState.Connected) return true;
        if (master.State is not CcpSessionState.Uncertain) return false;
        try { await master.ConnectAsync(ct).ConfigureAwait(false); return true; }
        catch (Exception) { return false; }
    }

    private static Task<(CcpCheckStatus, string)> SessionAsync(Context c, CancellationToken ct)
    {
        var target = c.Master.Target;
        if (c.Master.State != CcpSessionState.Connected)
            throw new InvalidOperationException($"The session is {c.Master.State}; connect first.");
        return Task.FromResult((CcpCheckStatus.Pass, $"Connected to {target.Name} (CRO 0x{target.CroId:X3}, DTO 0x{target.DtoId:X3})."));
    }

    private static async Task<(CcpCheckStatus, string)> IdentificationAsync(Context c, CancellationToken ct)
    {
        var id = c.Identification = await c.Master.IdentifyAsync(c.Options.Candidates, ct).ConfigureAwait(false);
        var fields = id.Slave.Fields;
        var problems = new List<string>();
        if (id.Slave.SlaveId != c.Master.Target.SlaveId) problems.Add($"slave ID {id.Slave.SlaveId}, expected {c.Master.Target.SlaveId}");
        if ((fields.AvailableResources & 0x03) != 0x03) problems.Add($"resources 0x{fields.AvailableResources:X2} lack CAL+DAQ");
        if (fields.ProtectedResources != 0) problems.Add($"resources protected 0x{fields.ProtectedResources:X2}");
        string detail = $"{id.Slave.SlaveId}, CCP {id.Slave.VersionMajor}.{id.Slave.VersionMinor}, driver 0x{fields.DriverVersion:X2}, " +
            (id.Build is { } build
                ? $"build {build.Build}{(id.CalibrationDescription is { } d ? $" \"{d}\"" : "")}" +
                  (build.SymbolEntriesAddress is null ? "" : id.RegistryHeaderReadable ? $", program {id.RegistryBuildId}" : ", registry header unreadable")
                : "build not recognised");
        if (problems.Count > 0) return (CcpCheckStatus.Fail, $"{detail}; {string.Join("; ", problems)}.");
        bool warn = id.Build is null || id.Build.SymbolEntriesAddress is not null && !id.RegistryHeaderReadable;
        return (warn ? CcpCheckStatus.Warn : CcpCheckStatus.Pass, detail + ".");
    }

    private static async Task<(CcpCheckStatus, string)> VariableTableAsync(Context c, CancellationToken ct)
    {
        if (c.Identification?.Build is not { SymbolEntriesAddress: not null } build)
            return (CcpCheckStatus.Skipped, "No verified variable-table location for this build.");
        var registry = await c.Master.ReadSymbolRegistryAsync(build, cancellationToken: ct).ConfigureAwait(false);
        return (CcpCheckStatus.Pass, $"{registry.Entries.Count} entries ({registry.Entries.Count(e => e.IsRam)} in RAM), " +
            $"header \"{registry.BuildId}\" {registry.Version}, terminator found.");
    }

    private static async Task<(CcpCheckStatus, string)> LatencyAsync(Context c, CancellationToken ct)
    {
        var samples = new List<double>(c.Options.LatencySamples);
        for (int i = 0; i < c.Options.LatencySamples; i++)
        {
            var watch = Stopwatch.StartNew();
            await c.Master.GetVersionAsync(ct).ConfigureAwait(false);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        double mean = samples.Average(), p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        c.MeanLatencyMs = mean;
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"{samples.Count} GET_CCP_VERSION round trips: min {samples[0]:F2}, mean {mean:F2}, p95 {p95:F2}, max {samples[^1]:F2} ms.");
        return mean > c.Options.LatencyWarnMilliseconds
            ? (CcpCheckStatus.Warn, string.Create(CultureInfo.InvariantCulture,
                $"{detail} Slow: reading a 27 KB calibration (about 5,600 UPLOADs) would take about {mean * 5_600 / 1000:F0} s."))
            : (CcpCheckStatus.Pass, detail);
    }

    private static async Task<(CcpCheckStatus, string)> ReadAsync(Context c, CancellationToken ct)
    {
        var build = c.Identification?.Build;
        uint address = build?.CalibrationBase ?? 0x40000000;
        int length = build is null ? c.Options.UnknownBuildReadLength : checked((int)build.CalibrationSize);
        var watch = Stopwatch.StartNew();
        c.Region = await c.Master.ReadAsync(address, length, cancellationToken: ct).ConfigureAwait(false);
        c.RegionAddress = address;
        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 1e-6);
        return (CcpCheckStatus.Pass, string.Create(CultureInfo.InvariantCulture,
            $"{length:N0} bytes from 0x{address:X8} ({(build is null ? "start of RAM" : "calibration RAM")}) in {seconds:F2} s, " +
            $"{length / seconds:N0} B/s."));
    }

    private static async Task<(CcpCheckStatus, string)> ChecksumAsync(Context c, CancellationToken ct)
    {
        if (c.Region is not { } region) return (CcpCheckStatus.Skipped, "No region was read.");
        var watch = Stopwatch.StartNew();
        ushort ecu = await c.Master.ChecksumAsync(c.RegionAddress, region.Length, cancellationToken: ct).ConfigureAwait(false);
        // Elapsed time includes SET_MTA and BUILD_CHKSUM round trips; only many passes give a usable per-pass time.
        int passes = (region.Length + 255) / 256;
        double work = Math.Max(0, watch.Elapsed.TotalMilliseconds - 2 * (c.MeanLatencyMs ?? 0));
        double perPass = work / passes;
        bool timed = passes >= 8;
        ushort local = CcpFrame.AdditiveChecksum(region);
        string timing = timed
            ? string.Create(CultureInfo.InvariantCulture, $", about {perPass:F2} ms per 256-byte pass.")
            : "; too short to time the passes.";
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"ECU 0x{ecu:X4}, read data 0x{local:X4}; {watch.Elapsed.TotalMilliseconds:F0} ms for {passes} pass(es){timing}");
        if (ecu != local) return (CcpCheckStatus.Fail, detail + " The ECU's sum disagrees with the bytes it returned.");
        return timed && perPass > c.Options.ChecksumPassWarnMilliseconds
            ? (CcpCheckStatus.Warn, detail + " Raise CcpMasterOptions.ChecksumPassTimeout above this.")
            : (CcpCheckStatus.Pass, detail);
    }

    private static async Task<(CcpCheckStatus, string)> DaqAsync(Context c, CancellationToken ct)
    {
        var target = c.Master.Target;
        var held = c.Master.GetDaqAllocations();
        if (held.Count > 0)
            return (CcpCheckStatus.Skipped, $"{held.Count} DAQ list(s) in use by {string.Join(", ", held.Select(a => a.Owner).Distinct())}; " +
                "stop them to measure every event channel.");

        uint address = c.Identification?.Build?.CalibrationBase ?? 0x40000000;
        var events = target.EventRatesHz.Keys.Order().Take(target.DaqListCount).ToList();
        var requests = events.Select(e => new CcpDaqListRequest(e, 1, [[new CcpDaqElement(address, 1)]])).ToList();
        var subscription = await c.Master.StartDaqAsync(requests, DaqOwner, ct).ConfigureAwait(false);
        var first = new long?[events.Count];
        var last = new long[events.Count];
        var counts = new int[events.Count];
        long overruns = 0;
        var unwrap = new long[events.Count];
        var lastRaw = new uint[events.Count];
        long samplesBeforeRead = 0, samplesDuringRead = 0;
        bool reading = false, readDone = false;

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(c.Options.DaqWindow);
        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var sample in subscription.Stream.Reader.ReadAllAsync(window.Token).ConfigureAwait(false))
                {
                    int i = sample.List;
                    if (first[i] is null) { first[i] = 0; lastRaw[i] = sample.TimestampMicroseconds; }
                    unwrap[i] += unchecked((int)(sample.TimestampMicroseconds - lastRaw[i]));
                    lastRaw[i] = sample.TimestampMicroseconds;
                    last[i] = unwrap[i];
                    counts[i]++;
                    if (sample.Overrun) overruns++;
                    if (Volatile.Read(ref reading)) Interlocked.Increment(ref samplesDuringRead);
                    else if (!Volatile.Read(ref readDone)) Interlocked.Increment(ref samplesBeforeRead);
                }
            }
            catch (OperationCanceledException) { }
        });

        // Memory access while every list streams: the read must succeed and DAQ must keep flowing meanwhile.
        int readLength = Math.Min(1024, c.Region?.Length ?? 1024);
        await Task.Delay(TimeSpan.FromTicks(c.Options.DaqWindow.Ticks / 3), ct).ConfigureAwait(false);
        Volatile.Write(ref reading, true);
        var concurrent = await c.Master.ReadAsync(address, readLength, cancellationToken: ct).ConfigureAwait(false);
        Volatile.Write(ref reading, false);
        Volatile.Write(ref readDone, true);
        bool readMatches = c.Region is not { } region || concurrent.AsSpan().SequenceEqual(region.AsSpan(0, readLength));

        await pump.ConfigureAwait(false);
        await subscription.StopAsync(ct).ConfigureAwait(false);

        var lines = new List<string>();
        bool fail = false, warn = false;
        for (int i = 0; i < events.Count; i++)
        {
            int expected = target.EventRatesHz[events[i]];
            if (counts[i] < 3)
            {
                warn = true;
                lines.Add($"event {events[i]}: {counts[i]} sample(s), too few to measure {expected} Hz");
                continue;
            }
            double rate = (counts[i] - 1) / (last[i] / 1e6);
            bool ok = Math.Abs(rate - expected) <= expected * c.Options.RateTolerance;
            fail |= !ok;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"event {events[i]}: {rate:F1} Hz (expected {expected}){(ok ? "" : " OUT OF RANGE")}"));
        }
        string concurrency = !readMatches ? "the concurrent read returned different bytes"
            : samplesDuringRead == 0 ? "no DAQ arrived during the concurrent read"
            : $"{readLength} B read while {samplesDuringRead} DAQ samples arrived";
        if (!readMatches) fail = true;
        if (samplesDuringRead == 0) warn = true;
        if (overruns > 0) warn = true;
        string detail = $"{string.Join("; ", lines)}. {concurrency}; {overruns} overrun-flagged sample(s); {subscription.Stream.Dropped} dropped.";
        return (fail ? CcpCheckStatus.Fail : warn ? CcpCheckStatus.Warn : CcpCheckStatus.Pass, detail);
    }

    private static async Task<(CcpCheckStatus, string)> EngineSpeedAsync(Context c, CancellationToken ct)
    {
        if (c.Options.EngineSpeed is not { } symbol) return (CcpCheckStatus.Skipped, "No engine-speed symbol for this build.");
        var raw = await c.Master.ShortUploadAsync(symbol.Address, symbol.Size, ct).ConfigureAwait(false);
        long value = 0;
        foreach (byte b in raw) value = value << 8 | b;
        double rpm = value * symbol.Scale;
        return (CcpCheckStatus.Pass, string.Create(CultureInfo.InvariantCulture,
            $"{rpm:0} rpm at 0x{symbol.Address:X8}{(rpm > 0 ? " (engine running: calibration writes would be refused)" : "")}."));
    }

    private static CcpCheckResult Counters(CcpMasterStatistics s)
    {
        var problems = new List<string>();
        if (s.Timeouts > 0) problems.Add($"{s.Timeouts} timeout(s)");
        if (s.StrayReplies > 0) problems.Add($"{s.StrayReplies} stray repl{(s.StrayReplies == 1 ? "y" : "ies")}");
        if (s.MalformedFrames > 0) problems.Add($"{s.MalformedFrames} malformed frame(s)");
        if (s.UnroutedDaqFrames > 0) problems.Add($"{s.UnroutedDaqFrames} unrouted DAQ frame(s)");
        if (s.ErrorReplies > 0) problems.Add($"{s.ErrorReplies} error repl{(s.ErrorReplies == 1 ? "y" : "ies")}");
        return problems.Count == 0
            ? new("Session counters", CcpCheckStatus.Pass, "No timeouts, stray replies, malformed or unrouted frames, or error replies.", TimeSpan.Zero)
            : new("Session counters", CcpCheckStatus.Warn, string.Join(", ", problems) + " this session (they may predate the self-test).", TimeSpan.Zero);
    }
}
