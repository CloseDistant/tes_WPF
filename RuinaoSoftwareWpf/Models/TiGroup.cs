using System.Collections.ObjectModel;

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
    private string deltaText = string.Empty;
    private bool isSelected;

    /// <summary>组标题，例如 "TI 刺激 7"。</summary>
    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    /// <summary>差频显示文本，例如 "Δf: 10.0 Hz"。</summary>
    public string DeltaText
    {
        get => deltaText;
        set => SetProperty(ref deltaText, value);
    }

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
}
