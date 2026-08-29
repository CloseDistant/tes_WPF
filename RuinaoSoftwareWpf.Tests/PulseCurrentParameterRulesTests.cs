namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class PulseCurrentParameterRulesTests
{
    [Theory]
    [InlineData("15.001", "15.00")]
    [InlineData("3600.1", "3600.0")]
    public void Normalize_AboveMaximumClampsAndRequestsToast(string input, string expected)
    {
        var kind = expected == "15.00"
            ? PulseCurrentParameterKind.CurrentMilliamp
            : PulseCurrentParameterKind.TreatmentDurationSeconds;

        var result = PulseCurrentParameterRules.Normalize(kind, input, "1.0");

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Value);
        Assert.Contains("已调整", result.ErrorMessage);
    }

    [Theory]
    [InlineData("1.235", "1.24")]
    [InlineData("12.35", "12.4")]
    public void Normalize_ExcessPrecisionRoundsWithoutToast(string input, string expected)
    {
        var kind = expected == "1.24"
            ? PulseCurrentParameterKind.CurrentMilliamp
            : PulseCurrentParameterKind.TreatmentDurationSeconds;

        var result = PulseCurrentParameterRules.Normalize(kind, input, "1.0");

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value);
        Assert.Empty(result.ErrorMessage);
    }

    [Theory]
    [InlineData(PulseCurrentParameterKind.PulseWidthMilliseconds, "0")]
    [InlineData(PulseCurrentParameterKind.IntervalWidthMilliseconds, "0")]
    [InlineData(PulseCurrentParameterKind.TreatmentDurationSeconds, "0")]
    public void Normalize_DisallowedZeroRestoresPreviousValue(
        PulseCurrentParameterKind kind,
        string input)
    {
        var result = PulseCurrentParameterRules.Normalize(kind, input, "10");

        Assert.False(result.IsValid);
        Assert.Equal("10", result.Value);
    }

    [Fact]
    public void CalculatePlannedTotalCount_LastIntervalIsOmitted()
    {
        var count = PulseCurrentParameterRules.CalculatePlannedTotalCount(
            treatmentDurationSeconds: 1,
            pulseWidthMilliseconds: 100,
            intervalWidthMilliseconds: 100);

        Assert.Equal(5, count);
    }
}
