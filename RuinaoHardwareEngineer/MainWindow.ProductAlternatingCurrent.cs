using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private readonly Dictionary<AlternatingCurrentParameterKind, string> productTacsPreviousValues =
        Enum.GetValues<AlternatingCurrentParameterKind>()
            .ToDictionary(value => value, AlternatingCurrentParameterRules.GetDefault);
    private AlternatingCurrentStimulationClient alternatingCurrentStimulationClient = null!;
    private AlternatingCurrentStimulationPlan? productTacsPreview;
    private AlternatingCurrentStimulationPlan? runningProductTacsPlan;
    private DispatcherTimer? productTacsTimer;
    private DateTimeOffset productTacsStartedAt;
    private bool productTacsConfigurationSent;
    private bool productTacsRunning;
    private bool productTacsDetailView;

    private void InitializeProductAlternatingCurrent()
    {
        alternatingCurrentStimulationClient = new AlternatingCurrentStimulationClient(client);
        ProductTacsBoardAddressComboBox.SelectedIndex = 0;
        ProductTacsChannelComboBox.SelectedIndex = 0;
        TryRefreshProductTacsPreview();
    }

    private void ProductTacsInput_Changed(object sender, EventArgs e)
    {
        MarkProductTacsDirty();
        TryRefreshProductTacsPreview();
    }

    private void ProductTacsInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox
            || textBox.Tag is not string kindText
            || !Enum.TryParse(kindText, out AlternatingCurrentParameterKind kind))
        {
            return;
        }

        var normalized = AlternatingCurrentParameterRules.Normalize(
            kind,
            textBox.Text,
            productTacsPreviousValues[kind]);
        textBox.Text = normalized.Value;
        productTacsPreviousValues[kind] = normalized.Value;
        TryRefreshProductTacsPreview();
        if (normalized.Message is not null)
        {
            ProductTacsStatusText.Text = normalized.Message;
            ProductTacsStatusText.Foreground = Brushes.DarkOrange;
        }
    }

    private void ProductTacsTestLoad_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    private void ProductTacsPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateProductTacsPreview();
            ProductTacsStatusText.Text = "tACS参数和分段硬件映射生成成功；尚未向硬件下发。";
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
        }
        catch (Exception exception)
        {
            ProductTacsStatusText.Text = $"参数转换失败：{exception.Message}";
            ProductTacsStatusText.Foreground = Brushes.DarkRed;
        }
    }

    private async void ProductTacsConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTacsActionAsync(async () =>
        {
            var parameters = ReadProductTacsParameters();
            var plan = AlternatingCurrentStimulationClient.CreatePlan(parameters);
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、通道{parameters.Channel}下发tACS配置。\n\n"
                    + $"幅值（单边峰值）：{parameters.PeakCurrentMilliampere:0.000}mA\n"
                    + $"频率：{parameters.FrequencyHz}Hz\n"
                    + $"渐升/渐降：{parameters.RampUpSeconds:0.0}s / {parameters.RampDownSeconds:0.0}s\n"
                    + $"总时间：{parameters.TotalDurationSeconds:0.0}s\n"
                    + $"类型2正弦段数：{plan.Segments.Count}\n\n"
                    + "仅允许连接测试负载，不得连接人体。是否继续？",
                "确认下发产品tACS配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ProductTacsStatusText.Text =
                $"正在通过RuinaoTesHardware.dll依次下发{plan.Segments.Count}段类型2正弦和总控制配置…";
            ProductTacsStatusText.Foreground = Brushes.DarkOrange;
            var result = await alternatingCurrentStimulationClient.ConfigureAsync(parameters, ReadOptions());
            productTacsPreview = result.Plan;
            productTacsConfigurationSent = true;
            RenderProductTacsPreview(result.Plan);
            var waveformSequences = string.Join(",", result.WaveformCommands.Select(value => value.RequestSequence));
            ProductTacsStatusText.Text =
                $"配置已被硬件接受：正弦seq=[{waveformSequences}]，"
                + $"总控制seq={result.ControlCommand.RequestSequence}；尚未执行状态回读或示波器验证。";
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TACS_CONFIG", ProductTacsStatusText.Text));
        }));
    }

    private async void ProductTacsStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTacsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductTacsPlan();
            EnsureProductTacsTestLoad();
            var confirmation = MessageBox.Show(
                $"将向业务板0x{plan.Parameters.BoardAddress:X2}发送业务板级开始命令0x0002=0。\n"
                    + "工程师软件不会在总时间结束后自动停止。\n\n是否继续？",
                "确认业务板级开始tACS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await alternatingCurrentStimulationClient.StartAsync(
                plan.Parameters.BoardAddress,
                ReadOptions());
            StartProductTacsProgress(plan);
            ProductTacsStatusText.Text = result.Message;
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TACS_START", result.Message));
        }));
    }

    private async void ProductTacsStartChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTacsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductTacsPlan();
            EnsureProductTacsTestLoad();
            var parameters = plan.Parameters;
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、CH{parameters.Channel}发送指定通道开始命令：\n"
                    + $"0x0002=0x{plan.EnableMask:X8}。\n\n是否继续？",
                "确认指定通道开始tACS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await alternatingCurrentStimulationClient.StartChannelAsync(
                parameters.BoardAddress,
                parameters.Channel,
                ReadOptions());
            StartProductTacsProgress(plan);
            ProductTacsStatusText.Text = result.Message;
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TACS_CHANNEL_START", result.Message));
        }));
    }

    private async void ProductTacsStopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTacsActionAsync(async () =>
        {
            var boardAddress = ReadProductTacsStopTarget().BoardAddress;
            var result = await alternatingCurrentStimulationClient.StopAsync(boardAddress, ReadOptions());
            StopProductTacsProgress(clearRemaining: true);
            ProductTacsStatusText.Text = result.Message;
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TACS_STOP", result.Message));
        }));
    }

    private async void ProductTacsStopChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTacsActionAsync(async () =>
        {
            var target = ReadProductTacsStopTarget();
            var result = await alternatingCurrentStimulationClient.StopChannelAsync(
                target.BoardAddress,
                target.Channel,
                ReadOptions());
            StopProductTacsProgress(clearRemaining: true);
            ProductTacsStatusText.Text = result.Message;
            ProductTacsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TACS_CHANNEL_STOP", result.Message));
        }));
    }

    private void ProductTacsFullViewButton_Click(object sender, RoutedEventArgs e)
    {
        productTacsDetailView = false;
        RenderProductTacsWaveform();
    }

    private void ProductTacsDetailViewButton_Click(object sender, RoutedEventArgs e)
    {
        productTacsDetailView = true;
        RenderProductTacsWaveform();
    }

    private void ProductTacsWaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderProductTacsWaveform();

    private async Task RunProductTacsActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ProductTacsStatusText.Text = $"操作失败：{exception.Message}";
            ProductTacsStatusText.Foreground = Brushes.DarkRed;
            throw;
        }
    }

    private AlternatingCurrentStimulationParameters ReadProductTacsParameters()
    {
        var target = ReadSelectedProductTacsTarget();
        var current = ReadProductTacsValue(
            AlternatingCurrentParameterKind.PeakCurrentMilliampere,
            ProductTacsCurrentTextBox.Text);
        var rampUp = ReadProductTacsValue(
            AlternatingCurrentParameterKind.RampUpSeconds,
            ProductTacsRampUpTextBox.Text);
        var rampDown = ReadProductTacsValue(
            AlternatingCurrentParameterKind.RampDownSeconds,
            ProductTacsRampDownTextBox.Text);
        var frequency = ReadProductTacsValue(
            AlternatingCurrentParameterKind.FrequencyHz,
            ProductTacsFrequencyTextBox.Text);
        var duration = ReadProductTacsValue(
            AlternatingCurrentParameterKind.TotalDurationSeconds,
            ProductTacsDurationTextBox.Text);
        return new AlternatingCurrentStimulationParameters(
            target.BoardAddress,
            target.Channel,
            current,
            rampUp,
            rampDown,
            decimal.ToUInt32(frequency),
            duration);
    }

    private (byte BoardAddress, int Channel) ReadSelectedProductTacsTarget()
    {
        var boardAddress = ProductTacsBoardAddressComboBox.SelectedItem is BoardAddressOption addressOption
            ? addressOption.Value
            : throw new FormatException("请选择业务板地址。");
        var channel = ProductTacsChannelComboBox.SelectedItem is int selectedChannel
            ? selectedChannel
            : throw new FormatException("请选择刺激通道。");
        return (boardAddress, channel);
    }

    private (byte BoardAddress, int Channel) ReadProductTacsStopTarget()
    {
        var configured = runningProductTacsPlan?.Parameters ?? productTacsPreview?.Parameters;
        return configured is null
            ? ReadSelectedProductTacsTarget()
            : (configured.BoardAddress, configured.Channel);
    }

    private static decimal ReadProductTacsValue(AlternatingCurrentParameterKind kind, string text)
    {
        if (!AlternatingCurrentParameterRules.TryValidate(kind, text, out var value, out var error))
        {
            throw new FormatException(error);
        }

        return value;
    }

    private void TryRefreshProductTacsPreview()
    {
        if (ProductTacsHardwarePreviewText is null)
        {
            return;
        }

        try
        {
            UpdateProductTacsPreview();
        }
        catch
        {
            productTacsPreview = null;
            ProductTacsHardwarePreviewText.Text = "参数尚未形成有效的tACS硬件配置。";
            RenderProductTacsWaveform();
        }
    }

    private void UpdateProductTacsPreview()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(ReadProductTacsParameters());
        productTacsPreview = plan;
        RenderProductTacsPreview(plan);
    }

    private void RenderProductTacsPreview(AlternatingCurrentStimulationPlan plan)
    {
        var text = new StringBuilder();
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"连续模式 · mask=0x{plan.EnableMask:X2} · version=0x{plan.ConfigurationVersion:X2} "
                + $"· Total={plan.TotalTimeMilliseconds}ms · segments={plan.Segments.Count}");
        text.AppendLine("序号  阶段  起点us      时长us      系数  峰值mA  频率Hz  幅值DA  相位°");
        foreach (var segment in plan.Segments)
        {
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"{segment.Index,2}    {GetProductTacsStageDisplay(segment.Stage),-4}  "
                    + $"{segment.StartMicroseconds,10}  {segment.DurationMicroseconds,10}  "
                    + $"{segment.EnvelopeCoefficient,4:0.0}  {segment.PeakCurrentMilliampere,7:0.000}  "
                    + $"{segment.FrequencyHz,6}  {segment.AmplitudeDa,6}  {segment.PhaseDegree,4}");
        }

        ProductTacsHardwarePreviewText.Text = text.ToString().TrimEnd();
        RenderProductTacsWaveform();
    }

    private void RenderProductTacsWaveform()
    {
        if (ProductTacsWaveformCanvas is null)
        {
            return;
        }

        var width = ProductTacsWaveformCanvas.ActualWidth;
        var height = ProductTacsWaveformCanvas.ActualHeight;
        if (width < 10 || height < 10)
        {
            return;
        }

        var center = height / 2;
        ProductTacsZeroLine.X1 = 0;
        ProductTacsZeroLine.X2 = width;
        ProductTacsZeroLine.Y1 = center;
        ProductTacsZeroLine.Y2 = center;
        ProductTacsPositiveEnvelopeLine.Points.Clear();
        ProductTacsNegativeEnvelopeLine.Points.Clear();
        ProductTacsCarrierLine.Points.Clear();
        if (productTacsPreview is null)
        {
            return;
        }

        if (productTacsDetailView)
        {
            RenderProductTacsCarrierDetail(productTacsPreview, width, height);
        }
        else
        {
            RenderProductTacsEnvelope(productTacsPreview, width, height);
        }
    }

    private void RenderProductTacsEnvelope(
        AlternatingCurrentStimulationPlan plan,
        double width,
        double height)
    {
        ProductTacsPositiveEnvelopeLine.Visibility = Visibility.Visible;
        ProductTacsNegativeEnvelopeLine.Visibility = Visibility.Visible;
        ProductTacsCarrierLine.Visibility = Visibility.Collapsed;
        var center = height / 2;
        var scale = height * 0.42;
        var totalMicroseconds = plan.Parameters.TotalDurationSeconds * 1_000_000m;
        foreach (var segment in plan.Segments)
        {
            var startX = (double)(segment.StartMicroseconds / totalMicroseconds) * width;
            var endX = (double)((segment.StartMicroseconds + segment.DurationMicroseconds) / totalMicroseconds) * width;
            var offset = (double)segment.EnvelopeCoefficient * scale;
            ProductTacsPositiveEnvelopeLine.Points.Add(new Point(startX, center - offset));
            ProductTacsPositiveEnvelopeLine.Points.Add(new Point(endX, center - offset));
            ProductTacsNegativeEnvelopeLine.Points.Add(new Point(startX, center + offset));
            ProductTacsNegativeEnvelopeLine.Points.Add(new Point(endX, center + offset));
        }

        ProductTacsWaveformCaption.Text =
            $"全程包络：{plan.Segments.Count}段严格等时阶梯；上下边界为±单边峰值设定，不是实测电流。";
    }

    private void RenderProductTacsCarrierDetail(
        AlternatingCurrentStimulationPlan plan,
        double width,
        double height)
    {
        ProductTacsPositiveEnvelopeLine.Visibility = Visibility.Collapsed;
        ProductTacsNegativeEnvelopeLine.Visibility = Visibility.Collapsed;
        ProductTacsCarrierLine.Visibility = Visibility.Visible;
        var segment = plan.Segments.FirstOrDefault(value => value.Stage == AlternatingCurrentWaveformStage.Stable)
            ?? plan.Segments.First();
        const int sampleCount = 600;
        var cycles = 5d;
        var windowSeconds = cycles / plan.Parameters.FrequencyHz;
        var startSeconds = segment.StartMicroseconds / 1_000_000d;
        var center = height / 2;
        var scale = height * 0.42;
        for (var index = 0; index <= sampleCount; index++)
        {
            var localSeconds = windowSeconds * index / sampleCount;
            var globalSeconds = startSeconds + localSeconds;
            var value = Math.Sin(2d * Math.PI * plan.Parameters.FrequencyHz * globalSeconds);
            ProductTacsCarrierLine.Points.Add(new Point(
                width * index / sampleCount,
                center - value * scale * (double)segment.EnvelopeCoefficient));
        }

        ProductTacsWaveformCaption.Text =
            $"载波细节：{plan.Parameters.FrequencyHz}Hz的5个周期，当前段包络系数{segment.EnvelopeCoefficient:0.0}；软件设定预览，不是实测电流。";
    }

    private void MarkProductTacsDirty()
    {
        productTacsConfigurationSent = false;
        if (ProductTacsStatusText is not null)
        {
            ProductTacsStatusText.Text = "参数已修改，尚未下发产品tACS配置。";
            ProductTacsStatusText.Foreground = Brushes.DarkOrange;
        }

        UpdateButtons();
    }

    private void InvalidateProductTacsConfiguration(string message)
    {
        StopProductTacsProgress(clearRemaining: true);
        productTacsConfigurationSent = false;
        productTacsPreview = null;
        if (ProductTacsStatusText is not null)
        {
            ProductTacsStatusText.Text = message;
            ProductTacsStatusText.Foreground = Brushes.DarkOrange;
        }
    }

    private void UpdateProductTacsButtons(bool canUseHardware)
    {
        if (ProductTacsConfigureButton is null)
        {
            return;
        }

        var hasOnlineBoard = ProductTacsBoardAddressComboBox.SelectedItem is BoardAddressOption;
        var noProductRunning = !productDirectCurrentRunning
            && !productMtpcsRunning
            && !productTpcsRunning
            && !productTacsRunning;
        ProductTacsPreviewButton.IsEnabled = !isBusy && hasOnlineBoard;
        ProductTacsConfigureButton.IsEnabled = canUseHardware && hasOnlineBoard && noProductRunning;
        ProductTacsStartButton.IsEnabled = canUseHardware
            && productTacsConfigurationSent
            && noProductRunning
            && ProductTacsTestLoadCheckBox.IsChecked == true;
        ProductTacsStartChannelButton.IsEnabled = ProductTacsStartButton.IsEnabled;
        ProductTacsStopButton.IsEnabled = !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
        ProductTacsStopChannelButton.IsEnabled = ProductTacsStopButton.IsEnabled;
    }

    private void StartProductTacsProgress(AlternatingCurrentStimulationPlan plan)
    {
        StopProductTacsProgress(clearRemaining: false);
        runningProductTacsPlan = plan;
        productTacsStartedAt = DateTimeOffset.UtcNow;
        productTacsRunning = true;
        productTacsTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Background,
            ProductTacsTimer_Tick,
            Dispatcher);
        UpdateProductTacsProgress();
        productTacsTimer.Start();
        UpdateButtons();
    }

    private void ProductTacsTimer_Tick(object? sender, EventArgs e) =>
        UpdateProductTacsProgress();

    private void UpdateProductTacsProgress()
    {
        if (runningProductTacsPlan is null)
        {
            return;
        }

        var progress = AlternatingCurrentStimulationTimeline.Calculate(
            runningProductTacsPlan,
            DateTimeOffset.UtcNow - productTacsStartedAt);
        ProductTacsSimulatedCurrentText.Text =
            $"{progress.SimulatedCurrentMilliampere:+0.000;-0.000;0.000} mA";
        ProductTacsEnvelopeText.Text = $"{progress.EnvelopePeakMilliampere:0.000} mA";
        ProductTacsRemainingTimeText.Text = FormatRemaining(progress.Remaining);
        if (!progress.IsCompleted)
        {
            return;
        }

        StopProductTacsProgress(clearRemaining: false);
        ProductTacsStatusText.Text =
            "软件预计总时间已结束；未追加发送停止命令。实际输出状态需由测量设备确认。";
        ProductTacsStatusText.Foreground = Brushes.DarkOrange;
        UpdateButtons();
    }

    private void StopProductTacsProgress(bool clearRemaining)
    {
        if (productTacsTimer is not null)
        {
            productTacsTimer.Stop();
            productTacsTimer.Tick -= ProductTacsTimer_Tick;
            productTacsTimer = null;
        }

        productTacsRunning = false;
        runningProductTacsPlan = null;
        if (ProductTacsSimulatedCurrentText is not null)
        {
            ProductTacsSimulatedCurrentText.Text = "0.000 mA";
            ProductTacsEnvelopeText.Text = "0.000 mA";
        }

        if (clearRemaining && ProductTacsRemainingTimeText is not null)
        {
            ProductTacsRemainingTimeText.Text = "00:00:00.0";
        }
    }

    private AlternatingCurrentStimulationPlan RequireConfiguredProductTacsPlan() =>
        productTacsConfigurationSent && productTacsPreview is not null
            ? productTacsPreview
            : throw new InvalidOperationException("当前产品tACS参数尚未成功下发，禁止开始刺激。");

    private void EnsureProductTacsTestLoad()
    {
        if (ProductTacsTestLoadCheckBox.IsChecked != true)
        {
            throw new InvalidOperationException("必须先确认当前连接的是测试负载，不能连接人体。");
        }
    }

    private static string GetProductTacsStageDisplay(AlternatingCurrentWaveformStage stage) =>
        stage switch
        {
            AlternatingCurrentWaveformStage.RampUp => "渐升",
            AlternatingCurrentWaveformStage.Stable => "平台",
            AlternatingCurrentWaveformStage.RampDown => "渐降",
            _ => "未知",
        };
}
