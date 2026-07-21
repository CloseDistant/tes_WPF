namespace RuinaoSoftwareWpf;

internal static class AuditActionCatalog
{
    private static readonly IReadOnlyDictionary<string, (AuditEventCategory Category, string ActionCode)> LegacyActions =
        new Dictionary<string, (AuditEventCategory, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["login"] = (AuditEventCategory.IdentitySession, "LOGIN"),
            ["logout"] = (AuditEventCategory.IdentitySession, "LOGOUT"),
            ["switch_account"] = (AuditEventCategory.IdentitySession, "SWITCH_ACCOUNT"),
            ["session_timeout"] = (AuditEventCategory.IdentitySession, "SESSION_TIMEOUT"),
            ["session_lock"] = (AuditEventCategory.IdentitySession, "SESSION_LOCK"),
            ["session_unlock"] = (AuditEventCategory.IdentitySession, "SESSION_UNLOCK"),
            ["create_user"] = (AuditEventCategory.AccountAuthorization, "CREATE_USER"),
            ["view_account_list"] = (AuditEventCategory.AccountAuthorization, "VIEW_ACCOUNT_LIST"),
            ["change_password"] = (AuditEventCategory.AccountAuthorization, "CHANGE_PASSWORD"),
            ["force_change_password"] = (AuditEventCategory.AccountAuthorization, "FORCE_CHANGE_PASSWORD"),
            ["reset_password"] = (AuditEventCategory.AccountAuthorization, "RESET_PASSWORD"),
            ["create_patient"] = (AuditEventCategory.PatientManagement, "CREATE_PATIENT"),
            ["update_patient"] = (AuditEventCategory.PatientManagement, "UPDATE_PATIENT"),
            ["switch_patient"] = (AuditEventCategory.PatientManagement, "SWITCH_PATIENT"),
            ["update_feature_visibility"] = (AuditEventCategory.SecurityConfiguration, "UPDATE_FEATURE_VISIBILITY"),
            ["update_startup_settings"] = (AuditEventCategory.SecurityConfiguration, "UPDATE_STARTUP_SETTINGS"),
            ["update_session_security_settings"] = (AuditEventCategory.SecurityConfiguration, "UPDATE_SESSION_SECURITY_SETTINGS"),
            ["Connect device"] = (AuditEventCategory.StimulationDevice, "DEVICE_CONNECT"),
            ["Disconnect device"] = (AuditEventCategory.StimulationDevice, "DEVICE_DISCONNECT"),
            ["Handshake check"] = (AuditEventCategory.StimulationDevice, "HANDSHAKE_CHECK"),
            ["Impedance check"] = (AuditEventCategory.StimulationDevice, "IMPEDANCE_CHECK"),
            ["Use prescription"] = (AuditEventCategory.PrescriptionManagement, "USE_PRESCRIPTION"),
            ["Reuse treatment record"] = (AuditEventCategory.PrescriptionManagement, "REUSE_TREATMENT_RECORD")
        };

    public static (AuditEventCategory Category, string ActionCode) FromLegacyAction(string action)
    {
        if (LegacyActions.TryGetValue(action, out var mapped))
        {
            return mapped;
        }

        var normalized = NormalizeActionCode(action);
        if (normalized.StartsWith("START_TI_GROUP", StringComparison.Ordinal)
            || normalized.StartsWith("START_TDCS_GROUP", StringComparison.Ordinal))
        {
            return (AuditEventCategory.StimulationDevice, "STIMULATION_START");
        }

        if (normalized.StartsWith("PAUSE_TI_GROUP", StringComparison.Ordinal))
        {
            return (AuditEventCategory.StimulationDevice, "STIMULATION_PAUSE");
        }

        if (normalized.StartsWith("EMERGENCY_STOP", StringComparison.Ordinal))
        {
            return (AuditEventCategory.StimulationDevice, "EMERGENCY_STOP");
        }

        if (normalized.StartsWith("COMPLETE_", StringComparison.Ordinal))
        {
            return (AuditEventCategory.StimulationDevice, "STIMULATION_STOP");
        }

        return (AuditEventCategory.AuditSystem, normalized);
    }

    public static AuditEventResult ParseResult(string result)
    {
        return result.Trim().ToLowerInvariant() switch
        {
            "success" => AuditEventResult.Success,
            "blocked" => AuditEventResult.Blocked,
            _ => AuditEventResult.Failed
        };
    }

    public static string NormalizeActionCode(string action)
    {
        var value = string.IsNullOrWhiteSpace(action) ? "UNKNOWN_ACTION" : action.Trim();
        return value.Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
    }
}
