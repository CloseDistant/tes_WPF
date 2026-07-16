namespace RuinaoSoftwareWpf;

public interface IEegRecordingService
{
    bool IsRecording { get; }

    Task StartAsync(string recordName, EegAcquisitionConfig config, CancellationToken cancellationToken = default);

    bool TryAppendSamples(EegSampleBatch batch);

    Task AppendSamplesAsync(EegSampleBatch batch, CancellationToken cancellationToken = default);

    Task AddMarkerAsync(EegMarkerRecord marker, CancellationToken cancellationToken = default);

    Task StopAsync(string status = "completed", CancellationToken cancellationToken = default);
}
