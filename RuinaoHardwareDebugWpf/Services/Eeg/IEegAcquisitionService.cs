namespace RuinaoHardwareDebugWpf;

public interface IEegAcquisitionService
{
    EegAcquisitionState State { get; }

    EegAcquisitionConfig Config { get; }

    IReadOnlyList<EegMarkerTag> MarkerTags { get; }

    event EventHandler<EegAcquisitionState>? StateChanged;

    event EventHandler<EegWaveformRenderModel>? RenderModelUpdated;

    event EventHandler<IReadOnlyList<EegMarkerRecord>>? MarkersChanged;

    event EventHandler<EegSampleBatch>? SamplesGenerated;

    void Configure(EegAcquisitionConfig config);

    Task StartAsync(string recordName, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    void AddMarker(EegMarkerTag tag, string source);

    void ReplaceMarkerTags(IReadOnlyList<EegMarkerTag> markerTags);

    IReadOnlyList<EegMarkerRecord> GetMarkers();

    EegWaveformRenderModel GetCurrentRenderModel();
}
