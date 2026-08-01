using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RuinaoHardwareEngineer.Features.ProductPulseCurrent;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private bool productTpcsDetailView = true;

    private void InitializeProductPulseCurrent()
    {
        ProductTpcsBoardAddressComboBox.SelectedIndex = 0;
        ProductTpcsChannelComboBox.SelectedIndex = 0;
        ProductTpcsPolarityComboBox.SelectedIndex = 0;
        TryRefreshProductTpcsPreview();
    }

    private void ProductTpcsInput_Changed(object sender, EventArgs e) =>
        TryRefreshProductTpcsPreview();

    private void ProductTpcsPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = RefreshProductTpcsPreview();
            ProductTpcsStatusText.Text =
                $"产品参数预览有效：计划{preview.TotalPulseCount}次完整脉冲；当前仍不会向硬件下发。";
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
        }
        catch (Exception exception)
        {
            ProductTpcsStatusText.Text = $"产品参数预览失败：{exception.Message}";
            ProductTpcsStatusText.Foreground = Brushes.DarkRed;
        }
    }

    private void ProductTpcsDetailViewButton_Click(object sender, RoutedEventArgs e)
    {
        productTpcsDetailView = true;
        RenderProductTpcsWaveformView();
    }

    private void ProductTpcsFullViewButton_Click(object sender, RoutedEventArgs e)
    {
        productTpcsDetailView = false;
        RenderProductTpcsWaveformView();
    }

    private void TryRefreshProductTpcsPreview()
    {
        if (ProductTpcsHardwarePreviewText is null)
        {
            return;
        }

        try
        {
            _ = RefreshProductTpcsPreview();
        }
        catch (Exception exception)
        {
            ProductTpcsTotalCountTextBox.Text = "—";
            ProductTpcsCurrentCountText.Text = "0 / —";
            ProductTpcsHardwarePreviewText.Text = $"参数尚未形成有效预览：{exception.Message}";
        }
    }

    private ProductPulseCurrentPreview RefreshProductTpcsPreview()
    {
        var reversed =
            ProductTpcsPolarityComboBox.SelectedValue is DirectCurrentPolarity.Reversed;
        var preview = ProductPulseCurrentPreviewCalculator.Calculate(
            ParseProductTpcsDecimal(ProductTpcsCurrentTextBox.Text, "幅值"),
            ParseProductTpcsDecimal(ProductTpcsRampWidthTextBox.Text, "上升宽度"),
            ParseProductTpcsDecimal(ProductTpcsPulseWidthTextBox.Text, "脉冲宽度"),
            ParseProductTpcsDecimal(ProductTpcsIntervalWidthTextBox.Text, "间隔宽度"),
            ParseProductTpcsDecimal(ProductTpcsDurationTextBox.Text, "治疗时间"),
            reversed);

        ProductTpcsTotalCountTextBox.Text =
            preview.TotalPulseCount.ToString(CultureInfo.InvariantCulture);
        ProductTpcsCurrentCountText.Text =
            $"0 / {preview.TotalPulseCount.ToString(CultureInfo.InvariantCulture)}";
        ProductTpcsTargetCurrentText.Text =
            $"{preview.SignedCurrentMilliampere:+0.000;-0.000;0.000} mA";
        ProductTpcsRemainingTimeText.Text = FormatProductTpcsDuration(
            TimeSpan.FromMilliseconds(preview.TreatmentDurationMilliseconds));
        ProductTpcsHardwarePreviewText.Text =
            $"界面候选映射（尚未写入DLL、不会下发）\n"
            + $"段1 Type=8渐升 · Duration={preview.RampDurationMicroseconds}us · "
            + $"Low=0 · High={preview.SignedDa} · Rise={preview.RampDurationMicroseconds}us · Repeat=1\n"
            + $"段2 Type=10脉冲 · Duration={preview.PulseCycleMicroseconds}us · "
            + $"Value={preview.SignedDa} · Pulse={preview.PulseDurationMicroseconds}us · "
            + $"Interval={preview.IntervalDurationMicroseconds}us · Repeat={preview.TotalPulseCount}\n"
            + $"总控候选值 · waveformCount=2 · total={preview.TreatmentDurationMilliseconds}ms";
        RenderProductTpcsWaveformView();
        return preview;
    }

    private void RenderProductTpcsWaveformView()
    {
        if (ProductTpcsWaveformLine is null)
        {
            return;
        }

        ProductTpcsWaveformLine.Points = productTpcsDetailView
            ? new PointCollection
            {
                new(0, 75),
                new(75, 25),
                new(150, 25),
                new(150, 75),
                new(225, 75),
                new(225, 25),
                new(300, 25),
                new(300, 75),
                new(375, 75),
                new(375, 25),
                new(450, 25),
                new(450, 75),
                new(525, 75),
                new(525, 25),
                new(600, 25),
                new(600, 75),
            }
            : new PointCollection
            {
                new(0, 75),
                new(40, 25),
                new(55, 25),
                new(55, 75),
                new(85, 75),
                new(85, 25),
                new(100, 25),
                new(100, 75),
                new(130, 75),
                new(130, 25),
                new(145, 25),
                new(145, 75),
                new(175, 75),
                new(175, 25),
                new(190, 25),
                new(190, 75),
                new(220, 75),
                new(220, 25),
                new(235, 25),
                new(235, 75),
                new(265, 75),
                new(265, 25),
                new(280, 25),
                new(280, 75),
                new(600, 75),
            };

        var reversed =
            ProductTpcsPolarityComboBox.SelectedValue is DirectCurrentPolarity.Reversed;
        ProductTpcsWaveformLine.RenderTransform = reversed
            ? new ScaleTransform(1, -1, 0, 75)
            : Transform.Identity;
        ProductTpcsWaveformCaption.Text = productTpcsDetailView
            ? "细节视图：第一次脉冲包含渐升，后续脉冲直接进入目标幅值。"
            : "全程示意：脉冲按实际时间持续重复；大量脉冲只做抽样显示，不代表下位机实测。";
        ProductTpcsDetailViewButton.Background =
            productTpcsDetailView ? new SolidColorBrush(Color.FromRgb(51, 65, 90)) : Brushes.Transparent;
        ProductTpcsDetailViewButton.Foreground =
            productTpcsDetailView ? Brushes.White : new SolidColorBrush(Color.FromRgb(170, 180, 200));
        ProductTpcsFullViewButton.Background =
            productTpcsDetailView ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(51, 65, 90));
        ProductTpcsFullViewButton.Foreground =
            productTpcsDetailView ? new SolidColorBrush(Color.FromRgb(170, 180, 200)) : Brushes.White;
    }

    private static decimal ParseProductTpcsDecimal(string text, string name)
    {
        if (decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return value;
        }

        throw new FormatException($"{name}必须是有效数值。");
    }

    private static string FormatProductTpcsDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds / 100}";
    }
}
