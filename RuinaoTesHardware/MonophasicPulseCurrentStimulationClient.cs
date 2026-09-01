namespace RuinaoTesHardware;

/// <summary>
/// M-tPCS产品级硬件API。负责参数校验、完整三角脉冲计划、类型8寄存器映射、
/// 发送顺序和回复判断。工程师软件和正式软件共用本类型。
/// </summary>
public sealed class MonophasicPulseCurrentStimulationClient
{
    public const decimal MinimumCurrentMilliampere = 0.01m;
    public const decimal MaximumCurrentMilliampere = 15.00m;
    public const decimal MinimumRampUpDownSeconds = 0.1m;
    public const decimal MaximumRampUpDownSeconds = 100.0m;
    public const decimal MaximumIntervalSeconds = 3_600.0m;
    public const decimal MinimumTotalDurationSeconds = 0.2m;
    public const decimal MaximumTotalDurationSeconds = 3_600.0m;
    public const uint ConfigurationVersion = 0x16;
    public const uint TrapezoidWaveformType = 8;

    private readonly CompositeStimulationHardwareWriter writer;

    public MonophasicPulseCurrentStimulationClient(BackplaneClient client)
    {
        writer = new CompositeStimulationHardwareWriter(client);
    }

    public async Task<StimulationHardwareConfigurationResult<MonophasicPulseCurrentStimulationPlan>> ConfigureAsync(
        MonophasicPulseCurrentStimulationParameters parameters,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(parameters);
        var hardwarePlan = ToHardwarePlan(plan);
        var waveformWrite = await ExecuteHardwareOperationAsync(
            "下发M-tPCS类型8三角脉冲配置",
            () => writer.WriteWaveformAsync(hardwarePlan, 0, options, cancellationToken));
        var controlWrite = await ExecuteHardwareOperationAsync(
            "下发M-tPCS通道总控制配置",
            () => writer.WriteControlAsync(hardwarePlan, options, cancellationToken));

        return new StimulationHardwareConfigurationResult<MonophasicPulseCurrentStimulationPlan>(
            plan,
            ToProductResult(waveformWrite, "M-tPCS类型8三角脉冲配置已被硬件接受，尚未执行状态回读验证。"),
            ToProductResult(controlWrite, "M-tPCS通道总控制配置已被硬件接受，尚未执行状态回读验证。"));
    }

