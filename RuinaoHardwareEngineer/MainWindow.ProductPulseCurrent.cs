using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private PulseCurrentStimulationClient pulseCurrentStimulationClient = null!;
    private PulseCurrentStimulationPlan? productTpcsPreview;
    private PulseCurrentStimulationPlan? runningProductTpcsPlan;
    private DispatcherTimer? productTpcsTimer;
    private DateTimeOffset productTpcsStartedAt;
    private bool productTpcsConfigurationSent;
    private bool productTpcsRunning;
    private bool productTpcsDetailView = true;

    public IReadOnlyList<ProductOption<PulseCurrentPolarity>> PulseCurrentPolarityOptions { get; } =
    [
        new(PulseCurrentPolarity.Normal, "不掉转"),
        new(PulseCurrentPolarity.Reversed, "调转"),
    ];

    private void InitializeProductPulseCurrent()
    {
        pulseCurrentStimulationClient = new PulseCurrentStimulationClient(client);
        ProductTpcsBoardAddressComboBox.SelectedIndex = 0;
        ProductTpcsChannelComboBox.SelectedIndex = 0;
        ProductTpcsPolarityComboBox.SelectedIndex = 0;
        TryRefreshProductTpcsPreview();
    }

    private void ProductTpcsInput_Changed(object sender, EventArgs e)
    {
        MarkProductTpcsDirty();
        TryRefreshProductTpcsPreview();
    }

    private void ProductTpcsTestLoad_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    private void ProductTpcsPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateProductTpcsPreview();
            ProductTpcsStatusText.Text = "tPCS产品参数已由RuinaoTesHardware.dll转换；尚未向硬件下发配置。";
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
        }
        catch (Exception exception)
        {
            ProductTpcsStatusText.Text = $"参数转换失败：{exception.Message}";
            ProductTpcsStatusText.Foreground = Brushes.DarkRed;
        }
    }

    private async void ProductTpcsConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTpcsActionAsync(async () =>
        {
            var parameters = ReadProductTpcsParameters();
            var plan = PulseCurrentStimulationClient.CreatePlan(parameters);
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、通道{parameters.Channel}下发产品tPCS配置。\n\n"
                    + $"电流：{plan.SignedCurrentMilliampere:+0.00;-0.00;0.00}mA\n"
                    + $"首次渐升：{parameters.RampWidthMilliseconds:0}ms\n"
                    + $"脉冲/间隔：{parameters.PulseWidthMilliseconds:0}ms / {parameters.IntervalWidthMilliseconds:0}ms\n"
                    + $"完整脉冲：{plan.PlannedPulseCount}次\n"
                    + $"硬件总运行时间：{plan.TotalTimeMilliseconds}ms（R+T）\n"
                    + $"Type 6：Low={plan.InitialRampSegment.LowDa}，High={plan.InitialRampSegment.HighDa}\n"
                    + $"Type 8：Low={plan.PulseTrainSegment.LowDa}，High={plan.PulseTrainSegment.HighDa}\n\n"
                    + "仅允许连接测试负载，不得连接人体。是否继续？",
                "确认下发产品tPCS配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ProductTpcsStatusText.Text =
                "正在通过RuinaoTesHardware.dll依次下发Type 6、Type 8和总控制配置…";
            ProductTpcsStatusText.Foreground = Brushes.DarkOrange;
            var result = await pulseCurrentStimulationClient.ConfigureAsync(parameters, ReadOptions());
            productTpcsPreview = result.Plan;
            productTpcsConfigurationSent = true;
            RenderProductTpcsPreview(result.Plan);
            ProductTpcsStatusText.Text =
                $"配置已被硬件接受：Type6 seq={result.InitialRampCommand.RequestSequence}，"
                + $"Type8 seq={result.PulseTrainCommand.RequestSequence}，"
                + $"总控制seq={result.ControlCommand.RequestSequence}；尚未执行状态回读验证。";
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TPCS_CONFIG", ProductTpcsStatusText.Text));
        }));
    }

    private async void ProductTpcsStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTpcsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductTpcsPlan();
            EnsureProductTpcsTestLoadConfirmed();
            var confirmation = MessageBox.Show(
                $"将向业务板0x{plan.Parameters.BoardAddress:X2}发送业务板级开始命令：\n"
                    + "0x0002=0x00000000。\n"
                    + "工程师软件不会在总时间结束后追加停止或拉低命令。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认tPCS业务板级开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await pulseCurrentStimulationClient.StartAsync(
                plan.Parameters.BoardAddress,
                ReadOptions());
            StartProductTpcsProgress(plan);
            ProductTpcsStatusText.Text = result.Message;
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TPCS_START", result.Message));
        }));
    }

    private async void ProductTpcsStartChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTpcsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductTpcsPlan();
            EnsureProductTpcsTestLoadConfirmed();
            var parameters = plan.Parameters;
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、CH{parameters.Channel}发送指定通道开始命令：\n"
                    + $"0x0002=0x{plan.EnableMask:X8}。\n"
                    + "本按钮只发送这一条硬件命令，不自动停止或拉低。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认tPCS指定通道开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await pulseCurrentStimulationClient.StartChannelAsync(
                parameters.BoardAddress,
                parameters.Channel,
                ReadOptions());
            StartProductTpcsProgress(plan);
            ProductTpcsStatusText.Text = result.Message;
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TPCS_CHANNEL_START", result.Message));
        }));
    }

    private async void ProductTpcsStopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTpcsActionAsync(async () =>
        {
            var boardAddress = runningProductTpcsPlan?.Parameters.BoardAddress
                ?? productTpcsPreview?.Parameters.BoardAddress
                ?? (ProductTpcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var result = await pulseCurrentStimulationClient.StopAsync(boardAddress, ReadOptions());
            StopProductTpcsProgress(clearRemaining: true);
            ProductTpcsStatusText.Text = result.Message;
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TPCS_STOP", result.Message));
        }));
    }

    private async void ProductTpcsStopChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductTpcsActionAsync(async () =>
        {
            var parameters = runningProductTpcsPlan?.Parameters ?? productTpcsPreview?.Parameters;
            var boardAddress = parameters?.BoardAddress
                ?? (ProductTpcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var channel = parameters?.Channel
                ?? (ProductTpcsChannelComboBox.SelectedItem is int selectedChannel
                    ? selectedChannel
                    : throw new FormatException("请选择刺激通道。"));
            var result = await pulseCurrentStimulationClient.StopChannelAsync(
                boardAddress,
                channel,
                ReadOptions());
            StopProductTpcsProgress(clearRemaining: true);
            ProductTpcsStatusText.Text = result.Message;
            ProductTpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TPCS_CHANNEL_STOP", result.Message));
        }));
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

    private async Task RunProductTpcsActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ProductTpcsStatusText.Text = $"操作失败：{exception.Message}";
            ProductTpcsStatusText.Foreground = Brushes.DarkRed;
            throw;
        }
    }

    private PulseCurrentStimulationParameters ReadProductTpcsParameters()
    {
        var boardAddress = ProductTpcsBoardAddressComboBox.SelectedItem is BoardAddressOption addressOption
            ? addressOption.Value
            : throw new FormatException("请选择业务板地址。");
        var channel = ProductTpcsChannelComboBox.SelectedItem is int selectedChannel
            ? selectedChannel
            : throw new FormatException("请选择刺激通道。");
        var polarity = ProductTpcsPolarityComboBox.SelectedValue is PulseCurrentPolarity selectedPolarity
            ? selectedPolarity
            : throw new FormatException("请选择极性。");
        return new PulseCurrentStimulationParameters(
            boardAddress,
            channel,
            ParseProductTpcsDecimal(ProductTpcsCurrentTextBox.Text, "幅值"),
            ParseProductTpcsDecimal(ProductTpcsRampWidthTextBox.Text, "上升宽度"),
            ParseProductTpcsDecimal(ProductTpcsPulseWidthTextBox.Text, "脉冲宽度"),
            ParseProductTpcsDecimal(ProductTpcsIntervalWidthTextBox.Text, "间隔宽度"),
            ParseProductTpcsDecimal(ProductTpcsDurationTextBox.Text, "治疗时间"),
            polarity);
    }

    private PulseCurrentStimulationPlan RequireConfiguredProductTpcsPlan()
    {
        if (!productTpcsConfigurationSent || productTpcsPreview is null)
        {
            throw new InvalidOperationException("当前产品tPCS参数尚未成功下发，禁止开始刺激。");
        }

        return productTpcsPreview;
    }

    private void EnsureProductTpcsTestLoadConfirmed()
    {
        if (ProductTpcsTestLoadCheckBox.IsChecked != true)
        {
            throw new InvalidOperationException("必须先确认当前连接的是测试负载，不能连接人体。");
        }
    }

    private void TryRefreshProductTpcsPreview()
    {
        if (ProductTpcsHardwarePreviewText is null)
        {
            return;
        }

        try
        {
            UpdateProductTpcsPreview();
        }
        catch (Exception exception)
        {
            ProductTpcsTotalCountTextBox.Text = "—";
            ProductTpcsCurrentCountText.Text = "0 / —";
            ProductTpcsSimulatedCurrentText.Text = "0.000 mA";
            ProductTpcsHardwarePreviewText.Text = $"参数尚未形成有效的tPCS硬件配置：{exception.Message}";
            productTpcsPreview = null;
        }
    }

    private void UpdateProductTpcsPreview()
    {
        var plan = PulseCurrentStimulationClient.CreatePlan(ReadProductTpcsParameters());
        productTpcsPreview = plan;
        RenderProductTpcsPreview(plan);
    }

    private void RenderProductTpcsPreview(PulseCurrentStimulationPlan plan)
    {
        ProductTpcsTotalCountTextBox.Text = plan.PlannedPulseCount.ToString(CultureInfo.InvariantCulture);
        ProductTpcsCurrentCountText.Text = $"0 / {plan.PlannedPulseCount}";
        ProductTpcsSimulatedCurrentText.Text = "0.000 mA";
        ProductTpcsRemainingTimeText.Text = FormatRemaining(TimeSpan.FromMilliseconds(plan.TotalTimeMilliseconds));
        ProductTpcsHardwarePreviewText.Text =
            $"DLL转换结果 · mask=0x{plan.EnableMask:X2} · version=0x{plan.ConfigurationVersion:X2} · waveformCount=2\n"
            + $"段1 Type={plan.InitialRampSegment.WaveformType}上升 · Duration={plan.InitialRampSegment.DurationMicroseconds}us "
            + $"· Low={plan.InitialRampSegment.LowDa} · High={plan.InitialRampSegment.HighDa} · Repeat=1\n"
            + $"段2 Type={plan.PulseTrainSegment.WaveformType}梯形 · Duration={plan.PulseTrainSegment.DurationMicroseconds}us "
            + $"· Low={plan.PulseTrainSegment.LowDa} · High={plan.PulseTrainSegment.HighDa} "
            + $"· Rise={plan.PulseTrainSegment.RiseMicroseconds}us · Hold={plan.PulseTrainSegment.HighHoldMicroseconds}us "
            + $"· Fall={plan.PulseTrainSegment.FallMicroseconds}us · Interval={plan.PulseTrainSegment.LowHoldMicroseconds}us · Repeat=1\n"
            + $"治疗窗口={plan.TreatmentDurationMilliseconds}ms · 完整脉冲计划={plan.ScheduledPulseDurationMilliseconds:0}ms "
            + $"· 零输出余量={plan.ZeroOutputTailMilliseconds:0}ms · 硬件总运行={plan.TotalTimeMilliseconds}ms（R+T）";
        RenderProductTpcsWaveformView();
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
                new(0, 75), new(75, 25), new(150, 25), new(150, 75), new(225, 75),
                new(225, 25), new(300, 25), new(300, 75), new(375, 75), new(375, 25),
                new(450, 25), new(450, 75), new(525, 75), new(525, 25), new(600, 25), new(600, 75),
            }
            : new PointCollection
            {
                new(0, 75), new(40, 25), new(55, 25), new(55, 75), new(85, 75),
                new(85, 25), new(100, 25), new(100, 75), new(130, 75), new(130, 25),
                new(145, 25), new(145, 75), new(175, 75), new(175, 25), new(190, 25),
                new(190, 75), new(220, 75), new(220, 25), new(235, 25), new(235, 75),
                new(265, 75), new(265, 25), new(280, 25), new(280, 75), new(600, 75),
            };

        var reversed = ProductTpcsPolarityComboBox.SelectedValue is PulseCurrentPolarity.Reversed;
        ProductTpcsWaveformLine.RenderTransform = reversed
            ? new ScaleTransform(1, -1, 0, 75)
            : Transform.Identity;
        ProductTpcsWaveformCaption.Text = productTpcsDetailView
            ? "细节视图：首次渐升只执行一次，随后为有间隔的同方向矩形脉冲。"
            : "全程示意：大量脉冲只做抽样显示，治疗窗口末尾不足一个完整脉冲的余量保持零输出。";
        ProductTpcsDetailViewButton.Background =
            productTpcsDetailView ? new SolidColorBrush(Color.FromRgb(51, 65, 90)) : Brushes.Transparent;
        ProductTpcsDetailViewButton.Foreground =
            productTpcsDetailView ? Brushes.White : new SolidColorBrush(Color.FromRgb(170, 180, 200));
        ProductTpcsFullViewButton.Background =
            productTpcsDetailView ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(51, 65, 90));
        ProductTpcsFullViewButton.Foreground =
            productTpcsDetailView ? new SolidColorBrush(Color.FromRgb(170, 180, 200)) : Brushes.White;
    }

    private void MarkProductTpcsDirty()
    {
        productTpcsConfigurationSent = false;
        if (ProductTpcsStatusText is not null)
        {
            ProductTpcsStatusText.Text = "参数已修改，尚未下发产品tPCS配置。";
            ProductTpcsStatusText.Foreground = Brushes.DarkOrange;
        }

        UpdateButtons();
    }

    private void InvalidateProductTpcsConfiguration(string message)
    {
        StopProductTpcsProgress(clearRemaining: true);
        productTpcsConfigurationSent = false;
        productTpcsPreview = null;
        if (ProductTpcsStatusText is not null)
        {
            ProductTpcsStatusText.Text = message;
            ProductTpcsStatusText.Foreground = Brushes.DarkOrange;
        }
    }

    private void UpdateProductTpcsButtons(bool canUseHardware)
    {
        if (ProductTpcsConfigureButton is null)
        {
            return;
        }

        var hasOnlineBoard = ProductTpcsBoardAddressComboBox.SelectedItem is BoardAddressOption;
        ProductTpcsPreviewButton.IsEnabled = !isBusy && hasOnlineBoard;
        ProductTpcsConfigureButton.IsEnabled =
            canUseHardware
            && hasOnlineBoard
            && !productTpcsRunning
            && !productDirectCurrentRunning
            && !productTacsRunning
            && !productMtpcsRunning;
        ProductTpcsStartButton.IsEnabled =
            canUseHardware
            && productTpcsConfigurationSent
            && !productTpcsRunning
            && !productDirectCurrentRunning
            && !productTacsRunning
            && !productMtpcsRunning
            && ProductTpcsTestLoadCheckBox.IsChecked == true;
        ProductTpcsStartChannelButton.IsEnabled = ProductTpcsStartButton.IsEnabled;
        var safetyCommandsAvailable =
            !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
        ProductTpcsStopButton.IsEnabled = safetyCommandsAvailable;
        ProductTpcsStopChannelButton.IsEnabled = safetyCommandsAvailable;
    }

    private void StartProductTpcsProgress(PulseCurrentStimulationPlan plan)
    {
        StopProductTpcsProgress(clearRemaining: false);
        runningProductTpcsPlan = plan;
        productTpcsStartedAt = DateTimeOffset.UtcNow;
        productTpcsRunning = true;
        productTpcsTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            ProductTpcsTimer_Tick,
            Dispatcher);
        UpdateProductTpcsProgress();
        productTpcsTimer.Start();
        UpdateButtons();
    }

    private void ProductTpcsTimer_Tick(object? sender, EventArgs e) =>
        UpdateProductTpcsProgress();

    private void UpdateProductTpcsProgress()
    {
        if (runningProductTpcsPlan is null)
        {
            return;
        }

        var progress = PulseCurrentStimulationTimeline.Calculate(
            runningProductTpcsPlan,
            DateTimeOffset.UtcNow - productTpcsStartedAt);
        ProductTpcsCurrentCountText.Text =
            $"{progress.CompletedPulseCount} / {runningProductTpcsPlan.PlannedPulseCount}";
        ProductTpcsSimulatedCurrentText.Text =
            $"{progress.ExpectedCurrentMilliampere:+0.000;-0.000;0.000} mA";
        ProductTpcsRemainingTimeText.Text = FormatRemaining(progress.Remaining);
        if (!progress.IsCompleted)
        {
            return;
        }

        StopProductTpcsProgress(clearRemaining: false);
        ProductTpcsStatusText.Text =
            "软件预计R+T总时间已结束；未追加发送停止或全通道拉低命令。实际输出状态需由示波器确认。";
        ProductTpcsStatusText.Foreground = Brushes.DarkOrange;
        UpdateButtons();
    }

    private void StopProductTpcsProgress(bool clearRemaining)
    {
        if (productTpcsTimer is not null)
        {
            productTpcsTimer.Stop();
            productTpcsTimer.Tick -= ProductTpcsTimer_Tick;
            productTpcsTimer = null;
        }

        productTpcsRunning = false;
        runningProductTpcsPlan = null;
        if (ProductTpcsSimulatedCurrentText is not null)
        {
            ProductTpcsSimulatedCurrentText.Text = "0.000 mA";
        }

        if (clearRemaining && ProductTpcsRemainingTimeText is not null)
        {
            ProductTpcsRemainingTimeText.Text = "00:00:00.0";
        }

        UpdateButtons();
    }

    private static decimal ParseProductTpcsDecimal(string text, string name)
    {
        if (decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value))
        {
            return value;
        }

        throw new FormatException($"{name}必须是有效数值。");
    }
}
