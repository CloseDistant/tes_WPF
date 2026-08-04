namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>按输出、时间和阻抗状态分组展示单通道 tDCS 启动确认。</summary>
public partial class DirectCurrentStartConfirmationDialog : Window
{
    public DirectCurrentStartConfirmationDialog(DirectCurrentStartConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();

        var channelName = request.ChannelName.Replace(" ", string.Empty, StringComparison.Ordinal);
        SubtitleText.Text = $"{channelName} · 经颅直流电刺激";
        ChannelText.Text = channelName;
        CurrentText.Text = $"{request.CurrentMilliampere.ToString("0.00", CultureInfo.InvariantCulture)} mA";
        PolarityText.Text = request.IsReversePolarity ? "调转" : "不掉转";
        ModeText.Text = request.IsContinuousMode ? "连续" : "间隔";
        RampUpText.Text = FormatSeconds(request.RampUpSeconds);
        RampDownText.Text = FormatSeconds(request.RampDownSeconds);
        TotalDurationText.Text = FormatSeconds(request.TotalDurationSeconds);
        CycleText.Text = request.IsContinuousMode
            ? "— / —"
            : $"{FormatSeconds(request.SingleDurationSeconds!.Value)} / {FormatSeconds(request.IntervalSeconds!.Value)}";

        var impedanceText = FormatImpedance(request.ImpedanceOhms);
        if (request.IsImpedanceWarning)
        {
            ImpedanceStatusBorder.Background = BrushFromHex("#332A18");
            ImpedanceStatusBorder.BorderBrush = BrushFromHex("#715522");
            ImpedanceStatusText.Foreground = BrushFromHex("#FFD84D");
            ImpedanceStatusText.Text = $"阻抗 {impedanceText} · 偏高";
            NoticeBorder.Background = BrushFromHex("#302719");
            NoticeBorder.BorderBrush = BrushFromHex("#E9A42B");
            NoticeText.Foreground = BrushFromHex("#F2DFBD");
            NoticeText.Text = $"当前{channelName}阻抗偏高。确认后仍将先下发该通道配置，再执行启动；其他通道不受影响。";
        }
        else
        {
            ImpedanceStatusBorder.Background = BrushFromHex("#132019");
            ImpedanceStatusBorder.BorderBrush = BrushFromHex("#2D6745");
            ImpedanceStatusText.Foreground = BrushFromHex("#68E18F");
            ImpedanceStatusText.Text = $"阻抗 {impedanceText} · 正常";
            NoticeText.Text = $"确认后将先下发{channelName}配置，再执行该通道启动；其他通道不受影响。";
        }
    }

    private static string FormatSeconds(double seconds) =>
        $"{seconds.ToString("0.###", CultureInfo.InvariantCulture)} s";

    private static string FormatImpedance(decimal impedanceOhms) =>
        impedanceOhms >= 1000m
            ? $"{(impedanceOhms / 1000m).ToString("0.00", CultureInfo.InvariantCulture)} kΩ"
            : $"{impedanceOhms.ToString("0.##", CultureInfo.InvariantCulture)} Ω";

    private static Brush BrushFromHex(string color) =>
        (Brush)new BrushConverter().ConvertFromString(color)!;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
