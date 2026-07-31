namespace RuinaoSoftwareWpf;

internal static class StimulationTreatmentLifecycle
{
    public const string RunningStatus = "RUNNING";
    public const string EndedStatus = "ENDED";
    public const string IncompleteStatus = "INCOMPLETE";
    public const string NormalCompletionEndType = "NORMAL_COMPLETION";
    public const string ManualTerminationEndType = "MANUAL_TERMINATION";
    public const string AbnormalTerminationEndType = "ABNORMAL_TERMINATION";

    public static void MarkSoftwareInterrupted(
        StimulationChannelTreatmentEntity channel,
        long updatedAtUnixMs)
    {
        channel.Status = IncompleteStatus;
        channel.EndedAtUnixMs = null;
        channel.EndType = AbnormalTerminationEndType;
        channel.EndReasonCode = StimulationEndReasonCodes.SoftwareInterrupted;
        channel.EndReasonDetail = "上次软件会话未写入正常结束状态。";
        channel.UpdatedAtUnixMs = updatedAtUnixMs;
    }

    public static void RecalculateRunState(StimulationRunEntity run, long updatedAtUnixMs)
    {
        if (run.Channels.Any(item => item.Status == RunningStatus))
        {
            run.Status = RunningStatus;
            run.EndedAtUnixMs = null;
        }
        else if (run.Channels.Any(item => item.Status == IncompleteStatus))
        {
            run.Status = IncompleteStatus;
            run.EndedAtUnixMs = null;
        }
        else
        {
            run.Status = EndedStatus;
            run.EndedAtUnixMs = run.Channels.Max(item => item.EndedAtUnixMs);
        }

        run.UpdatedAtUnixMs = updatedAtUnixMs;
    }

    public static string ToStorageCode(StimulationEndType endType) => endType switch
    {
        StimulationEndType.NormalCompletion => NormalCompletionEndType,
        StimulationEndType.ManualTermination => ManualTerminationEndType,
        StimulationEndType.AbnormalTermination => AbnormalTerminationEndType,
        _ => throw new ArgumentOutOfRangeException(nameof(endType))
    };
}
