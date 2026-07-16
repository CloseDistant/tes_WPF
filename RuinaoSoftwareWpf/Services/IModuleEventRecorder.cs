namespace RuinaoSoftwareWpf;

public interface IModuleEventRecorder
{
    void Enqueue(
        string eventType,
        string message,
        object payload,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
