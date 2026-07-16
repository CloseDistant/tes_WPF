using System.Windows.Media;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 演示数据工厂：创建 8 个 TI 刺激组，每组 2 个通道。
///
/// 对应关系：
/// - 第 1 组：CH 1 / CH 2
/// - 第 2 组：CH 3 / CH 4
/// - ...
/// - 第 7 组：CH 13 / CH 14
/// - 第 8 组：CH 15 / CH 16
///
/// 电极编号按每组 4 个递增：第 n 组从 E(4n-3) 开始。
/// </summary>
public sealed class DemoTiGroupFactory : ITiGroupFactory
{
    /// <summary>
    /// 创建 8 个演示 TI 刺激组。
    /// </summary>
    public IReadOnlyList<TiGroup> CreateDemoGroups()
    {
        var groups = new List<TiGroup>();

        for (var groupIndex = 1; groupIndex <= 8; groupIndex++)
        {
            // 根据组索引计算对应的通道号和电极号。
            var firstChannel = groupIndex * 2 - 1;
            var secondChannel = groupIndex * 2;
            var firstElectrode = groupIndex * 4 - 3;

            // 通道名称统一使用浅色；当前选中状态由卡片边框高亮表达。
            var accent = new SolidColorBrush(Color.FromRgb(228, 232, 239));
            accent.Freeze(); // Freeze 可以提高性能并允许跨线程使用。

            groups.Add(new TiGroup
            {
                Title = $"TI 刺激 {groupIndex}",
                DeltaText = "Δf: 10.0 Hz",
                Channels =
                {
                    new ChannelConfig
                    {
                        Name = $"CH {firstChannel}",
                        Anode = $"E{firstElectrode}",
                        Cathode = $"E{firstElectrode + 1}",
                        CurrentMA = groupIndex == 7 ? "0.90" : string.Empty,
                        RampUpS = "0.5",
                        RampDownS = "0.5",
                        DurationS = "1200",
                        IntervalS = "0",
                        SingleDurationS = "60",
                        FrequencyHz = "1000",
                        Polarity = "不掉转",
                        AccentBrush = accent
                    },
                    new ChannelConfig
                    {
                        Name = $"CH {secondChannel}",
                        Anode = $"E{firstElectrode + 2}",
                        Cathode = $"E{firstElectrode + 3}",
                        CurrentMA = groupIndex == 7 ? "0.90" : string.Empty,
                        RampUpS = "0.5",
                        RampDownS = "0.5",
                        DurationS = "1200",
                        IntervalS = "0",
                        SingleDurationS = "60",
                        FrequencyHz = groupIndex == 7 ? "1130" : "1010",
                        Polarity = "不掉转",
                        AccentBrush = accent
                    }
                }
            });
        }

        return groups;
    }
}
