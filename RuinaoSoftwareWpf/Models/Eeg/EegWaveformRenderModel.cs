namespace RuinaoSoftwareWpf;

public sealed record EegWaveformRenderModel(
    EegAcquisitionConfig Config,
    double[][] PageSamples,
    int PageIndex,
    int PageSampleIndex,
    long TotalSamples,
    TimeSpan Elapsed,
    bool IsRecording,
    IReadOnlyList<EegMarkerRecord> CurrentPageMarkers,
    IReadOnlyList<EegMarkerRecord> AllMarkers);
