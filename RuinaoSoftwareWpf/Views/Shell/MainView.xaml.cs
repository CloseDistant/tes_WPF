using System.Windows.Controls;
using System.Windows;
using RuinaoSoftwareWpf.Views.Dialogs;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// 主界面视图（对应 MainView.xaml）。
///
/// 这是软件的主体布局：顶部菜单、左侧导航、中间内容区、底部状态栏。
/// 真正的数据和行为来自 MainViewModel，这里只负责把 XAML 加载出来。
/// </summary>
public partial class MainView : UserControl
{
    private bool isAssessmentPresentationMode;

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

    /// <summary>
    /// 正式数字表型任务使用的纯范式呈现模式。这里只改变 Shell 的可见结构，
    /// 不修改导航 ViewModel 状态，因此退出任务后可以无损恢复进入前的工作台。
    /// </summary>
    public void EnterAssessmentPresentationMode()
    {
        if (isAssessmentPresentationMode)
        {
            return;
        }

        isAssessmentPresentationMode = true;
        CloseTransientPopups();
        ToolbarBorder.Visibility = Visibility.Collapsed;
        ToolbarRow.Height = new GridLength(0);
        StatusBorder.Visibility = Visibility.Collapsed;
        StatusRow.Height = new GridLength(0);
        SidebarToggleButton.Visibility = Visibility.Collapsed;
        Grid.SetColumn(PageContent, 0);
        Grid.SetColumnSpan(PageContent, 2);
    }

    /// <summary>
    /// 离开正式任务时恢复普通软件框架。方法可重复调用，供异常、取消和卸载路径兜底。
    /// </summary>
    public void ExitAssessmentPresentationMode()
    {
        if (!isAssessmentPresentationMode)
        {
            return;
        }

        isAssessmentPresentationMode = false;
        ToolbarRow.Height = new GridLength(40);
        ToolbarBorder.Visibility = Visibility.Visible;
        StatusRow.Height = new GridLength(22);
        StatusBorder.Visibility = Visibility.Visible;
        SidebarToggleButton.Visibility = Visibility.Visible;
        Grid.SetColumn(PageContent, 1);
        Grid.SetColumnSpan(PageContent, 1);
    }

    /// <summary>
    /// 设备菜单命令开始执行后立即收起下拉菜单，避免异步Toast出现时被Popup遮挡。
    /// </summary>
    private void DeviceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DeviceDropDownToggle.IsChecked = false;
    }

    private void ToolsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToolsDropDownToggle.IsChecked = false;
    }

    private void SecurityGuideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToolsDropDownToggle.IsChecked = false;
        var dialog = new SecurityGuideDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

}
