namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪问答思考与回答阶段的时间规则。
/// DispatcherTimer 只负责触发界面刷新，业务边界以单调时钟的实际经过时间判断。
/// </summary>
internal static class EmotionQuestionTimingRules
{
    internal const int MinimumAnswerSeconds = 20;
    internal const int MaximumAnswerSeconds = 120;
    internal const int MaximumThinkingSeconds = 60;

    internal static bool ShouldStartAnswer(TimeSpan elapsed) =>
        elapsed >= TimeSpan.FromSeconds(MaximumThinkingSeconds);

    internal static EmotionQuestionAnswerTimingState EvaluateAnswer(TimeSpan elapsed)
    {
        if (elapsed >= TimeSpan.FromSeconds(MaximumAnswerSeconds))
        {
            return EmotionQuestionAnswerTimingState.MaximumReached;
        }

        return elapsed >= TimeSpan.FromSeconds(MinimumAnswerSeconds)
            ? EmotionQuestionAnswerTimingState.Submittable
            : EmotionQuestionAnswerTimingState.MinimumDuration;
    }

    internal static int RemainingSeconds(TimeSpan elapsed, int durationSeconds)
    {
        var remaining = durationSeconds - elapsed.TotalSeconds;
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
    }
}

internal enum EmotionQuestionAnswerTimingState
{
    MinimumDuration,
    Submittable,
    MaximumReached
}
