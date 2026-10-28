using System.Text.Json;
using LotusECMLogger.Services;

namespace LotusECMLogger.Tests;

public sealed class AbsPressureConfigurationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EditableAndRuntimeRoundTripsPreserveOptInForSingleAndMultipleEcus(bool multiple)
    {
        string file = Path.Combine(Path.GetTempPath(), $"abs-config-{Guid.NewGuid()}.json");
        try
        {
            var config = LoggingConfigFileService.CreateDefaultConfig();
            if (multiple) config.Ecus!.Add(new ECUGroupJson { Name = "AEM", RequestId = 0x7E1, ResponseId = 0x7E9 });
            foreach (bool enabled in new[] { true, false })
            {
                config.LogAbsPressure = enabled;
                LoggingConfigFileService.SaveEditableConfig(config, file);
                Assert.Equal(enabled, LoggingConfigFileService.LoadEditableConfig(file).LogAbsPressure);
                var runtime = MultiECUConfigurationLoader.LoadFromFile(file);
                Assert.Equal(enabled, runtime.LogAbsPressure);
                MultiECUConfigurationLoader.SaveToFile(runtime, file);
                Assert.Equal(enabled, MultiECUConfigurationLoader.LoadFromFile(file).LogAbsPressure);
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                Assert.Equal(enabled, json.RootElement.TryGetProperty("logAbsPressure", out _));
            }
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HighSpeedPresetLoadsOptInWithoutConsumingEngineChannelCapacity(bool enabled)
    {
        string file = Path.Combine(Path.GetTempPath(), $"abs-preset-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                logAbsPressure = enabled,
                channels = new[] { new { name = "Engine", address = "0x40000000", size = 2, rate = 10 } }
            }));
            var preset = HighSpeedLogPresetLoader.LoadFromFile(file);
            Assert.Equal(enabled, preset.LogAbsPressure);
            Assert.Single(preset.Channels);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ExistingConfigurationsRemainOptedOut()
    {
        var configurations = MultiECUConfigurationLoader.GetAvailableConfigurations();
        Assert.NotEmpty(configurations);
        foreach (string name in configurations)
            Assert.False(MultiECUConfigurationLoader.LoadByName(name).LogAbsPressure);
        Assert.False(new HighSpeedLogPresetJson().LogAbsPressure);
        Assert.False(new HighSpeedLogPreset().LogAbsPressure);
        Assert.False(LoggingConfigFileService.CreateDefaultConfig().LogAbsPressure);
    }
}
