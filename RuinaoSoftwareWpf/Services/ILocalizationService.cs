namespace RuinaoSoftwareWpf;

/// <summary>
/// 多语言服务接口。
/// 通过 key 获取文本，支持中英文切换。
/// 新增语言时，只需替换实现类，不需要改 ViewModel。
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 当前是否为中文。
    /// </summary>
    bool IsChinese { get; }

    /// <summary>
    /// 语言切换事件。触发后界面应刷新所有绑定文字。
    /// </summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// 根据 key 获取当前语言的文本。
    /// </summary>
    string Text(string key);

    /// <summary>
    /// 切换当前语言。
    /// </summary>
    void ToggleLanguage();
}
