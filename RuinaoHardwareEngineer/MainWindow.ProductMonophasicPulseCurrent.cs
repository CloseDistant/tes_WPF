using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private MonophasicPulseCurrentStimulationClient monophasicPulseCurrentStimulationClient = null!;
    private MonophasicPulseCurrentStimulationPlan? productMtpcsPreview;
    private MonophasicPulseCurrentStimulationPlan? runningProductMtpcsPlan;
    private DispatcherTimer? productMtpcsTimer;
    private DateTimeOffset productMtpcsStartedAt;
    private bool productMtpcsConfigurationSent;
    private bool productMtpcsRunning;

    private void InitializeProductMonophasicPulseCurrent()
    {
        monophasicPulseCurrentStimulationClient =
            new MonophasicPulseCurrentStimulationClient(client);
        ProductMtpcsBoardAddressComboBox.SelectedIndex = 0;
        ProductMtpcsChannelComboBox.SelectedIndex = 0;
        TryRefreshProductMtpcsPreview();
    }

    private void ProductMtpcsInput_Changed(object sender, EventArgs e)
    {
        MarkProductMtpcsDirty();
        TryRefreshProductMtpcsPreview();
    }

    private void ProductMtpcsTestLoad_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    private void ProductMtpcsPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateProductMtpcsPreview();
            ProductMtpcsStatusText.Text = "M-tPCS产品参数转换成功；尚未向硬件下发配置。";
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
        }
        catch (Exception exception)
        {
            ProductMtpcsStatusText.Text = $"参数转换失败：{exception.Message}";
            ProductMtpcsStatusText.Foreground = Brushes.DarkRed;
        }
    }

    private async void ProductMtpcsConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductMtpcsActionAsync(async () =>
        {
            var parameters = ReadProductMtpcsParameters();
            var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(parameters);
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、通道{parameters.Channel}下发产品M-tPCS配置。\n\n"
                    + $"电流：{parameters.CurrentMilliampere:0.00}mA（固定正向）\n"
                    + $"渐升/渐降：{parameters.RampUpDownSeconds:0.0}s / {parameters.RampUpDownSeconds:0.0}s\n"
                    + $"间隔：{parameters.IntervalSeconds:0.0}s\n"
                    + $"完整脉冲：{plan.PlannedPulseCount}次\n"
                    + $"类型8：Low={plan.LowDa}，High={plan.HighDa}，高平台=0us\n\n"
                    + "仅允许连接测试负载，不得连接人体。是否继续？",
                "确认下发产品M-tPCS配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ProductMtpcsStatusText.Text =
                "正在通过RuinaoTesHardware.dll下发M-tPCS类型8三角脉冲和总控制配置…";
            ProductMtpcsStatusText.Foreground = Brushes.DarkOrange;
            var result = await monophasicPulseCurrentStimulationClient.ConfigureAsync(
                parameters,
                ReadOptions());
            productMtpcsPreview = result.Plan;
            productMtpcsConfigurationSent = true;
            RenderProductMtpcsPreview(result.Plan);
            ProductMtpcsStatusText.Text =
                $"配置已被硬件接受：波形seq={result.WaveformCommand.RequestSequence}，"
                + $"总控制seq={result.ControlCommand.RequestSequence}；尚未执行状态回读验证。";
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "MTPCS_CONFIG",
                ProductMtpcsStatusText.Text));
        }));
    }

    private async void ProductMtpcsStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductMtpcsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductMtpcsPlan();
            EnsureProductMtpcsTestLoadConfirmed();
            var confirmation = MessageBox.Show(
                $"将向业务板0x{plan.Parameters.BoardAddress:X2}发送业务板级开始命令：\n"
                    + "0x0002=0x00000000。\n"
                    + "工程师软件不会在总时间结束后追加停止或拉低命令。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认M-tPCS业务板级开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await monophasicPulseCurrentStimulationClient.StartAsync(
                plan.Parameters.BoardAddress,
                ReadOptions());
            StartProductMtpcsProgress(plan);
            ProductMtpcsStatusText.Text = result.Message;
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "MTPCS_START", result.Message));
        }));
    }

    private async void ProductMtpcsStartChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductMtpcsActionAsync(async () =>
        {
            var plan = RequireConfiguredProductMtpcsPlan();
            EnsureProductMtpcsTestLoadConfirmed();
            var parameters = plan.Parameters;
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、CH{parameters.Channel}发送指定通道开始命令：\n"
                    + $"0x0002=0x{plan.EnableMask:X8}。\n"
                    + "本按钮只发送这一条硬件命令，不自动停止或拉低。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认M-tPCS指定通道开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await monophasicPulseCurrentStimulationClient.StartChannelAsync(
                parameters.BoardAddress,
                parameters.Channel,
                ReadOptions());
            StartProductMtpcsProgress(plan);
            ProductMtpcsStatusText.Text = result.Message;
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "MTPCS_CHANNEL_START",
                result.Message));
        }));
    }

    private async void ProductMtpcsStopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductMtpcsActionAsync(async () =>
        {
            var boardAddress = runningProductMtpcsPlan?.Parameters.BoardAddress
                ?? productMtpcsPreview?.Parameters.BoardAddress
                ?? (ProductMtpcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var result = await monophasicPulseCurrentStimulationClient.StopAsync(
                boardAddress,
                ReadOptions());
            StopProductMtpcsProgress(clearRemaining: true);
            ProductMtpcsStatusText.Text = result.Message;
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "MTPCS_STOP", result.Message));
        }));
    }

    private async void ProductMtpcsStopChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductMtpcsActionAsync(async () =>
        {
            var parameters = runningProductMtpcsPlan?.Parameters
                ?? productMtpcsPreview?.Parameters;
            var boardAddress = parameters?.BoardAddress
                ?? (ProductMtpcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var channel = parameters?.Channel
                ?? (ProductMtpcsChannelComboBox.SelectedItem is int selectedChannel
                    ? selectedChannel
                    : throw new FormatException("请选择刺激通道。"));
            var result = await monophasicPulseCurrentStimulationClient.StopChannelAsync(
                boardAddress,
                channel,
                ReadOptions());
            StopProductMtpcsProgress(clearRemaining: true);
            ProductMtpcsStatusText.Text = result.Message;
            ProductMtpcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "MTPCS_CHANNEL_STOP",
                result.Message));
        }));
    }

    private async Task RunProductMtpcsActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ProductMtpcsStatusText.Text = $"操作失败：{exception.Message}";
            ProductMtpcsStatusText.Foreground = Brushes.DarkRed;
            throw;
        }
    }

    private MonophasicPulseCurrentStimulationParameters ReadProductMtpcsParameters()
    {
        var boardAddress = ProductMtpcsBoardAddressComboBox.SelectedItem is BoardAddressOption addressOption
            ? addressOption.Value
            : throw new FormatException("请选择业务板地址。");
        var channel = ProductMtpcsChannelComboBox.SelectedItem is int selectedChannel
            ? selectedChannel
            : throw new FormatException("请选择刺激通道。");
        return new MonophasicPulseCurrentStimulationParameters(
            boardAddress,
            channel,
            ParseProductDecimal(ProductMtpcsCurrentTextBox.Text, "电流幅值"),
            ParseProductDecimal(ProductMtpcsRampTextBox.Text, "渐升/渐降时间"),
            ParseProductDecimal(ProductMtpcsIntervalTextBox.Text, "间隔时间"),
            ParseProductDecimal(ProductMtpcsDurationTextBox.Text, "刺激时间"));
    }

    private MonophasicPulseCurrentStimulationPlan RequireConfiguredProductMtpcsPlan()
    {
        if (!productMtpcsConfigurationSent || productMtpcsPreview is null)
        {
            throw new InvalidOperationException("当前产品M-tPCS参数尚未成功下发，禁止开始刺激。");
        }

        return productMtpcsPreview;
    }

    private void EnsureProductMtpcsTestLoadConfirmed()
    {
        if (ProductMtpcsTestLoadCheckBox.IsChecked != true)
        {
            throw new InvalidOperationException("必须先确认当前连接的是测试负载，不能连接人体。");
        }
    }

    private void TryRefreshProductMtpcsPreview()
    {
        if (ProductMtpcsPreviewText is null)
        {
            return;
        }

        try
        {
            UpdateProductMtpcsPreview();
        }
        catch
        {
            ProductMtpcsPreviewText.Text = "参数尚未形成有效的M-tPCS硬件配置。";
            ProductMtpcsPlannedCountText.Text = "—";
            productMtpcsPreview = null;
        }
    }

    private void UpdateProductMtpcsPreview()
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            ReadProductMtpcsParameters());
        productMtpcsPreview = plan;
        RenderProductMtpcsPreview(plan);
    }

    private void RenderProductMtpcsPreview(MonophasicPulseCurrentStimulationPlan plan)
    {
        ProductMtpcsPlannedCountText.Text = plan.PlannedPulseCount.ToString();
        ProductMtpcsPreviewText.Text =
            $"DLL转换结果 · 类型={plan.WaveformType}三角脉冲 · mask=0x{plan.EnableMask:X2} "
            + $"· version=0x{plan.ConfigurationVersion:X2}\n"
            + $"单次={plan.SinglePulseDurationSeconds:0.0}s · 完整次数={plan.PlannedPulseCount} "
            + $"· 波形计划={plan.ScheduledWaveformDurationSeconds:0.0}s "
            + $"· 零输出尾段={plan.ZeroOutputTailSeconds:0.0}s\n"
            + $"Duration={plan.DurationMicroseconds}us · Total={plan.TotalTimeMilliseconds}ms "
            + $"· Low={plan.LowDa} · High={plan.HighDa}\n"
            + $"渐升={plan.RiseMicroseconds}us · 高平台={plan.HighHoldMicroseconds}us "
            + $"· 渐降={plan.FallMicroseconds}us · 间隔={plan.LowHoldMicroseconds}us";
    }

    private void MarkProductMtpcsDirty()
    {
        productMtpcsConfigurationSent = false;
        if (ProductMtpcsStatusText is not null)
        {
            ProductMtpcsStatusText.Text = "参数已修改，尚未下发产品M-tPCS配置。";
            ProductMtpcsStatusText.Foreground = Brushes.DarkOrange;
        }

        UpdateButtons();
    }

    private void InvalidateProductMtpcsConfiguration(string message)
    {
        StopProductMtpcsProgress(clearRemaining: true);
        productMtpcsConfigurationSent = false;
        productMtpcsPreview = null;
        if (ProductMtpcsStatusText is not null)
        {
            ProductMtpcsStatusText.Text = message;
            ProductMtpcsStatusText.Foreground = Brushes.DarkOrange;
        }
    }

    private void UpdateProductMtpcsButtons(bool canUseHardware)
    {
        if (ProductMtpcsConfigureButton is null)
        {
            return;
        }

        var hasOnlineBoard =
            ProductMtpcsBoardAddressComboBox.SelectedItem is BoardAddressOption;
        ProductMtpcsPreviewButton.IsEnabled = !isBusy && hasOnlineBoard;
        ProductMtpcsConfigureButton.IsEnabled =
            canUseHardware
            && hasOnlineBoard
            && !productMtpcsRunning
            && !productDirectCurrentRunning
            && !productTpcsRunning;
        ProductMtpcsStartButton.IsEnabled =
            canUseHardware
            && productMtpcsConfigurationSent
            && !productMtpcsRunning
            && !productDirectCurrentRunning
            && !productTpcsRunning
            && ProductMtpcsTestLoadCheckBox.IsChecked == true;
        ProductMtpcsStartChannelButton.IsEnabled =
            canUseHardware
            && productMtpcsConfigurationSent
            && !productMtpcsRunning
            && !productDirectCurrentRunning
            && !productTpcsRunning
            && ProductMtpcsTestLoadCheckBox.IsChecked == true;
        var safetyCommandsAvailable =
            !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
        ProductMtpcsStopButton.IsEnabled = safetyCommandsAvailable;
        ProductMtpcsStopChannelButton.IsEnabled = safetyCommandsAvailable;
    }

    private void StartProductMtpcsProgress(MonophasicPulseCurrentStimulationPlan plan)
    {
        StopProductMtpcsProgress(clearRemaining: false);
        runningProductMtpcsPlan = plan;
        productMtpcsStartedAt = DateTimeOffset.UtcNow;
        productMtpcsRunning = true;
        productMtpcsTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            ProductMtpcsTimer_Tick,
            Dispatcher);
        UpdateProductMtpcsProgress();
        productMtpcsTimer.Start();
        UpdateButtons();
    }

    private void ProductMtpcsTimer_Tick(object? sender, EventArgs e) =>
        UpdateProductMtpcsProgress();

    private void UpdateProductMtpcsProgress()
    {
        if (runningProductMtpcsPlan is null)
        {
            return;
        }

        var progress = MonophasicPulseCurrentStimulationTimeline.Calculate(
            runningProductMtpcsPlan,
            DateTimeOffset.UtcNow - productMtpcsStartedAt);
        ProductMtpcsExpectedCurrentText.Text =
            $"{progress.ExpectedCurrentMilliampere:0.000} mA";
        ProductMtpcsRemainingTimeText.Text = FormatRemaining(progress.Remaining);
        ProductMtpcsCompletedCountText.Text =
            $"{progress.CompletedPulseCount} / {runningProductMtpcsPlan.PlannedPulseCount}";
        if (!progress.IsCompleted)
        {
            return;
        }

        StopProductMtpcsProgress(clearRemaining: false);
        ProductMtpcsStatusText.Text =
            "软件预计总时间已结束；未追加发送停止或全通道拉低命令。实际输出状态需由示波器确认。";
        ProductMtpcsStatusText.Foreground = Brushes.DarkOrange;
        UpdateButtons();
    }

    private void StopProductMtpcsProgress(bool clearRemaining)
    {
        if (productMtpcsTimer is not null)
        {
            productMtpcsTimer.Stop();
            productMtpcsTimer.Tick -= ProductMtpcsTimer_Tick;
            productMtpcsTimer = null;
        }

        productMtpcsRunning = false;
        runningProductMtpcsPlan = null;
        if (ProductMtpcsExpectedCurrentText is not null)
        {
            ProductMtpcsExpectedCurrentText.Text = "0.000 mA";
        }

        if (clearRemaining && ProductMtpcsRemainingTimeText is not null)
        {
            ProductMtpcsRemainingTimeText.Text = "00:00:00.0";
            ProductMtpcsCompletedCountText.Text = "0 / —";
        }
    }
}
