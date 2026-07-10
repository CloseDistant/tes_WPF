namespace RuinaoHardwareDebugWpf;

/// <summary>
/// TI 刺激组工厂接口。
/// 负责创建界面左侧显示的 8 个 TI 刺激组（每组 2 个通道）。
/// 使用接口是为了方便测试时换用假数据。
/// </summary>
public interface ITiGroupFactory
{
    /// <summary>
    /// 创建一组演示用的 TI 刺激组。
    /// </summary>
    IReadOnlyList<TiGroup> CreateDemoGroups();
}
