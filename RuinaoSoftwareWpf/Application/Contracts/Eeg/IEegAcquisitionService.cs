namespace RuinaoSoftwareWpf.ApplicationContracts;

public interface IEegAcquisitionService
{
    EegAcquisitionStatus Status { get; }

    EegAcquisitionOptions Options { get; }

    event EventHandler<EegAcquisitionStatus>? StatusChanged;

    event EventHandler<EegSampleBlock>? SamplesReceived;

    void Configure(EegAcquisitionOptions options);

    Task StartAsync(string recordingName, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    void AddMarker(EegMarkerDefinition marker, string source);

    IReadOnlyList<EegMarker> GetMarkers();
}
