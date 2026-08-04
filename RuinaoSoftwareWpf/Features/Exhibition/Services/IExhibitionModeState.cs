namespace RuinaoSoftwareWpf.Features.Exhibition.Services;

/// <summary>
/// 展览版本的显式运行状态。该状态由EXHIBITION编译标识决定，不能在运行时静默切换。
/// </summary>
public interface IExhibitionModeState
{
    bool IsEnabled { get; }
}
