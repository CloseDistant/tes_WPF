using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// WPF MVVM 模式里的最小通知基类。
///
/// 它的作用：当数据（属性）发生变化时，告诉界面“该刷新了”。
/// 比如 CurrentPage 从 Control 变成 FemSimulation，界面会自动切换显示的内容。
///
/// 所有需要被 XAML 绑定的 ViewModel 和 Model 都继承它。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    // PropertyChanged 是 WPF 数据绑定的核心事件。
    // 触发后，WPF 会自动重新读取对应属性的值并更新界面。
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 设置属性值，并在值真正发生变化时通知界面刷新。
    ///
    /// 参数说明：
    /// - storage：属性的“背后存储字段”，例如 private string name。
    /// - value：要设置的新值。
    /// - propertyName：属性名，编译器自动填充，通常不用传。
    ///
    /// 返回值：true 表示值确实变了并触发了通知；false 表示新值和旧值相同，没有通知。
    /// </summary>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        // 如果新值和旧值相等，就不通知，避免无意义的 UI 刷新。
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 手动触发属性变化通知。
    /// 传空字符串 "" 表示“所有属性都变了”，常用于语言切换后整页刷新。
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