    public async Task<StimulationHardwareCommandResult> StartAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "开始M-tPCS",
            () => writer.StartAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "M-tPCS业务板开始命令已被硬件接受，实际输出需由测量设备确认。");
    }

    public Task<StimulationHardwareCommandResult> StartChannelAsync(
        byte boardAddress,
        int channel,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        StartChannelsAsync(
            boardAddress,
            CompositeStimulationHardwareWriter.CreateSingleChannelMask(channel),
            options,
            cancellationToken);

    public async Task<StimulationHardwareCommandResult> StartChannelsAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        CompositeStimulationHardwareWriter.ValidateChannelMask(channelMask);
        var result = await ExecuteHardwareOperationAsync(
            $"开始M-tPCS通道0x{channelMask:X2}",
            () => writer.StartAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"M-tPCS指定通道开始命令已被硬件接受：0x0002=0x{channelMask:X8}；实际输出需由测量设备确认。");
    }

    public async Task<StimulationHardwareCommandResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "停止M-tPCS",
            () => writer.StopAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "M-tPCS业务板停止命令已被硬件接受，硬件停止状态尚未回读验证。");
    }

    public Task<StimulationHardwareCommandResult> StopChannelAsync(
        byte boardAddress,
        int channel,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default) =>
        StopChannelsAsync(
            boardAddress,
            CompositeStimulationHardwareWriter.CreateSingleChannelMask(channel),
            options,
            cancellationToken);

    public async Task<StimulationHardwareCommandResult> StopChannelsAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        CompositeStimulationHardwareWriter.ValidateChannelMask(channelMask);
        var result = await ExecuteHardwareOperationAsync(
            $"停止M-tPCS通道0x{channelMask:X2}",
            () => writer.StopAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"M-tPCS指定通道停止命令已被硬件接受：0x0003=0x{channelMask:X8}；硬件停止状态尚未回读验证。");
    }

    public async Task<StimulationHardwareCommandResult> EmergencyStopBackplaneAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "背板紧急停止",
            () => writer.EmergencyStopBackplaneAsync(options, cancellationToken));
        return ToProductResult(result, "背板紧急停止命令已被硬件接受；硬件停止状态尚未回读验证。");
    }

    public static MonophasicPulseCurrentStimulationPlan CreatePlan(
        MonophasicPulseCurrentStimulationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);

        var singlePulseSeconds = parameters.RampUpDownSeconds * 2m;
        var cycleSeconds = singlePulseSeconds + parameters.IntervalSeconds;
        var plannedPulseCount = decimal.ToInt32(decimal.Floor(
            (parameters.TotalDurationSeconds + parameters.IntervalSeconds) / cycleSeconds));
        var scheduledSeconds = plannedPulseCount * singlePulseSeconds
            + Math.Max(0, plannedPulseCount - 1) * parameters.IntervalSeconds;
        var zeroTailSeconds = parameters.TotalDurationSeconds - scheduledSeconds;
        var da = DirectCurrentStimulationClient.ConvertCurrentToDa(parameters.CurrentMilliampere);

        return new MonophasicPulseCurrentStimulationPlan(
            parameters,
            singlePulseSeconds,
            cycleSeconds,
            plannedPulseCount,
            scheduledSeconds,
            zeroTailSeconds,
            EnableMask: 1U << (parameters.Channel - 1),
            ConfigurationVersion,
            TrapezoidWaveformType,
            SecondsToMicroseconds(scheduledSeconds, "完整脉冲计划时间"),
            LowDa: 0,
            HighDa: da,
            SecondsToMicroseconds(parameters.RampUpDownSeconds, "渐升时间"),
            HighHoldMicroseconds: 0,
            SecondsToMicroseconds(parameters.RampUpDownSeconds, "渐降时间"),
            SecondsToMicroseconds(parameters.IntervalSeconds, "间隔时间"),
            SecondsToMilliseconds(parameters.TotalDurationSeconds, "刺激时间"));
    }

    private static void Validate(MonophasicPulseCurrentStimulationParameters parameters)
    {
        CompositeStimulationHardwareWriter.ValidateBoardAddress(parameters.BoardAddress);
        CompositeStimulationHardwareWriter.ValidateChannel(parameters.Channel);
        if (parameters.CurrentMilliampere is < MinimumCurrentMilliampere
                or > MaximumCurrentMilliampere
            || decimal.Round(parameters.CurrentMilliampere, 2) != parameters.CurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                $"电流幅值必须在{MinimumCurrentMilliampere:0.00}～{MaximumCurrentMilliampere:0.00}mA之间，最小步进0.01mA。");
        }

        _ = DirectCurrentStimulationClient.ConvertCurrentToDa(parameters.CurrentMilliampere);
        ValidateOneDecimalTime(
            parameters.RampUpDownSeconds,
            MinimumRampUpDownSeconds,
            MaximumRampUpDownSeconds,
            "渐升/渐降时间");
        ValidateOneDecimalTime(
            parameters.IntervalSeconds,
            0m,
            MaximumIntervalSeconds,
            "间隔时间");
        ValidateOneDecimalTime(
            parameters.TotalDurationSeconds,
            MinimumTotalDurationSeconds,
            MaximumTotalDurationSeconds,
            "刺激时间");

        if (parameters.TotalDurationSeconds < parameters.RampUpDownSeconds * 2m)
        {
            throw new ArgumentException(
                "刺激时间不能小于一个完整三角脉冲的时长（2×渐升/渐降时间）。",
                nameof(parameters));
        }
    }

    private static void ValidateOneDecimalTime(
        decimal seconds,
        decimal minimum,
        decimal maximum,
        string name)
    {
        if (seconds < minimum || seconds > maximum || decimal.Round(seconds, 1) != seconds)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"{name}必须在{minimum:0.0}～{maximum:0.0}s之间，最小步进0.1s。");
        }
    }

    private static CompositeStimulationHardwarePlan ToHardwarePlan(
        MonophasicPulseCurrentStimulationPlan plan) =>
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

    private static StimulationHardwareCommandResult ToProductResult(
        BackplaneRegisterOperationResult result,
        string message) =>
        new(
            result.RequestSequence,
            result.Elapsed,
            StimulationHardwareConfirmationLevel.DeviceAccepted,
            result.HardwareStatusCode,
            message);

    private static async Task<BackplaneRegisterOperationResult> ExecuteHardwareOperationAsync(
        string operation,
        Func<Task<BackplaneRegisterOperationResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TimeoutException exception)
        {
            throw new StimulationHardwareException(
                StimulationHardwareDiagnosticCode.ResponseTimeout,
                operation,
                $"{operation}未在规定时间内收到匹配回复。",
                exception);
        }
        catch (BackplaneConnectionException exception)
        {
            throw new StimulationHardwareException(
                StimulationHardwareDiagnosticCode.CommunicationFailure,
                operation,
                $"{operation}失败：{exception.Message}",
                exception);
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
