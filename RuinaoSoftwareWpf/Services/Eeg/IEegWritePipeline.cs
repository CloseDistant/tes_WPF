namespace RuinaoSoftwareWpf;

public interface IEegWritePipeline : IAsyncDisposable
{
    bool IsRunning { get; }
    int QueueDepth { get; }
    int Capacity { get; }

    void Start(Func<EegSampleBatch, CancellationToken, Task> consumer);
    bool TryEnqueue(EegSampleBatch batch);
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
