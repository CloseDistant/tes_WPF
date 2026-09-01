using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

/// <summary>
/// tDCS产品级硬件API。负责产品参数校验、单位换算、类型8寄存器布局、发送顺序和回复判断。
/// 调用方只处理整理后的命令结果，不拼帧、不匹配ACK，也不解析硬件状态码。
/// </summary>
public sealed class DirectCurrentStimulationClient
{
    public const decimal MaximumCalibrationCurrentMilliampere = 15.000m;
    public const decimal MinimumCurrentMilliampere = 0.01m;
    public const decimal MaximumTimeSeconds = 3_600.0m;
    public const uint ConfigurationVersion = 0x16;
    public const uint TrapezoidWaveformType = 8;

    private readonly CompositeStimulationHardwareWriter writer;

    public DirectCurrentStimulationClient(BackplaneClient client)
    {
        writer = new CompositeStimulationHardwareWriter(client);
    }

    public async Task<DirectCurrentConfigurationResult> ConfigureAsync(
        DirectCurrentStimulationParameters parameters,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(parameters);
        var waveformWrite = await ExecuteHardwareOperationAsync(
            "下发类型8梯形配置",
            () => writer.WriteWaveformAsync(ToHardwarePlan(plan), 0, options, cancellationToken));
        var controlWrite = await ExecuteHardwareOperationAsync(
            "下发通道总控制配置",
            () => writer.WriteControlAsync(ToHardwarePlan(plan), options, cancellationToken));

        return new DirectCurrentConfigurationResult(
            plan,
            ToProductResult(waveformWrite, "类型8梯形配置已被硬件接受，尚未执行状态回读验证。"),
            ToProductResult(controlWrite, "通道总控制配置已被硬件接受，尚未执行状态回读验证。"));
    }

