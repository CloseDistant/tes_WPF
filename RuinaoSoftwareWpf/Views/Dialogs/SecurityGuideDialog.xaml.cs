namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

public partial class SecurityGuideDialog : Window
{
    private static readonly IReadOnlyList<SecurityGuideSection> Sections = CreateSections();

    public SecurityGuideDialog()
    {
        InitializeComponent();
        Loaded += SecurityGuideDialog_Loaded;
        SectionList.ItemsSource = Sections;
        SectionList.SelectedIndex = 0;
    }

    private void SecurityGuideDialog_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SecurityGuideDialog_Loaded;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1260, Math.Max(MinWidth, workArea.Width - 40));
        Height = Math.Min(760, Math.Max(MinHeight, workArea.Height - 40));
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is SecurityGuideSection section)
        {
            SectionContent.DataContext = section;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static IReadOnlyList<SecurityGuideSection> CreateSections()
    {
        return
        [
            Section(
                "账号与密码",
                "用户应使用本人账号登录软件，不得多人共用账号或向他人透露密码。",
                "用户离开工作站前，应主动退出登录或确认当前会话已经锁定。",
                "密码长度为8至20位字符。",
                "密码应至少包含字母、数字及特殊字符中的两类。",
                "修改密码时应验证当前密码，修改成功后使用新密码登录。",
                "连续登录失败达到限制时，账号将在规定时间内被锁定。",
                "发现非本人账号登录或未知账号时，应停止操作并联系管理员。"),
            Section(
                "用户权限",
                "软件根据当前登录账号和角色限制可执行的功能。",
                "不得借用其他角色账号获取额外权限，也不得绕过界面直接修改数据。",
                "用户只能执行本人角色被授权的操作。",
                "Admin负责账号管理、安全设置、数据备份恢复及其他管理操作。",
                "Doctor和Technician应在软件授权范围内执行患者、处方、刺激及记录相关操作。",
                "账号管理和关键权限操作会写入安全审计。"),
            Section(
                "自动锁定",
                "用户登录后，软件会监测无操作时间；达到设定时间后锁定当前会话。",
                "正在执行采集或电刺激时不触发自动锁定，流程结束后恢复无操作检测。",
                "自动锁定时间默认15分钟。",
                "Admin可在5至30分钟范围内配置锁定时间。",
                "锁定后只能使用当前登录账号的密码解锁。",
                "自动锁定不拦截用户主动执行Windows睡眠、关机或重启。"),
            Section(
                "安全审计",
                "软件使用独立安全审计模块记录用户活动和关键安全事件。",
                "审计记录不得通过数据库工具或文件编辑工具修改、删除或伪造。",
                "审计记录包括时间、操作者、角色、事件类型、动作、目标、结果和必要说明。",
                "审计信息不记录密码、身份证号和电话等敏感信息原文。",
                "用户可通过“工具 > 安全审计”查询权限范围内的记录。",
                "Admin可按日期、事件类型和操作者查询，并受控导出CSV。",
                "审计写入异常时，应停止重复操作并联系维护人员检查运行日志。"),
            Section(
                "数据存储与导出",
                "业务数据库和安全审计数据库采用整库加密，由软件自动访问。",
                "CSV导出文件不再由数据库加密保护，应保存至受控位置。",
                "不得使用数据库工具直接打开、修改、复制或替换软件数据库。",
                "不得将患者、处方、治疗记录或审计文件保存至公共共享目录。",
                "导出文件使用完成后，应按使用单位要求归档或删除。",
                "不得通过非授权方式传播导出数据。"),
            Section(
                "备份与恢复",
                "数据备份和恢复仅允许Admin执行，并应在无采集、无电刺激运行时操作。",
                "恢复会替换本机现有业务数据，执行前应确认当前数据已按需要备份。",
                "软件使用.rnbak格式生成备份包，备份人员应设置并保管备份密码。",
                "备份完成后应确认软件提示操作成功。",
                "恢复前应核对备份来源、文件名和密码。",
                "恢复成功后应按提示退出软件，再重新启动。",
                "备份或恢复失败时，应保留原备份包和运行日志并联系维护人员。"),
            Section(
                "发布文件校验",
                "Admin可在“设置 > 发布文件完整性”中手动检查软件发布文件。",
                "校验失败时，不得自行替换文件或继续反复启动软件。",
                "校验通过表示本次检查未发现受校验文件缺失、替换或内容变化。",
                "校验失败时应保留结果和运行日志，并联系维护人员。",
                "用户不得自行替换可执行文件、动态链接库、配置文件或其他发布文件。"),
            Section(
                "设备连接",
                "软件通过本地专用USB接口与经颅电刺激设备通信。",
                "设备通信异常时，应根据提示检查连接，不得使用工具干预或修改通信数据。",
                "软件不提供互联网通信、远程访问或远程控制功能。",
                "USB链路用于设备命令、刺激参数和运行状态，不传输账号密码或患者身份信息。",
                "应使用指定设备和接口，不得使用未经确认的转接设备或虚拟USB工具。",
                "通信超时或响应不匹配时，软件不会将本次操作判定为成功。"),
            Section(
                "软件版本与维护",
                "当前软件版本为1.0.0，不提供软件内、在线、远程或自动升级功能。",
                "任何来源不明的补丁、更新包或替换文件都不得用于本软件。",
                "用户不得自行下载补丁或替换程序文件。",
                "软件不提供远程维护入口。",
                "维护检查应按照使用单位批准的现场维护流程执行。"),
            Section(
                "安全事件处置",
                "发现账号、数据、审计、程序文件或设备通信异常时，应停止相关操作并保留现场信息。",
                "若正在执行电刺激，应优先按正常停止或紧急停止流程保证设备安全。",
                "记录异常发生时间、当前账号、操作步骤和软件提示。",
                "保留运行日志、审计记录、备份文件和错误截图。",
                "不得自行修改数据库或删除异常现场文件。",
                "联系管理员或维护人员进行后续处理。"),
            Section(
                "禁止事项",
                "以下操作可能破坏账号安全、数据保密性、审计追溯性或设备通信安全。",
                "违反禁止事项可能造成数据丢失、无法追溯或软件不能正常运行。",
                "共用账号、泄露密码或绕过登录。",
                "修改、删除或伪造安全审计记录。",
                "直接修改业务数据库或审计数据库。",
                "自行替换软件文件、安装补丁或来源不明的更新包。",
                "将患者数据、治疗记录、审计记录或备份交给未授权人员。",
                "在备份、恢复或发布文件校验过程中强制关闭软件或拔出介质。")
        ];
    }

    private static SecurityGuideSection Section(
        string title,
        string summary,
        string notice,
        params string[] items)
    {
        var numberedItems = items
            .Select((text, index) => new SecurityGuideItem($"{index + 1}.", text))
            .ToArray();
        return new SecurityGuideSection(title, summary, notice, numberedItems);
    }

    private sealed record SecurityGuideSection(
        string Title,
        string Summary,
        string Notice,
        IReadOnlyList<SecurityGuideItem> Items);

    private sealed record SecurityGuideItem(string NumberText, string Text);
}
