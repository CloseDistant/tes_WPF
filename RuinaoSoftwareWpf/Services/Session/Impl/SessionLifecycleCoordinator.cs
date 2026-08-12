namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

public sealed class SessionLifecycleCoordinator : ISessionLifecycleCoordinator
{
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IStimulationStateMachine stimulationStateMachine;
    private readonly IEegRecordingService eegRecordingService;
    private readonly ICaptureMediaService captureMediaService;

    public SessionLifecycleCoordinator(
        IUnifiedSessionService unifiedSessionService,
        IStimulationStateMachine stimulationStateMachine,
        IEegRecordingService eegRecordingService,
        ICaptureMediaService captureMediaService)
    {
        this.unifiedSessionService = unifiedSessionService;
        this.stimulationStateMachine = stimulationStateMachine;
        this.eegRecordingService = eegRecordingService;
        this.captureMediaService = captureMediaService;
        unifiedSessionService.CurrentSessionChanged += (_, _) => CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CurrentSessionChanged;

    public UnifiedSessionContext? CurrentSession => unifiedSessionService.CurrentSession;

    public bool HasRunningModule => stimulationStateMachine.CurrentState is
            StimulationExecutionState.Armed or
            StimulationExecutionState.Starting or
            StimulationExecutionState.Running or
            StimulationExecutionState.Stopping or
            StimulationExecutionState.Faulted
        || eegRecordingService.IsRecording
        || captureMediaService.IsCapturing;

    public async Task<SessionLifecycleResult> EndCurrentAsync(
        string? confirmedSessionKey = null,
        CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
        {
            return new SessionLifecycleResult(false, "当前没有活动 Session。");
        }

        if (HasRunningModule)
        {
            return new SessionLifecycleResult(false, "请先停止电刺激、EEG 和数字表型录制，再结束 Session。");
        }

        if (confirmedSessionKey is null)
        {
            return new SessionLifecycleResult(
                false,
                string.Empty,
                new SessionLifecycleConfirmationRequest(
                    session.SessionKey,
                    "结束当前 Session",
                    "结束后，下一次启动电刺激、EEG 或数字表型时会创建新的 Session。是否继续？",
                    "结束 Session",
                    "取消",
                    "已取消结束 Session。"));
        }

        if (!string.Equals(session.SessionKey, confirmedSessionKey, StringComparison.Ordinal))
        {
            return new SessionLifecycleResult(false, "当前 Session 已发生变化，请重新确认。");
        }

        var ended = await unifiedSessionService.EndAsync(
            "completed",
            "用户结束 Session",
            session.SessionKey,
            cancellationToken);
        if (!ended)
        {
            return new SessionLifecycleResult(false, "当前 Session 已发生变化，请重新确认。");
        }

        return new SessionLifecycleResult(true, "当前 Session 已结束。");
    }

    public async Task<SessionLifecycleResult> PrepareForPatientChangeAsync(
        string action,
        string? confirmedSessionKey = null,
        CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
        {
            return new SessionLifecycleResult(true, string.Empty);
        }

        if (HasRunningModule)
        {
            return new SessionLifecycleResult(false, $"当前 Session 仍有模块运行，无法{action}。");
        }

        if (confirmedSessionKey is null)
        {
            return new SessionLifecycleResult(
                false,
                string.Empty,
                new SessionLifecycleConfirmationRequest(
                    session.SessionKey,
                    action,
                    "当前患者已有活动 Session。继续操作将先结束该 Session，后续数据归入新患者的新 Session。",
                    "结束并继续",
                    "取消",
                    $"已取消{action}。"));
        }

        if (!string.Equals(session.SessionKey, confirmedSessionKey, StringComparison.Ordinal))
        {
            return new SessionLifecycleResult(false, "当前 Session 已发生变化，请重新确认。");
        }

        var ended = await unifiedSessionService.EndAsync(
            "completed",
            action,
            session.SessionKey,
            cancellationToken);
        if (!ended)
        {
            return new SessionLifecycleResult(false, "当前 Session 已发生变化，请重新确认。");
        }

        return new SessionLifecycleResult(true, string.Empty);
    }

    public async Task InterruptForShutdownAsync(CancellationToken cancellationToken = default)
    {
        await unifiedSessionService.EndAsync(
            "interrupted",
            "软件退出",
            cancellationToken: cancellationToken);
    }
}
