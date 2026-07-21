namespace RuinaoSoftwareWpf;

internal static class SessionSecurityPolicy
{
    public static bool IsValidIdleTimeout(int minutes)
    {
        return minutes is >= ISessionSecurityService.MinimumIdleTimeoutMinutes
            and <= ISessionSecurityService.MaximumIdleTimeoutMinutes;
    }

    public static int ParseIdleTimeoutOrDefault(string? value)
    {
        return int.TryParse(value, out var minutes) && IsValidIdleTimeout(minutes)
            ? minutes
            : ISessionSecurityService.DefaultIdleTimeoutMinutes;
    }

    public static bool ShouldSuppressAutoLock(
        bool hasRunningModule,
        bool hasActiveAssessment)
    {
        return hasRunningModule || hasActiveAssessment;
    }

    public static bool HasIdleTimeoutElapsed(TimeSpan elapsed, int timeoutMinutes)
    {
        return elapsed >= TimeSpan.FromMinutes(timeoutMinutes);
    }
}
