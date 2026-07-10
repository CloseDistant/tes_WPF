using System.Windows.Controls;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 设备管理页面视图。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 DeviceViewModel。
/// </summary>
public partial class DeviceView : UserControl
{
    public DeviceView()
    {
        InitializeComponent();
    }
}
