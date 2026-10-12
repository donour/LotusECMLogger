using System.Runtime.CompilerServices;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>
/// Freezes the command frames the live-validated high-speed logger sends for every shipped preset
/// (all channels at their default rate), as the regression gate for porting it onto CcpMaster.
/// Fixture lines are "opcode parameters" in hex, without the sequence byte or DLC padding.
/// Regenerate deliberately with HSL_GOLDEN_UPDATE=1, and review the diff.
/// </summary>
public class HighSpeedLogGoldenTests
{
    public static TheoryData<string> Presets => new()
    {
        "B13200091_closed_loop_fuel_diag.json", "B13200091_fuel_diagnostics.json", "B13200091_ignition_tuning.json",
        "B132E0091_ac_diagnosis.json", "B132E0091_dyno_tuning.json", "B132E0091_fuel_learning.json",
        "C132E0278_dpm_diagnostics.json", "C132E0278_fuel_diagnostics.json", "C132E0278_throttle_diagnostics.json",
        "b13200091-sample.json", "c132e0278-sample.json",
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void ConfigurationFramesMatchTheLiveValidatedLogger(string preset)
    {
        var actual = Render(preset, LegacyFrames);
        string path = FixturePath(preset);
        if (Environment.GetEnvironmentVariable("HSL_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, actual);
        }
        Assert.Equal(File.ReadAllLines(path), actual);
    }

    internal static IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> Selection(string preset)
    {
        var loaded = HighSpeedLogPresetLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "config", "highSpeedLogger", preset));
        return loaded.Channels.Select(c => (c, c.DefaultRate)).ToList();
    }

    /// <summary>Header plus one line per command, or the planner's error for over-capacity selections.</summary>
    internal static List<string> Render(string preset, Func<LoggingPlan, IEnumerable<string>> frames)
    {
        var selection = Selection(preset);
        var lines = new List<string> { $"# preset: {preset}", $"# selection: {selection.Count} channel(s) at their default rate" };
        LoggingPlan plan;
        try { plan = HighSpeedLogPlanner.Plan(selection); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            lines.Add($"# planner error: {ex.Message}");
            return lines;
        }
        lines.AddRange(frames(plan));
        return lines;
    }

    private static IEnumerable<string> LegacyFrames(LoggingPlan plan) =>
        HighSpeedLogLegacyCommands.Configure(plan).Select(c => $"{c.Command:X2} {Convert.ToHexString(c.Parameters)}");

    private static string FixturePath(string preset, [CallerFilePath] string source = "") =>
        Path.Combine(Environment.GetEnvironmentVariable("HSL_GOLDEN_UPDATE") == "1"
            ? Path.GetDirectoryName(source)!
            : AppContext.BaseDirectory, "Fixtures", "HighSpeedLog", Path.ChangeExtension(preset, ".txt"));
}
