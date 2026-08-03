using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationImpedancePolicyTests
{
    [Theory]
    [InlineData(null, StimulationImpedanceStatus.Unavailable)]
    [InlineData(1, StimulationImpedanceStatus.Normal)]
    [InlineData(10000, StimulationImpedanceStatus.Normal)]
    [InlineData(10001, StimulationImpedanceStatus.Warning)]
    [InlineData(20000, StimulationImpedanceStatus.Warning)]
    [InlineData(20001, StimulationImpedanceStatus.Critical)]
    public void GetStatus_UsesConfirmedThresholdBoundaries(
        int? impedanceOhms,
        StimulationImpedanceStatus expected)
    {
        Assert.Equal(
            expected,
            StimulationImpedancePresentation.GetStatus(impedanceOhms));
    }

    [Fact]
    public void Evaluate_SkipsCriticalAndUnavailableChannelsButKeepsWarningChannels()
    {
        var normal = CreateChannel("CH 1", 500m);
        var warning = CreateChannel("CH 2", 12_400m);
        var critical = CreateChannel("CH 3", 20_001m);
        var unavailable = CreateChannel("CH 4", null);

        var result = StimulationImpedanceStartPolicy.Evaluate(
            [normal, warning, critical, unavailable]);

        Assert.Equal([normal, warning], result.EligibleChannels);
        Assert.Equal([warning], result.WarningChannels);
        Assert.Equal([critical], result.CriticalChannels);
        Assert.Equal([unavailable], result.UnavailableChannels);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void BuildConfirmationMessage_SummarizesWarningAndSkippedChannels()
    {
        var warning = CreateChannel("CH 2", 12_400m);
        var critical = CreateChannel("CH 3", 20_001m);
        var unavailable = CreateChannel("CH 8", null);

        var message = StimulationImpedanceStartPolicy.BuildConfirmationMessage(
            StimulationImpedanceStartPolicy.Evaluate([warning, critical, unavailable]));

        Assert.Contains("CH2：12.40kΩ", message);
        Assert.Contains("CH3：阻抗过高", message);
        Assert.Contains("CH8：阻抗不可用", message);
        Assert.Contains("是否仍要开始其余符合条件的通道？", message);
    }

    [Fact]
    public void ConsecutiveFailureTracker_InvalidatesOnlyOnSecondConsecutiveFailure()
    {
        var tracker = new StimulationBoardReadFailureTracker();

        Assert.False(tracker.RecordFailure(0x01));
        Assert.True(tracker.RecordFailure(0x01));
        tracker.RecordSuccess(0x01);
        Assert.False(tracker.RecordFailure(0x01));
    }

    [Fact]
    public void ChannelPresentation_UsesGrayGreenBlueYellowAndRedStates()
    {
        var channel = CreateChannel("CH 1", null);
        Assert.Equal("#FF5F6B7D", channel.StatusIndicatorBrush.ToString());

        channel.UpdateImpedance(500m);
        Assert.Equal("#FF5DDA77", channel.StatusIndicatorBrush.ToString());

        channel.IsStimulating = true;
        Assert.Equal("#FF5A9FF2", channel.StatusIndicatorBrush.ToString());

        channel.UpdateImpedance(12_400m);
        Assert.Equal("#FFFFD84D", channel.StatusIndicatorBrush.ToString());

        channel.UpdateImpedance(20_001m);
        Assert.Equal("#FFE84E4F", channel.StatusIndicatorBrush.ToString());
    }

    private static ChannelConfig CreateChannel(string name, decimal? impedanceOhms)
    {
        var channel = new ChannelConfig { Name = name };
        channel.UpdateImpedance(impedanceOhms);
        return channel;
    }
}
