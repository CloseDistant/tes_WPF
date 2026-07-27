namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Windows.Media;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 将现有包含渲染模型的 EEG 服务适配为纯应用层采集契约。
/// 波形分页和 WPF 颜色仍只存在于旧 Presentation 契约中。
/// </summary>
public sealed class LegacyEegAcquisitionServiceAdapter : ApplicationContracts.IEegAcquisitionService
{
    private readonly ILegacyEegAcquisitionService legacy;

    public LegacyEegAcquisitionServiceAdapter(ILegacyEegAcquisitionService legacy)
    {
        this.legacy = legacy;
        legacy.StateChanged += OnStateChanged;
        legacy.SamplesGenerated += OnSamplesGenerated;
    }

    public EegAcquisitionStatus Status => MapState(legacy.State);

    public EegAcquisitionOptions Options => MapOptions(legacy.Config);

    public event EventHandler<EegAcquisitionStatus>? StatusChanged;

    public event EventHandler<EegSampleBlock>? SamplesReceived;

    public void Configure(EegAcquisitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ChannelCount <= 0
            || options.SampleRateHz <= 0
            || options.ChannelNames.Count != options.ChannelCount
            || options.HardwareGain <= 0)
        {
            throw new ArgumentException("EEG 采集参数不合法。", nameof(options));
        }

        legacy.Configure(new EegAcquisitionConfig
        {
            ChannelCount = options.ChannelCount,
            SampleRateHz = options.SampleRateHz,
            ChannelNames = options.ChannelNames.ToArray(),
            HighPassHz = options.HighPassHz,
            LowPassHz = options.LowPassHz,
            NotchHz = options.NotchHz,
            HardwareGain = options.HardwareGain
        });
    }

    public Task StartAsync(
        string recordingName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recordingName))
        {
            throw new ArgumentException("EEG 记录名称不能为空。", nameof(recordingName));
        }

        return legacy.StartAsync(recordingName, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return legacy.StopAsync(cancellationToken);
    }

    public void AddMarker(EegMarkerDefinition marker, string source)
    {
        ArgumentNullException.ThrowIfNull(marker);
        legacy.AddMarker(
            new EegMarkerTag(
                marker.DisplayName,
                marker.Shortcut,
                ParseColor(marker.ColorHex)),
            source);
    }

    public IReadOnlyList<EegMarker> GetMarkers()
    {
        return legacy.GetMarkers()
            .Select(marker => new EegMarker(
                CreateMarkerCode(marker.Name),
                marker.Name,
                marker.Shortcut,
                ToColorHex(marker.Color),
                marker.AbsoluteTimestampMs,
                marker.ExperimentTime,
                marker.SampleIndex,
                marker.Source))
            .ToArray();
    }

    private void OnStateChanged(object? sender, EegAcquisitionState state)
    {
        StatusChanged?.Invoke(this, MapState(state));
    }

    private void OnSamplesGenerated(object? sender, EegSampleBatch batch)
    {
        SamplesReceived?.Invoke(
            this,
            new EegSampleBlock(
                batch.ChannelSamples,
                batch.StartSampleIndex,
                batch.SampleCount,
                batch.ReceivedAt));
    }

    private static EegAcquisitionOptions MapOptions(EegAcquisitionConfig config)
    {
        return new EegAcquisitionOptions(
            config.ChannelCount,
            config.SampleRateHz,
            config.ChannelNames,
            config.HighPassHz,
            config.LowPassHz,
            config.NotchHz,
            config.HardwareGain);
    }

    private static EegAcquisitionStatus MapState(EegAcquisitionState state)
    {
        return state switch
        {
            EegAcquisitionState.Ready => EegAcquisitionStatus.Ready,
            EegAcquisitionState.Acquiring => EegAcquisitionStatus.Acquiring,
            EegAcquisitionState.Stopped => EegAcquisitionStatus.Stopped,
            _ => EegAcquisitionStatus.Idle
        };
    }

    private static string CreateMarkerCode(string name)
    {
        return string.Join(
            "-",
            name.Trim()
                .ToUpperInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-'))
            .Trim('-');
    }

    private static Color ParseColor(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex)
            || ColorConverter.ConvertFromString(colorHex) is not Color color)
        {
            throw new FormatException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "无效的 EEG 标记颜色：{0}",
                    colorHex));
        }

        return color;
    }

    private static string ToColorHex(Color color)
    {
        return FormattableString.Invariant(
            $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}
