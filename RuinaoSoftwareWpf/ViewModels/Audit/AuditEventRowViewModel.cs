namespace RuinaoSoftwareWpf;

using System.Globalization;

public sealed class AuditEventRowViewModel
{
    public AuditEventRowViewModel(AuditEventRecord record)
    {
        TimeText = record.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ActorText = record.ActorLoginName;
        RoleText = AuditDisplayNames.Role(record.ActorRoleId);
        CategoryText = AuditDisplayNames.Category(record.Category);
        ActionText = AuditDisplayNames.Action(record.ActionCode);
        ResultText = AuditDisplayNames.Result(record.Result);
        ResultForeground = record.Result switch
        {
            AuditEventResult.Success => "#73D995",
            AuditEventResult.Blocked => "#E7BE6D",
            _ => "#F29696"
        };
        ResultBackground = record.Result switch
        {
            AuditEventResult.Success => "#20372B",
            AuditEventResult.Blocked => "#3A3022",
            _ => "#3A2529"
        };
        ReasonText = record.Reason ?? record.FailureCode ?? string.Empty;
    }

    public string TimeText { get; }
    public string ActorText { get; }
    public string RoleText { get; }
    public string CategoryText { get; }
    public string ActionText { get; }
    public string ResultText { get; }
    public string ResultForeground { get; }
    public string ResultBackground { get; }
    public string ReasonText { get; }
}
