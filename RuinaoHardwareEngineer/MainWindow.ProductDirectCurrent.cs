using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private DirectCurrentStimulationClient directCurrentStimulationClient = null!;
    private DirectCurrentStimulationPlan? productDirectCurrentPreview;
    private DirectCurrentStimulationPlan? runningProductDirectCurrentPlan;
    private DispatcherTimer? productDirectCurrentTimer;
    private DateTimeOffset productDirectCurrentStartedAt;
    private bool productDirectCurrentConfigurationSent;
    private bool productDirectCurrentRunning;

    public IReadOnlyList<ProductOption<DirectCurrentDeliveryMode>> DirectCurrentModeOptions { get; } =
    [
        new(DirectCurrentDeliveryMode.Continuous, "连续"),
        new(DirectCurrentDeliveryMode.Intermittent, "间隔"),
    ];

    public IReadOnlyList<ProductOption<DirectCurrentPolarity>> DirectCurrentPolarityOptions { get; } =
    [
        new(DirectCurrentPolarity.Normal, "不掉转"),
        new(DirectCurrentPolarity.Reversed, "调转"),
    ];

    private void InitializeProductDirectCurrent()
    {
        directCurrentStimulationClient = new DirectCurrentStimulationClient(client);
        ProductTdcsBoardAddressComboBox.SelectedIndex = 0;
        ProductTdcsChannelComboBox.SelectedIndex = 0;
        ProductTdcsModeComboBox.SelectedIndex = 0;
        ProductTdcsPolarityComboBox.SelectedIndex = 0;
        UpdateProductDirectCurrentModeFields();
        // WPF可能尚未完成ItemsSource绑定；启动阶段只尝试生成预览，
        // 下拉框暂时无SelectedItem时不得阻止工程师工具打开。
        TryRefreshProductDirectCurrentPreview();
    }

    private void ProductTdcsInput_Changed(object sender, EventArgs e)
    {
        MarkProductDirectCurrentDirty();
        TryRefreshProductDirectCurrentPreview();
    }

    private void ProductTdcsMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateProductDirectCurrentModeFields();
        MarkProductDirectCurrentDirty();
        TryRefreshProductDirectCurrentPreview();
    }

    private void ProductTdcsTestLoad_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    private void ProductTdcsPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateProductDirectCurrentPreview();
            ProductTdcsStatusText.Text = "产品参数转换成功；尚未向硬件下发配置。";
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
        }
        catch (Exception exception)
        {
            ProductTdcsStatusText.Text = $"参数转换失败：{exception.Message}";
            ProductTdcsStatusText.Foreground = Brushes.DarkRed;
        }
    }

    private async void ProductTdcsConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductDirectCurrentActionAsync(async () =>
        {
            var parameters = ReadProductDirectCurrentParameters();
            var plan = DirectCurrentStimulationClient.CreatePlan(parameters);
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、通道{parameters.Channel}下发产品tDCS配置。\n\n"
                    + $"模式：{GetModeDisplay(parameters.DeliveryMode)}\n"
                    + $"电流：{parameters.CurrentMilliampere:0.00}mA\n"
                    + $"极性：{GetPolarityDisplay(parameters.Polarity)}\n"
                    + $"类型8：Low={plan.LowDa}，High={plan.HighDa}\n\n"
                    + "仅允许连接测试负载，不得连接人体。是否继续？",
                "确认下发产品tDCS配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ProductTdcsStatusText.Text = "正在通过RuinaoTesHardware.dll下发类型8梯形和总控制配置…";
            ProductTdcsStatusText.Foreground = Brushes.DarkOrange;
            var result = await directCurrentStimulationClient.ConfigureAsync(
                parameters,
                ReadOptions());
            productDirectCurrentPreview = result.Plan;
            productDirectCurrentConfigurationSent = true;
            RenderProductDirectCurrentPreview(result.Plan);
            ProductTdcsStatusText.Text =
                $"配置已被硬件接受：波形seq={result.WaveformCommand.RequestSequence}，"
                + $"总控制seq={result.ControlCommand.RequestSequence}；尚未执行状态回读验证。";
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "TDCS_CONFIG",
                ProductTdcsStatusText.Text));
        }));
    }

    private async void ProductTdcsStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductDirectCurrentActionAsync(async () =>
        {
            if (!productDirectCurrentConfigurationSent || productDirectCurrentPreview is null)
            {
                throw new InvalidOperationException("当前产品tDCS参数尚未成功下发，禁止开始刺激。");
            }

            var configuredPlan = productDirectCurrentPreview;
            if (ProductTdcsTestLoadCheckBox.IsChecked != true)
            {
                throw new InvalidOperationException("必须先确认当前连接的是测试负载，不能连接人体。");
            }

            var parameters = configuredPlan.Parameters;
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}发送业务板级开始命令：\n"
                    + "0x0002=0x00000000。\n"
                    + "工程师软件不会在总时间结束后追加停止或拉低命令。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认业务板级开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await directCurrentStimulationClient.StartAsync(
                parameters.BoardAddress,
                ReadOptions());
            StartProductDirectCurrentProgress(configuredPlan);
            ProductTdcsStatusText.Text = result.Message;
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TDCS_START", result.Message));
        }));
    }

    private async void ProductTdcsStartChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductDirectCurrentActionAsync(async () =>
        {
            if (!productDirectCurrentConfigurationSent || productDirectCurrentPreview is null)
            {
                throw new InvalidOperationException("当前产品tDCS参数尚未成功下发，禁止开始指定通道刺激。");
            }

            var configuredPlan = productDirectCurrentPreview;
            if (ProductTdcsTestLoadCheckBox.IsChecked != true)
            {
                throw new InvalidOperationException("必须先确认当前连接的是测试负载，不能连接人体。");
            }

            var parameters = configuredPlan.Parameters;
            var confirmation = MessageBox.Show(
                $"将向业务板0x{parameters.BoardAddress:X2}、CH{parameters.Channel}发送指定通道开始命令：\n"
                    + $"0x0002=0x{configuredPlan.EnableMask:X8}。\n"
                    + "本按钮只发送这一条硬件命令，不自动停止或拉低。\n\n"
                    + "请确认输出端只连接测试负载，是否继续？",
                "确认指定通道开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await directCurrentStimulationClient.StartChannelAsync(
                parameters.BoardAddress,
                parameters.Channel,
                ReadOptions());
            StartProductDirectCurrentProgress(configuredPlan);
            ProductTdcsStatusText.Text = result.Message;
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TDCS_CHANNEL_START", result.Message));
        }));
    }

    private async void ProductTdcsStopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductDirectCurrentActionAsync(async () =>
        {
            var boardAddress = runningProductDirectCurrentPlan?.Parameters.BoardAddress
                ?? productDirectCurrentPreview?.Parameters.BoardAddress
                ?? (ProductTdcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var result = await directCurrentStimulationClient.StopAsync(
                boardAddress,
                ReadOptions());
            StopProductDirectCurrentProgress(clearRemaining: true);
            ProductTdcsStatusText.Text = result.Message;
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TDCS_STOP", result.Message));
        }));
    }

    private async void ProductTdcsStopChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunProductDirectCurrentActionAsync(async () =>
        {
            var configuredParameters = runningProductDirectCurrentPlan?.Parameters
                ?? productDirectCurrentPreview?.Parameters;
            var boardAddress = configuredParameters?.BoardAddress
                ?? (ProductTdcsBoardAddressComboBox.SelectedItem is BoardAddressOption option
                    ? option.Value
                    : throw new FormatException("请选择在线业务板槽位。"));
            var channel = configuredParameters?.Channel
                ?? (ProductTdcsChannelComboBox.SelectedItem is int selectedChannel
                    ? selectedChannel
                    : throw new FormatException("请选择刺激通道。"));
            var result = await directCurrentStimulationClient.StopChannelAsync(
                boardAddress,
                channel,
                ReadOptions());
            StopProductDirectCurrentProgress(clearRemaining: true);
            ProductTdcsStatusText.Text = result.Message;
            ProductTdcsStatusText.Foreground = Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TDCS_CHANNEL_STOP", result.Message));
        }));
    }

    private async Task RunProductDirectCurrentActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ProductTdcsStatusText.Text = $"操作失败：{exception.Message}";
            ProductTdcsStatusText.Foreground = Brushes.DarkRed;
            throw;
        }
    }

    private DirectCurrentStimulationParameters ReadProductDirectCurrentParameters()
    {
        var boardAddress = ProductTdcsBoardAddressComboBox.SelectedItem is BoardAddressOption addressOption
            ? addressOption.Value
            : throw new FormatException("请选择业务板地址。");
        var channel = ProductTdcsChannelComboBox.SelectedItem is int selectedChannel
            ? selectedChannel
            : throw new FormatException("请选择刺激通道。");
        var deliveryMode = ProductTdcsModeComboBox.SelectedValue is DirectCurrentDeliveryMode selectedMode
            ? selectedMode
            : throw new FormatException("请选择刺激模式。");
        var polarity = ProductTdcsPolarityComboBox.SelectedValue is DirectCurrentPolarity selectedPolarity
            ? selectedPolarity
            : throw new FormatException("请选择极性。");

        return new DirectCurrentStimulationParameters(
            boardAddress,
            channel,
            ParseProductDecimal(ProductTdcsCurrentTextBox.Text, "电流幅值"),
            ParseProductDecimal(ProductTdcsRampUpTextBox.Text, "渐升时间"),
            ParseProductDecimal(ProductTdcsRampDownTextBox.Text, "渐降时间"),
            ParseProductDecimal(ProductTdcsDurationTextBox.Text, "刺激时间"),
            deliveryMode,
            deliveryMode == DirectCurrentDeliveryMode.Intermittent
                ? ParseProductDecimal(ProductTdcsIntervalTextBox.Text, "间隔时间")
                : 0m,
            deliveryMode == DirectCurrentDeliveryMode.Intermittent
                ? ParseProductDecimal(ProductTdcsSingleDurationTextBox.Text, "单次时长")
                : 0m,
            polarity);
    }

    private static decimal ParseProductDecimal(string text, string name)
    {
        if (decimal.TryParse(
                text.Trim(),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value)
            || decimal.TryParse(
                text.Trim(),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture,
                out value))
        {
            return value;
        }

        throw new FormatException($"{name}必须是有效数值。");
    }

    private void TryRefreshProductDirectCurrentPreview()
    {
        if (ProductTdcsPreviewText is null)
        {
            return;
        }

        try
        {
            UpdateProductDirectCurrentPreview();
        }
        catch
        {
            ProductTdcsPreviewText.Text = "参数尚未形成有效的硬件配置。";
            productDirectCurrentPreview = null;
        }
    }

    private void UpdateProductDirectCurrentPreview()
    {
        var plan = DirectCurrentStimulationClient.CreatePlan(
            ReadProductDirectCurrentParameters());
        productDirectCurrentPreview = plan;
        RenderProductDirectCurrentPreview(plan);
    }

    private void RenderProductDirectCurrentPreview(DirectCurrentStimulationPlan plan)
    {
        ProductTdcsPreviewText.Text =
            $"DLL转换结果 · 类型={plan.WaveformType}梯形 · mask=0x{plan.EnableMask:X2} "
            + $"· version=0x{plan.ConfigurationVersion:X2}\n"
            + $"Duration={plan.DurationMicroseconds}us · Total={plan.TotalTimeMilliseconds}ms "
            + $"· Low={plan.LowDa} · High={plan.HighDa}\n"
            + $"渐升={plan.RiseMicroseconds}us · 高平台={plan.HighHoldMicroseconds}us "
            + $"· 渐降={plan.FallMicroseconds}us · 低平台/间隔={plan.LowHoldMicroseconds}us";
    }

    private void UpdateProductDirectCurrentModeFields()
    {
        if (ProductTdcsIntervalTextBox is null)
        {
            return;
        }

        var intermittent =
            ProductTdcsModeComboBox.SelectedValue is DirectCurrentDeliveryMode.Intermittent;
        ProductTdcsIntervalTextBox.IsEnabled = intermittent;
        ProductTdcsSingleDurationTextBox.IsEnabled = intermittent;
    }

    private void MarkProductDirectCurrentDirty()
    {
        productDirectCurrentConfigurationSent = false;
        if (ProductTdcsStatusText is not null)
        {
            ProductTdcsStatusText.Text = "参数已修改，尚未下发产品tDCS配置。";
            ProductTdcsStatusText.Foreground = Brushes.DarkOrange;
        }

        UpdateButtons();
    }

    private void InvalidateProductDirectCurrentConfiguration(string message)
    {
        StopProductDirectCurrentProgress(clearRemaining: true);
        productDirectCurrentConfigurationSent = false;
        productDirectCurrentPreview = null;
        if (ProductTdcsStatusText is not null)
        {
            ProductTdcsStatusText.Text = message;
            ProductTdcsStatusText.Foreground = Brushes.DarkOrange;
        }
    }

    private void UpdateProductDirectCurrentButtons(bool canUseHardware)
    {
        if (ProductTdcsConfigureButton is null)
        {
            return;
        }

        var hasOnlineBoard =
            ProductTdcsBoardAddressComboBox.SelectedItem is BoardAddressOption;
        ProductTdcsPreviewButton.IsEnabled = !isBusy && hasOnlineBoard;
        ProductTdcsConfigureButton.IsEnabled =
            canUseHardware
            && hasOnlineBoard
            && !productDirectCurrentRunning
            && !productTacsRunning
            && !productMtpcsRunning
            && !productTpcsRunning;
        ProductTdcsStartButton.IsEnabled =
            canUseHardware
            && productDirectCurrentConfigurationSent
            && !productDirectCurrentRunning
            && !productTacsRunning
            && !productMtpcsRunning
            && !productTpcsRunning
            && ProductTdcsTestLoadCheckBox.IsChecked == true;
        ProductTdcsStartChannelButton.IsEnabled =
            canUseHardware
            && productDirectCurrentConfigurationSent
            && !productDirectCurrentRunning
            && !productTacsRunning
            && !productMtpcsRunning
            && !productTpcsRunning
            && ProductTdcsTestLoadCheckBox.IsChecked == true;
        ProductTdcsStopButton.IsEnabled =
            !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
        ProductTdcsStopChannelButton.IsEnabled =
            !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
    }

    private void StartProductDirectCurrentProgress(DirectCurrentStimulationPlan plan)
    {
        StopProductDirectCurrentProgress(clearRemaining: false);
        runningProductDirectCurrentPlan = plan;
        productDirectCurrentStartedAt = DateTimeOffset.UtcNow;
        productDirectCurrentRunning = true;
        productDirectCurrentTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            ProductDirectCurrentTimer_Tick,
            Dispatcher);
        UpdateProductDirectCurrentProgress();
        productDirectCurrentTimer.Start();
        UpdateButtons();
    }

    private void ProductDirectCurrentTimer_Tick(object? sender, EventArgs e) =>
        UpdateProductDirectCurrentProgress();

    private void UpdateProductDirectCurrentProgress()
    {
        if (runningProductDirectCurrentPlan is null)
        {
            return;
        }

        var progress = DirectCurrentStimulationTimeline.Calculate(
            runningProductDirectCurrentPlan,
            DateTimeOffset.UtcNow - productDirectCurrentStartedAt);
        ProductTdcsExpectedCurrentText.Text =
            $"{progress.ExpectedCurrentMilliampere:+0.000;-0.000;0.000} mA";
        ProductTdcsRemainingTimeText.Text = FormatRemaining(progress.Remaining);
        if (!progress.IsCompleted)
        {
            return;
        }

        StopProductDirectCurrentProgress(clearRemaining: false);
        ProductTdcsStatusText.Text =
            "软件预计总时间已结束；未追加发送停止或全通道拉低命令。实际输出状态需由测量设备确认。";
        ProductTdcsStatusText.Foreground = Brushes.DarkOrange;
        UpdateButtons();
    }

    private void StopProductDirectCurrentProgress(bool clearRemaining)
    {
        if (productDirectCurrentTimer is not null)
        {
            productDirectCurrentTimer.Stop();
            productDirectCurrentTimer.Tick -= ProductDirectCurrentTimer_Tick;
            productDirectCurrentTimer = null;
        }

        productDirectCurrentRunning = false;
        runningProductDirectCurrentPlan = null;
        if (ProductTdcsExpectedCurrentText is not null)
        {
            ProductTdcsExpectedCurrentText.Text = "0.000 mA";
        }

        if (clearRemaining && ProductTdcsRemainingTimeText is not null)
        {
            ProductTdcsRemainingTimeText.Text = "00:00:00.0";
        }
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var totalHours = (int)remaining.TotalHours;
        return $"{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}.{remaining.Milliseconds / 100}";
    }

    private static string GetModeDisplay(DirectCurrentDeliveryMode mode) =>
        mode == DirectCurrentDeliveryMode.Continuous ? "连续" : "间隔";

    private static string GetPolarityDisplay(DirectCurrentPolarity polarity) =>
        polarity == DirectCurrentPolarity.Normal ? "不掉转" : "调转";

    public sealed record ProductOption<T>(T Value, string Display);
}
