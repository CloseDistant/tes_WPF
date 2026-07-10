namespace RuinaoHardwareDebugWpf;

using System.Text.Json;

/// <summary>
/// 刺激控制引擎默认实现。
/// 负责把页面命令串联到安全检查、状态机、硬件服务和审计日志。
/// </summary>
public sealed class StimulationEngine : IStimulationEngine
{
    private readonly IHardwareService hardwareService;
    private readonly ISafetyService safetyService;
    private readonly IStimulationStateMachine stimulationStateMachine;
    private readonly IAuditLogService auditLog;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IRunConfigurationSnapshotService configurationSnapshots;
    private StimulationConfigurationSnapshot? activeConfiguration;

    public StimulationEngine(
        IHardwareService hardwareService,
        ISafetyService safetyService,
        IStimulationStateMachine stimulationStateMachine,
        IAuditLogService auditLog,
        IUnifiedSessionService unifiedSessionService,
        IRunConfigurationSnapshotService configurationSnapshots)
    {
        this.hardwareService = hardwareService;
        this.safetyService = safetyService;
        this.stimulationStateMachine = stimulationStateMachine;
        this.auditLog = auditLog;
        this.unifiedSessionService = unifiedSessionService;
        this.configurationSnapshots = configurationSnapshots;
    }

    public StimulationExecutionState CurrentState => stimulationStateMachine.CurrentState;

    public async Task<HardwareOperationResult> StartTiGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default)
    {
        var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
        activeConfiguration = StimulationConfigurationSnapshot.Create(group);
        configurationSnapshots.Capture(session.SessionKey, SessionModuleCodes.Stimulation, activeConfiguration);
        var executionGroup = activeConfiguration.ToMutableGroup();
        await RecordRequestAsync("start_requested", executionGroup, selectedChannelNames, cancellationToken);
        await safetyService.EnsureCanStartStimulationAsync(cancellationToken);
        stimulationStateMachine.MoveTo(StimulationExecutionState.Running, "StartTiGroup");
        auditLog.RecordUserAction($"Start TI group {group.Title}");
        return await hardwareService.StartGroupAsync(executionGroup, selectedChannelNames, cancellationToken);
    }

    public async Task<HardwareOperationResult> PauseTiGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default)
    {
        var executionGroup = (activeConfiguration ?? StimulationConfigurationSnapshot.Create(group)).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("pause_requested", executionGroup, selectedChannelNames, cancellationToken);
        stimulationStateMachine.MoveTo(StimulationExecutionState.Paused, "PauseTiGroup");
        auditLog.RecordUserAction($"Pause TI group {group.Title}");
        return await hardwareService.PauseGroupAsync(executionGroup, selectedChannelNames, cancellationToken);
    }

    public async Task<HardwareOperationResult> EmergencyStopTiGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default)
    {
        var executionGroup = (activeConfiguration ?? StimulationConfigurationSnapshot.Create(group)).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("emergency_stop_requested", executionGroup, reason, cancellationToken);
        stimulationStateMachine.MoveTo(StimulationExecutionState.EmergencyStopped, $"EmergencyStop:{reason}");
        auditLog.RecordUserAction($"Emergency stop TI group {group.Title}: {reason}");
        var result = await hardwareService.EmergencyStopGroupAsync(executionGroup, reason, cancellationToken);
        activeConfiguration = null;
        configurationSnapshots.Clear(SessionModuleCodes.Stimulation);
        return result;
    }

    private Task RecordRequestIfSessionActiveAsync(
        string eventType,
        TiGroup group,
        string details,
        CancellationToken cancellationToken)
    {
        return unifiedSessionService.CurrentSession is null
            ? Task.CompletedTask
            : RecordRequestAsync(eventType, group, details, cancellationToken);
    }

    private Task RecordRequestAsync(
        string eventType,
        TiGroup group,
        string details,
        CancellationToken cancellationToken)
    {
        return unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.Stimulation,
            eventType,
            group.Title,
            JsonSerializer.Serialize(new { group = group.Title, details }),
            cancellationToken: cancellationToken);
    }
}
