namespace RuinaoSoftwareWpf;

/// <summary>
/// 主界面上下文接口。
///
/// 一些子 ViewModel（如 FemSimulationViewModel）需要读取主界面级共享状态，
/// 但又不想直接依赖庞大的 MainViewModel。于是定义这个最小接口，只暴露它们真正需要的东西。
///
/// 这样做的好处：
/// - 子 VM 不需要知道 MainViewModel 的全部细节。
/// - 测试子 VM 时，可以用一个假实现（Mock）代替 MainViewModel。
/// </summary>
public interface IMainUiContext
{
    /// <summary>多语言 ViewModel，子页面可以读取文本。</summary>
    LocalizationViewModel Localization { get; }
}
