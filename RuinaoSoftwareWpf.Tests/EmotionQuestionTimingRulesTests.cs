namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionQuestionTimingRulesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(29_999, 0)]
    [InlineData(30_000, 1)]
    [InlineData(119_999, 1)]
    [InlineData(120_000, 2)]
    [InlineData(125_000, 2)]
    public void EvaluateAnswer_UsesConfirmedThirtyAndOneHundredTwentySecondBoundaries(
        int elapsedMilliseconds,
        int expected)
    {
        var actual = EmotionQuestionTimingRules.EvaluateAnswer(
            TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal((EmotionQuestionAnswerTimingState)expected, actual);
    }

    [Theory]
    [InlineData(0, 30, 30)]
    [InlineData(1, 30, 30)]
    [InlineData(29_001, 30, 1)]
    [InlineData(30_000, 30, 0)]
    [InlineData(11_001, 12, 1)]
    [InlineData(12_000, 12, 0)]
    public void RemainingSeconds_RoundsUpWithoutEndingAStageEarly(
        int elapsedMilliseconds,
        int durationSeconds,
        int expected)
    {
        var actual = EmotionQuestionTimingRules.RemainingSeconds(
            TimeSpan.FromMilliseconds(elapsedMilliseconds),
            durationSeconds);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConfirmedDurations_RemainThirtyOneHundredTwentyAndTwelveSeconds()
    {
        Assert.Equal(30, EmotionQuestionTimingRules.MinimumAnswerSeconds);
        Assert.Equal(120, EmotionQuestionTimingRules.MaximumAnswerSeconds);
        Assert.Equal(12, AssessmentCaptureViewModel.CaptureWorkbenchForcedRestSeconds);
    }
}
