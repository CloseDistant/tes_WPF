using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 一个 TI 刺激组，固定包含一对通道（两个 ChannelConfig）。
///
/// 在界面上：
/// - 左侧显示 8 个 TiGroup 的列表。
/// - 选中某个 TiGroup 后，右侧只绑定并显示这个组的 Channels。
/// - 这种“单选组 → 只显示一对通道”的交互，就是参考图要求的效果。
/// </summary>
public sealed class TiGroup : ObservableObject
{
    private string title = string.Empty;
    private bool isSelected;
    private readonly HashSet<ChannelConfig> observedChannels = [];

    public TiGroup()
    {
        Channels.CollectionChanged += OnChannelsChanged;
    }

    /// <summary>组标题，例如 "TI 刺激 7"。</summary>
    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    /// <summary>组内两个通道载波频率的绝对差值，例如 "Δf: 10.0 Hz"。</summary>
    public string DeltaText => TryGetCarrierFrequency(0, out var firstFrequencyHz)
        && TryGetCarrierFrequency(1, out var secondFrequencyHz)
            ? FormattableString.Invariant(
                $"Δf: {Math.Abs(secondFrequencyHz - firstFrequencyHz):0.0} Hz")
            : "Δf: -- Hz";

    /// <summary>当前是否被选中。用于 XAML 中改变选中项的背景色等样式。</summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    /// <summary>
    /// 该组包含的两个通道配置。
    /// 例如 TI 刺激 7 对应 CH 13 和 CH 14。
    /// </summary>
    public ObservableCollection<ChannelConfig> Channels { get; } = new();

    private void OnChannelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeChannelSubscriptions();
        OnPropertyChanged(nameof(DeltaText));
    }

    private void SynchronizeChannelSubscriptions()
    {
        foreach (var removedChannel in observedChannels.Except(Channels).ToArray())
        {
            removedChannel.PropertyChanged -= OnChannelPropertyChanged;
            observedChannels.Remove(removedChannel);
        }

        foreach (var addedChannel in Channels.Except(observedChannels))
        {
            addedChannel.PropertyChanged += OnChannelPropertyChanged;
            observedChannels.Add(addedChannel);
        }
    }

    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChannelConfig.FrequencyHz))
        {
            OnPropertyChanged(nameof(DeltaText));
        }
    }

    private bool TryGetCarrierFrequency(int channelIndex, out decimal frequencyHz)
    {
        frequencyHz = 0;
        if (channelIndex >= Channels.Count)
        {
            return false;
        }

        var value = Channels[channelIndex].FrequencyHz;
        var isValid = decimal.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out frequencyHz)
            || decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out frequencyHz);
        return isValid && frequencyHz >= 0;
    }
}
