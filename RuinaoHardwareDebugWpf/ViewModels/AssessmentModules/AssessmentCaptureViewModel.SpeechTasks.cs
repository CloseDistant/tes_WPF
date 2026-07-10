namespace RuinaoHardwareDebugWpf;

public sealed partial class AssessmentCaptureViewModel
{
    private void BeginVoiceBaselineSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        voiceBaselineIndex = 0;
        voiceBaselineRemainingSeconds = VoiceBaselineSegmentSeconds;
        currentVoiceBaselineStartedAt = null;
        voiceBaselinePhase = VoiceBaselinePhase.WaitingToStart;
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineReady", 1, VoiceBaselineItems.Length);
        VoiceBaselineRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceVoiceBaselineStageNotice");
    }

    private void StartVoiceBaselineSegment()
    {
        if (!IsVoiceBaselineModule || currentStep != CaptureWorkbenchStep.ModuleExecution || voiceBaselineIndex >= VoiceBaselineItems.Length)
        {
            return;
        }

        var item = VoiceBaselineItems[voiceBaselineIndex];
        currentVoiceBaselineStartedAt = DateTimeOffset.Now;
        voiceBaselineRemainingSeconds = VoiceBaselineSegmentSeconds;
        voiceBaselinePhase = VoiceBaselinePhase.Recording;
        VoiceBaselineRestText = string.Empty;
        UpdateVoiceBaselineStatusText();
        RecordModuleEventSafely(
            "voice_baseline_segment_started",
            $"语音基线第 {voiceBaselineIndex + 1} 段开始",
            new
            {
                segmentIndex = voiceBaselineIndex + 1,
                segmentTotal = VoiceBaselineItems.Length,
                promptText = item.PromptText,
                syllableName = item.SyllableName,
                syllableType = item.SyllableType,
                minDurationSeconds = VoiceBaselineSegmentSeconds,
                startedAtUnixMs = currentVoiceBaselineStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentVoiceBaselineStartedAt,
            null);
        voiceBaselineTimer.Start();
        NotifyStageChanged();
    }

    private void AdvanceVoiceBaseline()
    {
        if (!IsVoiceBaselineModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetVoiceBaselineState();
            NotifyStageChanged();
            return;
        }

        if (voiceBaselinePhase == VoiceBaselinePhase.Recording)
        {
            if (voiceBaselineRemainingSeconds > 1)
            {
                voiceBaselineRemainingSeconds--;
                UpdateVoiceBaselineStatusText();
                NotifyStageChanged();
                return;
            }

            CompleteCurrentVoiceBaselineSegment();
            return;
        }

        if (voiceBaselinePhase == VoiceBaselinePhase.Resting)
        {
            if (voiceBaselineRemainingSeconds > 1)
            {
                voiceBaselineRemainingSeconds--;
                UpdateVoiceBaselineRestText();
                NotifyStageChanged();
                return;
            }

            StartVoiceBaselineSegment();
        }
    }

    private void CompleteCurrentVoiceBaselineSegment()
    {
        var completedAt = DateTimeOffset.Now;
        var item = voiceBaselineIndex >= 0 && voiceBaselineIndex < VoiceBaselineItems.Length
            ? VoiceBaselineItems[voiceBaselineIndex]
            : null;
        var durationMs = currentVoiceBaselineStartedAt.HasValue
            ? (long)(completedAt - currentVoiceBaselineStartedAt.Value).TotalMilliseconds
            : 0L;

        if (item is not null)
        {
            RecordModuleEventSafely(
                "voice_baseline_segment_completed",
                $"语音基线第 {voiceBaselineIndex + 1} 段完成",
                new
                {
                    segmentIndex = voiceBaselineIndex + 1,
                    segmentTotal = VoiceBaselineItems.Length,
                    promptText = item.PromptText,
                    syllableName = item.SyllableName,
                    syllableType = item.SyllableType,
                    startedAtUnixMs = currentVoiceBaselineStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs
                },
                currentVoiceBaselineStartedAt,
                completedAt);
        }

        voiceBaselineIndex++;
        currentVoiceBaselineStartedAt = null;

        if (voiceBaselineIndex >= VoiceBaselineItems.Length)
        {
            CompleteVoiceBaseline();
            return;
        }

        voiceBaselinePhase = VoiceBaselinePhase.Resting;
        voiceBaselineRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        VoiceBaselineStatusText = $"已完成 {voiceBaselineIndex} / {VoiceBaselineItems.Length} 段";
        UpdateVoiceBaselineRestText();
        NotifyStageChanged();
    }

    private void CompleteVoiceBaseline()
    {
        voiceBaselineTimer.Stop();
        voiceBaselinePhase = VoiceBaselinePhase.Completed;
        voiceBaselineRemainingSeconds = 0;
        currentVoiceBaselineStartedAt = null;
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineCompleted");
        VoiceBaselineRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceVoiceBaselineCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateVoiceBaselineStatusText()
    {
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineKeepSpeaking", voiceBaselineRemainingSeconds);
    }

    private void UpdateVoiceBaselineRestText()
    {
        VoiceBaselineRestText = T("CaptureWorkspaceRestRemaining", voiceBaselineRemainingSeconds);
    }

    /// <summary>
    /// 初始化词语朗读序列。
    /// 模块级音视频录制已经开始，此处只控制 6 组词语的时间戳和 UI 状态。
    /// </summary>
    private void BeginWordReadingSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        wordReadingIndex = 0;
        wordReadingRemainingSeconds = WordReadingGroupSeconds;
        currentWordReadingStartedAt = null;
        wordReadingPhase = WordReadingPhase.WaitingToStart;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingReady", 1, WordReadingGroups.Length);
        WordReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceWordReadingStageNotice");
    }

    private void StartWordReadingGroup()
    {
        if (!IsWordReadingModule || currentStep != CaptureWorkbenchStep.ModuleExecution || wordReadingIndex >= WordReadingGroups.Length)
        {
            return;
        }

        var group = WordReadingGroups[wordReadingIndex];
        currentWordReadingStartedAt = DateTimeOffset.Now;
        wordReadingRemainingSeconds = WordReadingGroupSeconds;
        wordReadingPhase = WordReadingPhase.Reading;
        WordReadingRestText = string.Empty;
        UpdateWordReadingStatusText();
        RecordModuleEventSafely(
            "word_reading_group_started",
            $"词语朗读第 {wordReadingIndex + 1} 组开始",
            new
            {
                groupIndex = wordReadingIndex + 1,
                groupTotal = WordReadingGroups.Length,
                words = group.Words,
                wordGroupType = group.WordGroupType,
                fixedDurationSeconds = WordReadingGroupSeconds,
                startedAtUnixMs = currentWordReadingStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentWordReadingStartedAt,
            null);
        wordReadingTimer.Start();
        NotifyStageChanged();
    }

    private void AdvanceWordReading()
    {
        if (!IsWordReadingModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetWordReadingState();
            NotifyStageChanged();
            return;
        }

        if (wordReadingPhase == WordReadingPhase.Reading)
        {
            if (wordReadingRemainingSeconds > 1)
            {
                wordReadingRemainingSeconds--;
                UpdateWordReadingStatusText();
                NotifyStageChanged();
                return;
            }

            CompleteCurrentWordReadingGroup();
            return;
        }

        if (wordReadingPhase == WordReadingPhase.Resting)
        {
            if (wordReadingRemainingSeconds > 1)
            {
                wordReadingRemainingSeconds--;
                UpdateWordReadingRestText();
                NotifyStageChanged();
                return;
            }

            StartWordReadingGroup();
        }
    }

    private void CompleteCurrentWordReadingGroup()
    {
        var completedAt = DateTimeOffset.Now;
        var group = wordReadingIndex >= 0 && wordReadingIndex < WordReadingGroups.Length
            ? WordReadingGroups[wordReadingIndex]
            : null;
        var durationMs = currentWordReadingStartedAt.HasValue
            ? (long)(completedAt - currentWordReadingStartedAt.Value).TotalMilliseconds
            : 0L;

        if (group is not null)
        {
            RecordModuleEventSafely(
                "word_reading_group_completed",
                $"词语朗读第 {wordReadingIndex + 1} 组完成",
                new
                {
                    groupIndex = wordReadingIndex + 1,
                    groupTotal = WordReadingGroups.Length,
                    words = group.Words,
                    wordGroupType = group.WordGroupType,
                    startedAtUnixMs = currentWordReadingStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs
                },
                currentWordReadingStartedAt,
                completedAt);
        }

        wordReadingIndex++;
        currentWordReadingStartedAt = null;

        if (wordReadingIndex >= WordReadingGroups.Length)
        {
            CompleteWordReading();
            return;
        }

        wordReadingPhase = WordReadingPhase.Resting;
        wordReadingRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingCompletedCount", wordReadingIndex, WordReadingGroups.Length);
        UpdateWordReadingRestText();
        NotifyStageChanged();
    }

    private void CompleteWordReading()
    {
        wordReadingTimer.Stop();
        wordReadingPhase = WordReadingPhase.Completed;
        wordReadingRemainingSeconds = 0;
        currentWordReadingStartedAt = null;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingCompleted");
        WordReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceWordReadingCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateWordReadingStatusText()
    {
        WordReadingStatusText = T("CaptureWorkspaceWordReadingRemaining", wordReadingRemainingSeconds);
    }

    private void UpdateWordReadingRestText()
    {
        WordReadingRestText = T("CaptureWorkspaceRestRemaining", wordReadingRemainingSeconds);
    }

}
