namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionQuestionTimingRulesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(19_999, 0)]
    [InlineData(20_000, 1)]
    [InlineData(119_999, 1)]
    [InlineData(120_000, 2)]
    [InlineData(125_000, 2)]
    public void EvaluateAnswer_UsesTwentyAndOneHundredTwentySecondBoundaries(
        int elapsedMilliseconds,
        int expected)
    {
        var actual = EmotionQuestionTimingRules.EvaluateAnswer(
            TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal((EmotionQuestionAnswerTimingState)expected, actual);
    }

    [Theory]
    [InlineData(0, 20, 20)]
    [InlineData(1, 20, 20)]
    [InlineData(19_001, 20, 1)]
    [InlineData(20_000, 20, 0)]
    [InlineData(59_001, 60, 1)]
    [InlineData(60_000, 60, 0)]
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
    public void ConfirmedDurations_AreTwentyOneHundredTwentyAndSixtySeconds()
    {
        Assert.Equal(20, EmotionQuestionTimingRules.MinimumAnswerSeconds);
        Assert.Equal(120, EmotionQuestionTimingRules.MaximumAnswerSeconds);
        Assert.Equal(60, EmotionQuestionTimingRules.MaximumThinkingSeconds);
    }

    [Theory]
    [InlineData(59_999, false)]
    [InlineData(60_000, true)]
    [InlineData(61_000, true)]
    public void Thinking_AutoStartsAtSixtySeconds(int milliseconds, bool expected)
    {
        Assert.Equal(expected, EmotionQuestionTimingRules.ShouldStartAnswer(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
