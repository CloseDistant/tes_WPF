namespace RuinaoHardwareDebugWpf;

public sealed class SessionLifecycleCoordinator : ISessionLifecycleCoordinator
{
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IStimulationStateMachine stimulationStateMachine;
    private readonly IEegRecordingService eegRecordingService;
    private readonly ICaptureMediaRecorder captureMediaRecorder;
    private readonly IUserDialogService userDialogService;

    public SessionLifecycleCoordinator(
        IUnifiedSessionService unifiedSessionService,
        IStimulationStateMachine stimulationStateMachine,
        IEegRecordingService eegRecordingService,
        ICaptureMediaRecorder captureMediaRecorder,
        IUserDialogService userDialogService)
    {
        this.unifiedSessionService = unifiedSessionService;
        this.stimulationStateMachine = stimulationStateMachine;
        this.eegRecordingService = eegRecordingService;
        this.captureMediaRecorder = captureMediaRecorder;
        this.userDialogService = userDialogService;
        unifiedSessionService.CurrentSessionChanged += (_, _) => CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CurrentSessionChanged;

    public UnifiedSessionContext? CurrentSession => unifiedSessionService.CurrentSession;

    public bool HasRunningModule => stimulationStateMachine.CurrentState == StimulationExecutionState.Running
        || eegRecordingService.IsRecording
        || captureMediaRecorder.IsRecording;

    public async Task<SessionLifecycleResult> EndCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSession is null)
        {
            return new SessionLifecycleResult(false, "当前没有活动 Session。");
        }

        if (HasRunningModule)
        {
            return new SessionLifecycleResult(false, "请先停止电刺激、EEG 和数字表型录制，再结束 Session。");
        }

        if (!userDialogService.ConfirmWarning(
                "结束当前 Session",
                "结束后，下一次启动电刺激、EEG 或数字表型时会创建新的 Session。是否继续？",
                "结束 Session",
                "取消"))
        {
            return new SessionLifecycleResult(false, "已取消结束 Session。");
        }

        await unifiedSessionService.EndAsync("completed", "用户结束 Session", cancellationToken);
        return new SessionLifecycleResult(true, "当前 Session 已结束。");
    }

    public async Task<SessionLifecycleResult> PrepareForPatientChangeAsync(
        string action,
        CancellationToken cancellationToken = default)
    {
        if (CurrentSession is null)
        {
            return new SessionLifecycleResult(true, string.Empty);
        }

        if (HasRunningModule)
        {
            return new SessionLifecycleResult(false, $"当前 Session 仍有模块运行，无法{action}。");
        }

        if (!userDialogService.ConfirmWarning(
                action,
                "当前患者已有活动 Session。继续操作将先结束该 Session，后续数据归入新患者的新 Session。",
                "结束并继续",
                "取消"))
        {
            return new SessionLifecycleResult(false, $"已取消{action}。");
        }

        await unifiedSessionService.EndAsync("completed", action, cancellationToken);
        return new SessionLifecycleResult(true, string.Empty);
    }

    public Task InterruptForShutdownAsync(CancellationToken cancellationToken = default)
    {
        return unifiedSessionService.EndAsync("interrupted", "软件退出", cancellationToken);
    }
}
