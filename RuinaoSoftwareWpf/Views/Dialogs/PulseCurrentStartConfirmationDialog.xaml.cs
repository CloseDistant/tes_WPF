namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>按输出、时间和阻抗状态分组展示单通道tPCS启动确认。</summary>
public partial class PulseCurrentStartConfirmationDialog : Window
{
    public PulseCurrentStartConfirmationDialog(PulseCurrentStartConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsSynchronized || request.Channels.Count != 1)
        {
            throw new ArgumentException("tPCS参数确认窗口仅用于单通道启动。", nameof(request));
        }

        InitializeComponent();

        var channel = request.Channels[0];
        var channelName = channel.ChannelName.Replace(" ", string.Empty, StringComparison.Ordinal);
        SubtitleText.Text = $"{channelName} · 经颅脉冲电流刺激";
        ChannelText.Text = channelName;
        CurrentText.Text = $"{channel.CurrentMilliampere.ToString("0.00", CultureInfo.InvariantCulture)} mA";
        PolarityText.Text = channel.Polarity;
        PulseWidthText.Text = $"{channel.PulseWidthMilliseconds} ms";
        RiseWidthText.Text = $"{channel.RiseWidthMilliseconds} ms";
        IntervalWidthText.Text = $"{channel.IntervalWidthMilliseconds} ms";
        DurationText.Text = $"{channel.TreatmentDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
        PlannedCountText.Text = channel.PlannedTotalCount.ToString(CultureInfo.InvariantCulture);

        var impedanceText = $"{channel.ImpedanceOhms.ToString("0.00", CultureInfo.InvariantCulture)} Ω";
        if (channel.IsImpedanceWarning)
        {
            ImpedanceStatusBorder.Background = BrushFromHex("#332A18");
            ImpedanceStatusBorder.BorderBrush = BrushFromHex("#715522");
            ImpedanceStatusText.Foreground = BrushFromHex("#FFD84D");
            ImpedanceStatusText.Text = $"阻抗 {impedanceText} · 偏高";
            NoticeBorder.Background = BrushFromHex("#302719");
            NoticeBorder.BorderBrush = BrushFromHex("#E9A42B");
            NoticeText.Foreground = BrushFromHex("#F2DFBD");
            NoticeText.Text = $"当前{channelName}阻抗偏高。请确认参数和连接状态，确认后开始该通道刺激。";
        }
        else
        {
            ImpedanceStatusBorder.Background = BrushFromHex("#132019");
            ImpedanceStatusBorder.BorderBrush = BrushFromHex("#2D6745");
            ImpedanceStatusText.Foreground = BrushFromHex("#68E18F");
            ImpedanceStatusText.Text = $"阻抗 {impedanceText} · 正常";
            NoticeText.Text = $"确认后将开始{channelName}经颅脉冲电流刺激；其他通道不受影响。";
        }
    }

    private static Brush BrushFromHex(string color) =>
        (Brush)new BrushConverter().ConvertFromString(color)!;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
