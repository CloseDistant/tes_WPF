using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views;

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

    /// <summary>
    /// 关闭窗口前先收起所有顶层下拉菜单，让 Popup 在当前输入事件结束前释放鼠标捕获。
    /// </summary>
    public void CloseTransientPopups()
    {
        DeviceDropDownToggle.IsChecked = false;
        SimulationDropDownToggle.IsChecked = false;
        ToolsDropDownToggle.IsChecked = false;
        AccountDropDownToggle.IsChecked = false;
        MoreDropDownToggle.IsChecked = false;
        PatientDropDownToggle.IsChecked = false;
    }
}