    public async Task<DirectCurrentCommandResult> StartAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "开始直流电刺激",
            () => writer.StartAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "开始刺激命令已被硬件接受，实际输出需由测量设备确认。");
    }

    public async Task<DirectCurrentCommandResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "停止直流电刺激",
            () => writer.StopAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "停止刺激命令已被硬件接受，硬件停止状态尚未回读验证。");
    }

    /// <summary>
    /// 启动一块业务板上的单个物理刺激通道。
    /// 写入值低8位为通道掩码；当前固件即使尚未处理该掩码，API仍按V1.6最终格式下发。
    /// </summary>
    public Task<DirectCurrentCommandResult> StartChannelAsync(
        byte boardAddress,
        int channel,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        StartChannelsAsync(
            boardAddress,
            CompositeStimulationHardwareWriter.CreateSingleChannelMask(channel),
            options,
            cancellationToken);

    /// <summary>停止一块业务板上的单个物理刺激通道。</summary>
    public Task<DirectCurrentCommandResult> StopChannelAsync(
        byte boardAddress,
        int channel,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        StopChannelsAsync(
            boardAddress,
            CompositeStimulationHardwareWriter.CreateSingleChannelMask(channel),
            options,
            cancellationToken);

    /// <summary>按低8位通道掩码启动一块业务板上的一个或多个刺激通道。</summary>
    public async Task<DirectCurrentCommandResult> StartChannelsAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        CompositeStimulationHardwareWriter.ValidateChannelMask(channelMask);
        var result = await ExecuteHardwareOperationAsync(
            $"开始直流电刺激通道0x{channelMask:X2}",
            () => writer.StartAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"指定通道开始命令已被硬件接受：0x0002=0x{channelMask:X8}；实际输出需由测量设备确认。");
    }

    /// <summary>按低8位通道掩码停止一块业务板上的一个或多个刺激通道。</summary>
    public async Task<DirectCurrentCommandResult> StopChannelsAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        CompositeStimulationHardwareWriter.ValidateChannelMask(channelMask);
        var result = await ExecuteHardwareOperationAsync(
            $"停止直流电刺激通道0x{channelMask:X2}",
            () => writer.StopAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"指定通道停止命令已被硬件接受：0x0003=0x{channelMask:X8}；硬件停止状态尚未回读验证。");
    }

    /// <summary>
    /// 向背板发送全机紧急停止命令。该命令只写背板0x0003=0，
    /// 不遍历业务板，也不附加任何通道拉低操作。
    /// </summary>
    public async Task<DirectCurrentCommandResult> EmergencyStopBackplaneAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "背板紧急停止",
            () => writer.EmergencyStopBackplaneAsync(options, cancellationToken));
        return ToProductResult(
            result,
            "背板紧急停止命令已被硬件接受；硬件停止状态尚未回读验证。");
    }

    public static DirectCurrentStimulationPlan CreatePlan(
        DirectCurrentStimulationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);

        var totalMicroseconds = SecondsToMicroseconds(parameters.TotalDurationSeconds, "刺激时间");
        var totalMilliseconds = SecondsToMilliseconds(parameters.TotalDurationSeconds, "刺激时间");
        var riseMicroseconds = SecondsToMicroseconds(parameters.RampUpSeconds, "渐升时间");
        var fallMicroseconds = SecondsToMicroseconds(parameters.RampDownSeconds, "渐降时间");

        decimal highHoldSeconds;
        decimal lowHoldSeconds;
        if (parameters.DeliveryMode == DirectCurrentDeliveryMode.Continuous)
        {
            highHoldSeconds = parameters.TotalDurationSeconds
                - parameters.RampUpSeconds
                - parameters.RampDownSeconds;
            lowHoldSeconds = 0m;
        }
        else
        {
            highHoldSeconds = parameters.SingleDurationSeconds
                - parameters.RampUpSeconds
                - parameters.RampDownSeconds;
            lowHoldSeconds = parameters.IntervalSeconds;
        }

        var da = ConvertCurrentToDa(parameters.CurrentMilliampere);
        // 极性调转只改变目标值的符号，梯形仍从零电流渐升到目标，
        // 否则将Low设为负值、High设为零会使硬件从负电流逐渐回到零。
        var lowDa = 0;
        var highDa = parameters.Polarity == DirectCurrentPolarity.Normal ? da : -da;
        return new DirectCurrentStimulationPlan(
            parameters,
            EnableMask: 1U << (parameters.Channel - 1),
            ConfigurationVersion,
            TrapezoidWaveformType,
            totalMicroseconds,
            lowDa,
            highDa,
            riseMicroseconds,
            SecondsToMicroseconds(highHoldSeconds, "高平台时间"),
            fallMicroseconds,
            SecondsToMicroseconds(lowHoldSeconds, "间隔时间"),
            totalMilliseconds);
    }

    public static int ConvertCurrentToDa(decimal currentMilliampere)
    {
        if (currentMilliampere is < MinimumCurrentMilliampere
            or > MaximumCalibrationCurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                $"电流幅值必须在{MinimumCurrentMilliampere:0.00}～"
                    + $"{MaximumCalibrationCurrentMilliampere:0.000}mA之间。");
        }

        return decimal.ToInt32(decimal.Round(
            currentMilliampere / MaximumCalibrationCurrentMilliampere * short.MaxValue,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static async Task<BackplaneRegisterOperationResult> ExecuteHardwareOperationAsync(
        string operation,
        Func<Task<BackplaneRegisterOperationResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            throw new DirectCurrentStimulationException(
                DirectCurrentDiagnosticCode.ResponseTimeout,
                operation,
                $"{operation}未在规定时间内收到匹配回复。",
                exception);
        }
        catch (BackplaneConnectionException exception)
        {
            throw new DirectCurrentStimulationException(
                DirectCurrentDiagnosticCode.CommunicationFailure,
                operation,
                $"{operation}失败：{exception.Message}",
                exception);
        }
    }

    private static DirectCurrentCommandResult ToProductResult(
        BackplaneRegisterOperationResult result,
        string message)
    {
        return new DirectCurrentCommandResult(
            result.RequestSequence,
            result.Elapsed,
            DirectCurrentConfirmationLevel.DeviceAccepted,
            result.HardwareStatusCode,
            message);
    }

    private static CompositeStimulationHardwarePlan ToHardwarePlan(
        DirectCurrentStimulationPlan plan) =>
        new(
            plan.Parameters.BoardAddress,
            plan.Parameters.Channel,
            plan.EnableMask,
            plan.ConfigurationVersion,
            plan.TotalTimeMilliseconds,
            [
                new StimulationWaveformHardwareSegment(
                    plan.WaveformType,
                    plan.DurationMicroseconds,
                    FrequencyHz: 0,
                    Amplitude: 0,
                    Offset: 0,
                    PhaseDegree: 0,
                    DutyPermilleOrOrder: 0,
                    plan.LowDa,
                    plan.HighDa,
                    plan.RiseMicroseconds,
                    plan.HighHoldMicroseconds,
                    plan.FallMicroseconds,
                    plan.LowHoldMicroseconds,
                    SampleCount: 0,
                    RepeatCount: 1,
                    Flags: 0),
            ]);

    private static void Validate(DirectCurrentStimulationParameters parameters)
    {
        CompositeStimulationHardwareWriter.ValidateBoardAddress(parameters.BoardAddress);
        CompositeStimulationHardwareWriter.ValidateChannel(parameters.Channel);

        _ = ConvertCurrentToDa(parameters.CurrentMilliampere);
        ValidateTime(parameters.RampUpSeconds, "渐升时间", allowZero: true);
        ValidateTime(parameters.RampDownSeconds, "渐降时间", allowZero: true);
        ValidateTime(parameters.TotalDurationSeconds, "刺激时间", allowZero: false);

        var rampTotal = parameters.RampUpSeconds + parameters.RampDownSeconds;
        if (rampTotal > parameters.TotalDurationSeconds)
        {
            throw new ArgumentException("刺激时间不能小于渐升时间与渐降时间之和。", nameof(parameters));
        }

        if (parameters.DeliveryMode != DirectCurrentDeliveryMode.Intermittent)
        {
            return;
        }

        ValidateTime(parameters.IntervalSeconds, "间隔时间", allowZero: true);
        ValidateTime(parameters.SingleDurationSeconds, "单次时长", allowZero: false);
        if (rampTotal >= parameters.SingleDurationSeconds)
        {
            throw new ArgumentException("单次时长必须大于渐升时间与渐降时间之和。", nameof(parameters));
        }
    }

    private static void ValidateTime(decimal seconds, string name, bool allowZero)
    {
        var minimum = allowZero ? 0m : 0.1m;
        if (seconds < minimum
            || seconds > MaximumTimeSeconds
            || decimal.Round(seconds, 1) != seconds)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"{name}必须在{minimum:0.0}～{MaximumTimeSeconds:0.0}s之间，最小步进0.1s。");
        }
    }

    private static uint SecondsToMicroseconds(decimal seconds, string name) =>
        ConvertToUInt32(seconds * 1_000_000m, name);

    private static uint SecondsToMilliseconds(decimal seconds, string name) =>
        ConvertToUInt32(seconds * 1_000m, name);

    private static uint ConvertToUInt32(decimal value, string name)
    {
        if (value is < uint.MinValue or > uint.MaxValue)
        {
            throw new OverflowException($"{name}换算后的硬件整数超出UInt32范围。");
        }

        return decimal.ToUInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }
}
