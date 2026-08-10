namespace RuinaoSoftwareWpf;

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
    private readonly IPatientService patientService;
    private readonly IAuthorizationService authorizationService;
    private readonly object activeChannelsSync = new();
    private readonly HashSet<string> activeChannelNames = new(StringComparer.Ordinal);
    private StimulationConfigurationSnapshot? activeConfiguration;

    public StimulationEngine(
        IHardwareService hardwareService,
        ISafetyService safetyService,
        IStimulationStateMachine stimulationStateMachine,
        IAuditLogService auditLog,
        IUnifiedSessionService unifiedSessionService,
        IRunConfigurationSnapshotService configurationSnapshots,
        IPatientService patientService,
        IAuthorizationService authorizationService)
    {
        this.hardwareService = hardwareService;
        this.safetyService = safetyService;
        this.stimulationStateMachine = stimulationStateMachine;
        this.auditLog = auditLog;
        this.unifiedSessionService = unifiedSessionService;
        this.configurationSnapshots = configurationSnapshots;
        this.patientService = patientService;
        this.authorizationService = authorizationService;
    }

    public StimulationExecutionState CurrentState => stimulationStateMachine.CurrentState;

    public async Task<HardwareOperationResult> StartTiGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        activeConfiguration = StimulationConfigurationSnapshot.Create(group);
        var parameterRecord = StimulationRecordParameters.CreateTiPrescription(group, prescriptionName);
        var executionGroup = activeConfiguration.ToMutableGroup();
        if (patientService.CurrentPatient is not null)
        {
            var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
            configurationSnapshots.Capture(session.SessionKey, SessionModuleCodes.Stimulation, activeConfiguration);
            await RecordRequestAsync("start_requested", executionGroup, selectedChannelNames, cancellationToken);
        }

        await safetyService.EnsureCanStartStimulationAsync(cancellationToken);
        auditLog.RecordUserAction($"Start TI group {group.Title}");
        return await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Starting,
            "StartTiGroupRequested",
            () => RegisterActiveChannels(executionGroup),
            "StartTiGroupConfirmed",
            token => hardwareService.StartGroupAsync(executionGroup, selectedChannelNames, parameterRecord, token),
            cancellationToken);
    }

    public async Task<HardwareOperationResult> StartDirectCurrentGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        activeConfiguration = StimulationConfigurationSnapshot.Create(group);
        var parameterRecord = StimulationRecordParameters.CreateDirectCurrentPrescription(group, prescriptionName);
        var executionGroup = activeConfiguration.ToMutableGroup();
        if (patientService.CurrentPatient is not null)
        {
            var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
            configurationSnapshots.Capture(session.SessionKey, SessionModuleCodes.Stimulation, activeConfiguration);
            await RecordRequestAsync("start_requested", executionGroup, selectedChannelNames, cancellationToken);
        }

        await safetyService.EnsureCanStartStimulationAsync(cancellationToken);
        auditLog.RecordUserAction($"Start tDCS group {group.Title}");
        return await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Starting,
            "StartDirectCurrentGroupRequested",
            () => RegisterActiveChannels(executionGroup),
            "StartDirectCurrentGroupConfirmed",
            token => hardwareService.StartGroupAsync(executionGroup, selectedChannelNames, parameterRecord, token),
            cancellationToken);
    }

    public async Task<HardwareOperationResult> StartMonophasicPulseCurrentGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        activeConfiguration = StimulationConfigurationSnapshot.Create(group);
        var parameterRecord = StimulationRecordParameters.CreateMonophasicPulseCurrentPrescription(
            group,
            prescriptionName);
        var executionGroup = activeConfiguration.ToMutableGroup();
        if (patientService.CurrentPatient is not null)
        {
            var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
            configurationSnapshots.Capture(session.SessionKey, SessionModuleCodes.Stimulation, activeConfiguration);
            await RecordRequestAsync("start_requested", executionGroup, selectedChannelNames, cancellationToken);
        }

        await safetyService.EnsureCanStartStimulationAsync(cancellationToken);
        auditLog.RecordUserAction($"Start M-tPCS group {group.Title}");
        return await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Starting,
            "StartMonophasicPulseCurrentGroupRequested",
            () => RegisterActiveChannels(executionGroup),
            "StartMonophasicPulseCurrentGroupConfirmed",
            token => hardwareService.StartGroupAsync(executionGroup, selectedChannelNames, parameterRecord, token),
            cancellationToken);
    }

    public async Task<HardwareOperationResult> StopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        var executionGroup = StimulationConfigurationSnapshot.Create(group).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("stop_requested", executionGroup, selectedChannelNames, cancellationToken);
        auditLog.RecordUserAction($"Stop {stimulationType} group {group.Title}");
        var result = await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Stopping,
            $"StopGroupRequested:{selectedChannelNames}",
            () => CompleteActiveChannels(executionGroup, StimulationExecutionState.Stopped),
            $"StopGroupConfirmed:{selectedChannelNames}",
            token => hardwareService.StopGroupAsync(
                executionGroup,
                selectedChannelNames,
                stimulationType,
                token),
            cancellationToken);
        await RecordRequestIfSessionActiveAsync("stopped", executionGroup, selectedChannelNames, cancellationToken);
        ClearConfigurationWhenIdle();
        return result;
    }

    public async Task<HardwareOperationResult> EmergencyStopTiGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default)
    {
        var executionGroup = StimulationConfigurationSnapshot.Create(group).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("emergency_stop_requested", executionGroup, reason, cancellationToken);
        auditLog.RecordUserAction($"Emergency stop TI group {group.Title}: {reason}");
        var result = await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Stopping,
            $"EmergencyStopTiRequested:{reason}",
            () => ClearActiveChannels(StimulationExecutionState.EmergencyStopped),
            $"EmergencyStopTiConfirmed:{reason}",
            token => hardwareService.EmergencyStopGroupAsync(
                executionGroup,
                reason,
                StimulationModeCodes.TemporalInterference,
                token),
            cancellationToken);
        activeConfiguration = null;
        configurationSnapshots.Clear(SessionModuleCodes.Stimulation);
        return result;
    }

    public async Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(
        TiGroup group,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var executionGroup = StimulationConfigurationSnapshot.Create(group).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("emergency_stop_requested", executionGroup, reason, cancellationToken);
        auditLog.RecordUserAction($"Emergency stop tDCS group {group.Title}: {reason}");
        var result = await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Stopping,
            $"EmergencyStopDirectCurrentRequested:{reason}",
            () => ClearActiveChannels(StimulationExecutionState.EmergencyStopped),
            $"EmergencyStopDirectCurrentConfirmed:{reason}",
            token => hardwareService.EmergencyStopGroupAsync(
                executionGroup,
                reason,
                StimulationModeCodes.DirectCurrent,
                token),
            cancellationToken);
        activeConfiguration = null;
        configurationSnapshots.Clear(SessionModuleCodes.Stimulation);
        return result;
    }

    public async Task<HardwareOperationResult> EmergencyStopMonophasicPulseCurrentGroupAsync(
        TiGroup group,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var executionGroup = StimulationConfigurationSnapshot.Create(group).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("emergency_stop_requested", executionGroup, reason, cancellationToken);
        auditLog.RecordUserAction($"Emergency stop M-tPCS group {group.Title}: {reason}");
        var result = await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Stopping,
            $"EmergencyStopMonophasicPulseCurrentRequested:{reason}",
            () => ClearActiveChannels(StimulationExecutionState.EmergencyStopped),
            $"EmergencyStopMonophasicPulseCurrentConfirmed:{reason}",
            token => hardwareService.EmergencyStopGroupAsync(
                executionGroup,
                reason,
                StimulationModeCodes.MonophasicPulseCurrent,
                token),
            cancellationToken);
        activeConfiguration = null;
        configurationSnapshots.Clear(SessionModuleCodes.Stimulation);
        return result;
    }

    public async Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        var executionGroup = StimulationConfigurationSnapshot.Create(group).ToMutableGroup();
        await RecordRequestIfSessionActiveAsync("complete_requested", executionGroup, selectedChannelNames, cancellationToken);
        auditLog.RecordUserAction($"Complete {stimulationType} group {group.Title}: {selectedChannelNames}");
        var result = await ExecuteConfirmedTransitionAsync(
            StimulationExecutionState.Stopping,
            $"CompleteRequested:{selectedChannelNames}",
            () => CompleteActiveChannels(executionGroup, StimulationExecutionState.Completed),
            $"CompleteConfirmed:{selectedChannelNames}",
            token => hardwareService.CompleteGroupAsync(
                executionGroup,
                selectedChannelNames,
                stimulationType,
                token),
            cancellationToken);
        await RecordRequestIfSessionActiveAsync("completed", executionGroup, selectedChannelNames, cancellationToken);
        ClearConfigurationWhenIdle();
        return result;
    }

    private async Task<HardwareOperationResult> ExecuteConfirmedTransitionAsync(
        StimulationExecutionState pendingState,
        string pendingTrigger,
        Func<StimulationExecutionState> getConfirmedState,
        string confirmedTrigger,
        Func<CancellationToken, Task<HardwareOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        stimulationStateMachine.MoveTo(pendingState, pendingTrigger);
        try
        {
            var result = await operation(cancellationToken);
            var confirmedState = getConfirmedState();
            stimulationStateMachine.MoveTo(confirmedState, confirmedTrigger);
            return result;
        }
        catch (Exception exception)
        {
            stimulationStateMachine.MoveTo(
                StimulationExecutionState.Faulted,
                $"{pendingTrigger}Failed:{exception.GetType().Name}");
            throw;
        }
    }

    private StimulationExecutionState RegisterActiveChannels(TiGroup group)
    {
        lock (activeChannelsSync)
        {
            foreach (var channel in group.Channels)
            {
                activeChannelNames.Add(channel.Name);
            }
        }

        return StimulationExecutionState.Running;
    }

    private StimulationExecutionState CompleteActiveChannels(
        TiGroup group,
        StimulationExecutionState terminalState)
    {
        lock (activeChannelsSync)
        {
            foreach (var channel in group.Channels)
            {
                activeChannelNames.Remove(channel.Name);
            }

            return activeChannelNames.Count == 0
                ? terminalState
                : StimulationExecutionState.Running;
        }
    }

    private StimulationExecutionState ClearActiveChannels(StimulationExecutionState terminalState)
    {
        lock (activeChannelsSync)
        {
            activeChannelNames.Clear();
        }

        return terminalState;
    }

    private void ClearConfigurationWhenIdle()
    {
        lock (activeChannelsSync)
        {
            if (activeChannelNames.Count > 0)
            {
                return;
            }
        }

        activeConfiguration = null;
        configurationSnapshots.Clear(SessionModuleCodes.Stimulation);
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
