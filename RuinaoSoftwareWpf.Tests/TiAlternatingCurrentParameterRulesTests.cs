using HardwareParameterKind = RuinaoTesHardware.AlternatingCurrentParameterKind;
using HardwareParameterRules = RuinaoTesHardware.AlternatingCurrentParameterRules;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TiAlternatingCurrentParameterRulesTests
{
    [Theory]
    [InlineData(TiAlternatingCurrentParameterKind.PeakCurrentMilliampere, "0", "0.125")]
    [InlineData(TiAlternatingCurrentParameterKind.PeakCurrentMilliampere, "2.5", "0.125")]
    [InlineData(TiAlternatingCurrentParameterKind.PeakCurrentMilliampere, "0.1235", "0.125")]
    [InlineData(TiAlternatingCurrentParameterKind.RampUpSeconds, "3600.1", "0.5")]
    [InlineData(TiAlternatingCurrentParameterKind.FrequencyHz, "10000.5", "1000")]
    [InlineData(TiAlternatingCurrentParameterKind.TotalDurationSeconds, "0", "1200.0")]
    public void Normalize_MatchesSharedHardwareDll(
        TiAlternatingCurrentParameterKind kind,
        string value,
        string fallback)
    {
        var applicationResult = TiAlternatingCurrentParameterRules.Normalize(kind, value, fallback);
        var hardwareResult = HardwareParameterRules.Normalize(
            (HardwareParameterKind)(int)kind,
            value,
            fallback);

        Assert.Equal(hardwareResult.Value, applicationResult.Value);
        Assert.Equal(hardwareResult.Message is null, applicationResult.IsValid);
    }

    [Fact]
    public void TryCreate_UsesFrozenTiSpecification()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 1",
            CurrentMA = "2.000",
            RampUpS = "0.5",
            RampDownS = "0.5",
            FrequencyHz = "10000",
            DurationS = "3600.0",
        };

        var succeeded = TiAlternatingCurrentParameters.TryCreate(channel, out var result, out var error);

        Assert.True(succeeded, error);
        Assert.NotNull(result);
        Assert.Equal(2.000m, result.PeakCurrentMilliampere);
        Assert.Equal(10_000U, result.FrequencyHz);
        Assert.Equal(3_600.0m, result.TotalDurationSeconds);
    }

    [Fact]
    public void TryCreate_WhenRampsExceedTotal_RejectsStart()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 1",
            CurrentMA = "1.000",
            RampUpS = "0.6",
            RampDownS = "0.5",
            FrequencyHz = "1000",
            DurationS = "1.0",
        };

        var succeeded = TiAlternatingCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("不能小于", error, StringComparison.Ordinal);
    }
}
