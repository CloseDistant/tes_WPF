namespace RuinaoSoftwareWpf;

using System.Collections.Concurrent;
using System.Text.Json;

public sealed class RunConfigurationSnapshotService : IRunConfigurationSnapshotService
{
    private readonly ConcurrentDictionary<string, RunConfigurationSnapshot> snapshots = new(StringComparer.Ordinal);
    private long version;

    public RunConfigurationSnapshot Capture<T>(string sessionKey, string moduleCode, T configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        ArgumentNullException.ThrowIfNull(configuration);

        var snapshot = new RunConfigurationSnapshot(
            sessionKey,
            moduleCode,
            Interlocked.Increment(ref version),
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(configuration));
        snapshots[moduleCode] = snapshot;
        return snapshot;
    }

    public RunConfigurationSnapshot? GetCurrent(string moduleCode)
    {
        return snapshots.TryGetValue(moduleCode, out var snapshot) ? snapshot : null;
    }

    public void Clear(string moduleCode)
    {
        snapshots.TryRemove(moduleCode, out _);
    }
}
