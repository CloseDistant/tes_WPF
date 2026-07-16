namespace RuinaoSoftwareWpf;

public interface IEegSegmentFileWriter : IAsyncDisposable
{
    void Start(string outputDirectory, long recordingId, EegAcquisitionConfig config, int segmentSeconds, long startedAtUnixMs);

    Task<IReadOnlyList<EegDataSegmentInfo>> AppendBatchAsync(EegSampleBatch batch, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EegDataSegmentInfo>> StopAsync(CancellationToken cancellationToken = default);
}
