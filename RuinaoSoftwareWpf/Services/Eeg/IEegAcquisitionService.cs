namespace RuinaoSoftwareWpf;

/// <summary>
/// 旧 EEG 界面契约，暂时保留波形渲染模型和 WPF 标记颜色。
/// 新的应用层调用必须使用 Application.IEegAcquisitionService。
/// </summary>
public interface ILegacyEegAcquisitionService
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
