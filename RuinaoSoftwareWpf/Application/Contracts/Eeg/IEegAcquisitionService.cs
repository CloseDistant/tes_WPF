namespace RuinaoSoftwareWpf.ApplicationContracts;

public enum EegAcquisitionStatus
{
    Idle,
    Ready,
    Acquiring,
    Stopped,
    Faulted
}

public sealed record EegAcquisitionOptions(
    int ChannelCount,
    int SampleRateHz,
    IReadOnlyList<string> ChannelNames,
    double? HighPassHz = null,
    double? LowPassHz = null,
    double? NotchHz = null,
    int HardwareGain = 1);

public sealed record EegMarkerDefinition(
    string Code,
    string DisplayName,
    string Shortcut,
    string ColorHex);

public sealed record EegMarker(
    string Code,
    string DisplayName,
    string Shortcut,
    string ColorHex,
    long AbsoluteTimestampMilliseconds,
    TimeSpan ExperimentTime,
    long SampleIndex,
    string Source);

public sealed record EegSampleBlock(
    double[][] ChannelSamples,
    long StartSampleIndex,
    int SampleCount,
    DateTimeOffset ReceivedAt);

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
