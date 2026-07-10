using System.Windows.Controls;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 主界面视图（对应 MainView.xaml）。
///
/// 这是软件的主体布局：顶部菜单、左侧导航、中间内容区、底部状态栏。
/// 真正的数据和行为来自 MainViewModel，这里只负责把 XAML 加载出来。
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }
}
