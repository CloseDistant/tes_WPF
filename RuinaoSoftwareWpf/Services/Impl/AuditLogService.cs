namespace RuinaoSoftwareWpf;

/// <summary>
/// 兼容既有状态机同步审计接口，正式数据写入独立安全审计数据库。
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditTrailService auditTrail;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;

    public AuditLogService(
        IAuditTrailService auditTrail,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.auditTrail = auditTrail;
        this.accountService = accountService;
        this.logger = logger;
    }

    public void RecordStateTransition<TState>(StateTransition<TState> transition)
    {
        var isStimulationOrDevice = typeof(TState).Name.Contains("Stimulation", StringComparison.Ordinal)
            || typeof(TState).Name.Contains("Device", StringComparison.Ordinal);
        if (!isStimulationOrDevice)
        {
            return;
        }

        AppendSafely(new AuditEventInput(
            AuditEventCategory.StimulationDevice,
            $"STATE_{typeof(TState).Name}",
            Actor(transition.OperatorId),
            typeof(TState).Name,
            transition.To?.ToString() ?? string.Empty,
            AuditEventResult.Success,
            Reason: $"from={transition.From}, trigger={transition.Trigger}"));
    }

    public void RecordUserAction(string action, string operatorId = "system")
    {
        var mapped = AuditActionCatalog.FromLegacyAction(action);
        if (mapped.Category == AuditEventCategory.AuditSystem)
        {
            return;
        }

        AppendSafely(new AuditEventInput(
            mapped.Category,
            mapped.ActionCode,
            Actor(operatorId),
            "Application",
            string.Empty,
            AuditEventResult.Success));
    }

    public void RecordHardwareCommunication(string direction, string command, string details)
    {
        AppendSafely(new AuditEventInput(
            AuditEventCategory.StimulationDevice,
            $"HARDWARE_{direction}",
            AuditActor.System,
            "HardwareCommand",
            command,
            AuditEventResult.Success,
            Reason: details));
    }

    public void RecordSafetyEvent(SafetyEvaluationResult result, string operatorId = "system")
    {
        AppendSafely(new AuditEventInput(
            AuditEventCategory.StimulationDevice,
            $"SAFETY_{result.Action}",
            Actor(operatorId),
            "SafetyEvaluation",
            result.Action.ToString(),
            AuditEventResult.Blocked,
            "SAFETY_CHECK_BLOCKED",
            result.Reason));
    }

    private AuditActor Actor(string operatorId)
    {
        if (accountService.CurrentUser is { } currentUser
            && (string.Equals(operatorId, "system", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operatorId, currentUser.LoginName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(operatorId, currentUser.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)))
        {
            return AuditActor.From(currentUser);
        }

        return string.Equals(operatorId, "system", StringComparison.OrdinalIgnoreCase)
            ? AuditActor.System
            : new AuditActor(null, operatorId, null);
    }

    private void AppendSafely(AuditEventInput auditEvent)
    {
        try
        {
            auditTrail.AppendAsync(auditEvent).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logger.Error($"安全审计写入失败：action={auditEvent.ActionCode}", exception);
        }
    }
}
