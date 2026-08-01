using RuinaoTesHardware;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class DirectCurrentStimulationTimelineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 1)]
    [InlineData(10, 2)]
    [InlineData(60, 2)]
    [InlineData(115, 1)]
    [InlineData(120, 0)]
    public void Continuous_CalculatesRampPlateauAndRemainingTime(
        double elapsedSeconds,
        double expectedCurrent)
    {
        var plan = CreatePlan(DirectCurrentDeliveryMode.Continuous, DirectCurrentPolarity.Normal);

        var progress = DirectCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal((decimal)expectedCurrent, progress.ExpectedCurrentMilliampere);
        Assert.Equal(Math.Max(0, 120 - elapsedSeconds), progress.Remaining.TotalSeconds, 6);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(15, 2)]
    [InlineData(25, 1)]
    [InlineData(32, 0)]
    [InlineData(40, 1)]
    public void Intermittent_CalculatesRepeatedTrapezoidAndZeroCurrentInterval(
        double elapsedSeconds,
        double expectedCurrent)
    {
        var plan = CreatePlan(DirectCurrentDeliveryMode.Intermittent, DirectCurrentPolarity.Normal);

        var progress = DirectCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal((decimal)expectedCurrent, progress.ExpectedCurrentMilliampere);
    }

    [Fact]
    public void Reversed_ReturnsNegativeExpectedCurrent()
    {
        var plan = CreatePlan(DirectCurrentDeliveryMode.Continuous, DirectCurrentPolarity.Reversed);

        var progress = DirectCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromSeconds(15));

        Assert.Equal(-2m, progress.ExpectedCurrentMilliampere);
    }

    private static DirectCurrentStimulationPlan CreatePlan(
        DirectCurrentDeliveryMode mode,
        DirectCurrentPolarity polarity) =>
        DirectCurrentStimulationClient.CreatePlan(
            new DirectCurrentStimulationParameters(
                BoardAddress: 0x01,
                Channel: 1,
                CurrentMilliampere: 2m,
                RampUpSeconds: 10m,
                RampDownSeconds: 10m,
                TotalDurationSeconds: 120m,
                DeliveryMode: mode,
                IntervalSeconds: mode == DirectCurrentDeliveryMode.Intermittent ? 5m : 0m,
                SingleDurationSeconds: mode == DirectCurrentDeliveryMode.Intermittent ? 30m : 0m,
                Polarity: polarity));
}
