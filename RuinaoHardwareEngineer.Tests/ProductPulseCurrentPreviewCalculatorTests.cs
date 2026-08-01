using RuinaoHardwareEngineer.Features.ProductPulseCurrent;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class ProductPulseCurrentPreviewCalculatorTests
{
    [Fact]
    public void Calculate_ValidParameters_CalculatesCompletePulseCountAndCandidateSegments()
    {
        var preview = ProductPulseCurrentPreviewCalculator.Calculate(
            currentMilliampere: 10m,
            rampWidthMilliseconds: 5m,
            pulseWidthMilliseconds: 10m,
            intervalWidthMilliseconds: 20m,
            treatmentDurationSeconds: 120m,
            reversed: false);

        Assert.Equal(4_000U, preview.TotalPulseCount);
        Assert.Equal(5_000U, preview.RampDurationMicroseconds);
        Assert.Equal(10_000U, preview.PulseDurationMicroseconds);
        Assert.Equal(20_000U, preview.IntervalDurationMicroseconds);
        Assert.Equal(30_000U, preview.PulseCycleMicroseconds);
        Assert.Equal(120_000U, preview.TreatmentDurationMilliseconds);
        Assert.Equal(21_845, preview.SignedDa);
    }

    [Fact]
    public void Calculate_Reversed_ReturnsNegativeCurrentAndDa()
    {
        var preview = ProductPulseCurrentPreviewCalculator.Calculate(
            currentMilliampere: 10m,
            rampWidthMilliseconds: 5m,
            pulseWidthMilliseconds: 10m,
            intervalWidthMilliseconds: 20m,
            treatmentDurationSeconds: 120m,
            reversed: true);

        Assert.Equal(-10m, preview.SignedCurrentMilliampere);
        Assert.Equal(-21_845, preview.SignedDa);
    }

    [Fact]
    public void Calculate_TreatmentCannotContainFirstPulse_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ProductPulseCurrentPreviewCalculator.Calculate(
                currentMilliampere: 1m,
                rampWidthMilliseconds: 5m,
                pulseWidthMilliseconds: 10m,
                intervalWidthMilliseconds: 20m,
                treatmentDurationSeconds: 0.01m,
                reversed: false));

        Assert.Contains("第一次完整脉冲", exception.Message, StringComparison.Ordinal);
    }
}
