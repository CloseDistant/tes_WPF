namespace RuinaoHardwareDebugWpf;

public interface IEegRecordingRepository
{
    Task<int> RecoverIncompleteEegRecordingsAsync(CancellationToken cancellationToken = default);

    Task<EegRecordingInfo> CreateEegRecordingAsync(
        CaptureSessionInfo captureSession,
        string recordName,
        EegAcquisitionConfig config,
        string outputDirectory,
        int segmentSeconds,
        CancellationToken cancellationToken = default);

    Task AddEegDataSegmentAsync(
        EegRecordingInfo recording,
        EegDataSegmentInfo segment,
        CancellationToken cancellationToken = default);

    Task AddEegMarkersAsync(
        EegRecordingInfo recording,
        IReadOnlyList<EegMarkerRecord> markers,
        CancellationToken cancellationToken = default);

    Task CompleteEegRecordingAsync(
        EegRecordingInfo recording,
        long sampleCount,
        string status,
        CancellationToken cancellationToken = default);
}
