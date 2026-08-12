namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>显示已校验 tDCS 参数快照的单通道启动确认弹窗。</summary>
public partial class DirectCurrentChannelStartConfirmationDialog : Window
{
    public DirectCurrentChannelStartConfirmationDialog(
        DirectCurrentChannelStartConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();

        var channelName = request.ChannelName.Replace(" ", string.Empty, StringComparison.Ordinal);
        ChannelSubtitleText.Text = $"{channelName} · 经颅直流电刺激";
        ChannelText.Text = channelName;
        CurrentText.Text = $"{FormatNumber(request.CurrentMilliampere)} mA";
        PolarityText.Text = request.IsReversePolarity ? "调转" : "不掉转";
        ModeText.Text = request.IsContinuousMode ? "连续" : "间隔";
        RampText.Text = $"{FormatNumber(request.RampUpSeconds)} s / {FormatNumber(request.RampDownSeconds)} s";
        TotalDurationText.Text = $"{FormatNumber(request.TotalDurationSeconds)} s";
        SingleDurationText.Text = request.IsContinuousMode
            ? "—"
            : $"{FormatNumber(request.SingleDurationSeconds)} s";
        IntervalText.Text = request.IsContinuousMode
            ? "—"
            : $"{FormatNumber(request.IntervalSeconds)} s";

        var warning = request.ImpedanceStatus == StimulationImpedanceStatus.Warning;
        ImpedanceStatusText.Text = warning ? "阻抗偏高，请确认后继续" : "阻抗正常";
        ImpedanceValueText.Text = FormatImpedance(request.ImpedanceOhms);
        ImpedanceStatusBorder.Background = new SolidColorBrush(
            warning ? Color.FromRgb(0x36, 0x30, 0x1D) : Color.FromRgb(0x1D, 0x32, 0x27));
        ImpedanceStatusBorder.BorderBrush = new SolidColorBrush(
            warning ? Color.FromRgb(0x8D, 0x6C, 0x24) : Color.FromRgb(0x2F, 0x78, 0x4A));
        ImpedanceStatusText.Foreground = new SolidColorBrush(
            warning ? Color.FromRgb(0xFF, 0xD8, 0x4D) : Color.FromRgb(0x5D, 0xDA, 0x77));
        ImpedanceValueText.Foreground = ImpedanceStatusText.Foreground;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatImpedance(decimal impedanceOhms) =>
        impedanceOhms > 10_000m
            ? $"{(impedanceOhms / 1000m).ToString("0.00", CultureInfo.InvariantCulture)} kΩ"
            : $"{impedanceOhms.ToString("0.##", CultureInfo.InvariantCulture)} Ω";

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
