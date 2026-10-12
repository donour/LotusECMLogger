using System.Runtime.CompilerServices;
using LotusECMLogger.Models;
using LotusECMLogger.Services;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>
/// The command frames the live-validated high-speed logger sent for every shipped preset (all channels at
/// their default rate), frozen before it was ported onto CcpMaster. Fixture lines are "opcode parameters" in
/// hex, without the sequence byte or DLC padding. The ported logger runs against the simulator and its
/// configuration CROs, normalised to that form, must match. Normalisation accepts only the intended
/// differences: zero padding to DLC 8, the GET_CCP_VERSION check after CONNECT, and the DTO ID that
/// GET_DAQ_SIZE now carries in bytes 4–7 (the slave ignores them).
/// HSL_GOLDEN_UPDATE=1 rewrites the fixtures from the ported logger, so use it only for an intended protocol
/// change, and review the diff.
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
        var actual = Render(preset, PortedFrames);
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
    internal static List<string> Render(string preset,
        Func<IReadOnlyList<(HighSpeedChannel Channel, int RateHz)>, IEnumerable<string>> frames)
    {
        var selection = Selection(preset);
        var lines = new List<string> { $"# preset: {preset}", $"# selection: {selection.Count} channel(s) at their default rate" };
        try { HighSpeedLogPlanner.Plan(selection); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            lines.Add($"# planner error: {ex.Message}");
            return lines;
        }
        lines.AddRange(frames(selection));
        return lines;
    }

    /// <summary>Runs the ported logger until it streams, and returns its configuration CROs in fixture form.</summary>
    private static IEnumerable<string> PortedFrames(IReadOnlyList<(HighSpeedChannel Channel, int RateHz)> selection)
    {
        using var harness = new LoggerHarness();
        harness.RunUntilStreaming(selection);
        return Normalise(harness.Script!.Sent);
    }

    // Parameter bytes the legacy builder sent per opcode; the rest of each 8-byte CRO must be zero padding.
    private static readonly Dictionary<CcpCommandCode, int> LegacyParameterLength = new()
    {
        [CcpCommandCode.Connect] = 2, [CcpCommandCode.GetDaqSize] = 1, [CcpCommandCode.SetDaqPointer] = 3,
        [CcpCommandCode.WriteDaq] = 6, [CcpCommandCode.StartStop] = 6, [CcpCommandCode.StartStopAll] = 1,
    };

    internal static List<string> Normalise(IReadOnlyList<byte[]> sent)
    {
        var lines = new List<string>();
        foreach (var cro in sent)
        {
            var command = (CcpCommandCode)cro[0];
            if (command == CcpCommandCode.GetCcpVersion)
            {
                Assert.Equal((byte)CcpCommandCode.Connect, sent[sent.ToList().IndexOf(cro) - 1][0]);
                continue;
            }
            Assert.True(LegacyParameterLength.TryGetValue(command, out int length), $"Unexpected {command} while configuring.");
            var padding = cro.AsSpan(2 + length);
            if (command == CcpCommandCode.GetDaqSize)
            {
                Assert.Equal("00000351", Convert.ToHexString(cro.AsSpan(4)));
                padding = cro.AsSpan(3, 1);
            }
            Assert.True(padding.IndexOfAnyExcept((byte)0) < 0, $"{command} has non-zero padding.");
            lines.Add($"{cro[0]:X2} {Convert.ToHexString(cro.AsSpan(2, length))}");
            if (command == CcpCommandCode.StartStopAll) break; // configuration ends when the lists start
        }
        return lines;
    }

    private static string FixturePath(string preset, [CallerFilePath] string source = "") =>
        Path.Combine(Environment.GetEnvironmentVariable("HSL_GOLDEN_UPDATE") == "1"
            ? Path.GetDirectoryName(source)!
            : AppContext.BaseDirectory, "Fixtures", "HighSpeedLog", Path.ChangeExtension(preset, ".txt"));
}
