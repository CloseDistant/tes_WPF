using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TacsParameterRulesTests
{
    [Theory]
    [InlineData(TacsParameterKind.PeakCurrentMilliampere, "0.001", "0.001")]
    [InlineData(TacsParameterKind.PeakCurrentMilliampere, "2.500", "2.000")]
    [InlineData(TacsParameterKind.RampUpSeconds, "3600.1", "3600.0")]
    [InlineData(TacsParameterKind.FrequencyHz, "10000.5", "10000")]
    [InlineData(TacsParameterKind.TotalDurationSeconds, "0", "1200.0")]
    public void Normalize_UsesIndependentTacsSpecification(
        TacsParameterKind kind,
        string value,
        string expected)
    {
        var result = TacsParameterRules.Normalize(kind, value, "1200.0");

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void TryCreate_AcceptsTacsBoundaryValues()
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

        var succeeded = TacsParameters.TryCreate(channel, out var result, out var error);

        Assert.True(succeeded, error);
        Assert.NotNull(result);
        Assert.Equal(2.000m, result.PeakCurrentMilliampere);
        Assert.Equal(10_000U, result.FrequencyHz);
        Assert.Equal(3_600.0m, result.TotalDurationSeconds);
    }

    [Fact]
    public void TryCreate_WhenRampsExceedTreatmentTime_RejectsStart()
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

        var succeeded = TacsParameters.TryCreate(channel, out _, out var error);

        Assert.False(succeeded);
        Assert.Contains("不能小于", error, StringComparison.Ordinal);
    }
}
