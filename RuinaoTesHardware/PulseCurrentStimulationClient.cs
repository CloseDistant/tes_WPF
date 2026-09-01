namespace RuinaoTesHardware;

/// <summary>
/// tPCS产品级硬件API。负责参数校验、一次Type 6渐升、Type 8间隔脉冲计划、
/// 发送顺序和回复判断。工程师软件和正式软件共用本类型。
/// </summary>
public sealed class PulseCurrentStimulationClient
{
    public const decimal MinimumCurrentMilliampere = 0.01m;
    public const decimal MaximumCurrentMilliampere = 15.00m;
    public const decimal MinimumRampWidthMilliseconds = 0m;
    public const decimal MaximumRampWidthMilliseconds = 1_000m;
    public const decimal MinimumPulseWidthMilliseconds = 1m;
    public const decimal MaximumPulseWidthMilliseconds = 2_000m;
    public const decimal MinimumIntervalWidthMilliseconds = 1m;
    public const decimal MaximumIntervalWidthMilliseconds = 10_000m;
    public const decimal MinimumTreatmentDurationSeconds = 0.1m;
    public const decimal MaximumTreatmentDurationSeconds = 3_600.0m;
    public const uint ConfigurationVersion = 0x16;
    public const uint RampWaveformType = 6;
    public const uint TrapezoidWaveformType = 8;
    public const uint InstantaneousEdgeDurationMicroseconds = 1;

    private readonly CompositeStimulationHardwareWriter writer;

    public PulseCurrentStimulationClient(BackplaneClient client)
    {
        writer = new CompositeStimulationHardwareWriter(client);
    }

    public async Task<PulseCurrentStimulationConfigurationResult> ConfigureAsync(
        PulseCurrentStimulationParameters parameters,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(parameters);
        var hardwarePlan = ToHardwarePlan(plan);
        var rampWrite = await ExecuteHardwareOperationAsync(
            "下发tPCS类型6首次渐升配置",
            () => writer.WriteWaveformAsync(hardwarePlan, 0, options, cancellationToken));
        var pulseWrite = await ExecuteHardwareOperationAsync(
            "下发tPCS类型8间隔脉冲配置",
            () => writer.WriteWaveformAsync(hardwarePlan, 1, options, cancellationToken));
        var controlWrite = await ExecuteHardwareOperationAsync(
            "下发tPCS通道总控制配置",
            () => writer.WriteControlAsync(hardwarePlan, options, cancellationToken));

        return new PulseCurrentStimulationConfigurationResult(
            plan,
            ToProductResult(rampWrite, "tPCS类型6首次渐升配置已被硬件接受，尚未执行状态回读验证。"),
            ToProductResult(pulseWrite, "tPCS类型8间隔脉冲配置已被硬件接受，尚未执行状态回读验证。"),
            ToProductResult(controlWrite, "tPCS通道总控制配置已被硬件接受，尚未执行状态回读验证。"));
    }

    public async Task<StimulationHardwareCommandResult> StartAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "开始tPCS",
            () => writer.StartAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "tPCS业务板开始命令已被硬件接受，实际输出需由测量设备确认。");
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
            $"开始tPCS通道0x{channelMask:X2}",
            () => writer.StartAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"tPCS指定通道开始命令已被硬件接受：0x0002=0x{channelMask:X8}；实际输出需由测量设备确认。");
    }

    public async Task<StimulationHardwareCommandResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "停止tPCS",
            () => writer.StopAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "tPCS业务板停止命令已被硬件接受，硬件停止状态尚未回读验证。");
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
            $"停止tPCS通道0x{channelMask:X2}",
            () => writer.StopAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"tPCS指定通道停止命令已被硬件接受：0x0003=0x{channelMask:X8}；硬件停止状态尚未回读验证。");
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

    public static PulseCurrentStimulationPlan CreatePlan(PulseCurrentStimulationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);

        var treatmentMilliseconds = parameters.TreatmentDurationSeconds * 1_000m;
        var cycleMilliseconds = parameters.PulseWidthMilliseconds + parameters.IntervalWidthMilliseconds;
        var plannedPulseCount = decimal.ToInt32(decimal.Floor(
            (treatmentMilliseconds + parameters.IntervalWidthMilliseconds) / cycleMilliseconds));
        var scheduledMilliseconds = plannedPulseCount * parameters.PulseWidthMilliseconds
            + Math.Max(0, plannedPulseCount - 1) * parameters.IntervalWidthMilliseconds;
        var zeroTailMilliseconds = treatmentMilliseconds - scheduledMilliseconds;
        var da = DirectCurrentStimulationClient.ConvertCurrentToDa(parameters.CurrentMilliampere);
        var signedDa = parameters.Polarity == PulseCurrentPolarity.Normal ? da : -da;
        var signedCurrent = parameters.Polarity == PulseCurrentPolarity.Normal
            ? parameters.CurrentMilliampere
            : -parameters.CurrentMilliampere;
        var rampDurationMicroseconds = MillisecondsToMicroseconds(
            parameters.RampWidthMilliseconds,
            "上升宽度");
        var scheduledDurationMicroseconds = MillisecondsToMicroseconds(
            scheduledMilliseconds,
            "完整脉冲计划时间");

        var rampSegment = new PulseCurrentWaveformSegmentPlan(
            RampWaveformType,
            rampDurationMicroseconds,
            LowDa: 0,
            HighDa: signedDa,
            RiseMicroseconds: 0,
            HighHoldMicroseconds: 0,
            FallMicroseconds: 0,
            LowHoldMicroseconds: 0,
            RepeatCount: 1);
        var pulseSegment = new PulseCurrentWaveformSegmentPlan(
            TrapezoidWaveformType,
            scheduledDurationMicroseconds,
            LowDa: 0,
            HighDa: signedDa,
            // 下位机把0作为特殊值处理，不能形成期望的瞬时边沿；1us是已由硬件验证的最小有效值。
            RiseMicroseconds: InstantaneousEdgeDurationMicroseconds,
            HighHoldMicroseconds: MillisecondsToMicroseconds(parameters.PulseWidthMilliseconds, "脉冲宽度"),
            FallMicroseconds: InstantaneousEdgeDurationMicroseconds,
            LowHoldMicroseconds: MillisecondsToMicroseconds(parameters.IntervalWidthMilliseconds, "间隔宽度"),
            RepeatCount: 1);

        return new PulseCurrentStimulationPlan(
            parameters,
            signedCurrent,
            plannedPulseCount,
            scheduledMilliseconds,
            zeroTailMilliseconds,
            EnableMask: 1U << (parameters.Channel - 1),
            ConfigurationVersion,
            rampSegment,
            pulseSegment,
            TreatmentDurationMilliseconds: ConvertToUInt32(treatmentMilliseconds, "治疗时间"),
            TotalTimeMilliseconds: ConvertToUInt32(
                parameters.RampWidthMilliseconds + treatmentMilliseconds,
                "硬件总运行时间"));
    }

    private static void Validate(PulseCurrentStimulationParameters parameters)
    {
        CompositeStimulationHardwareWriter.ValidateBoardAddress(parameters.BoardAddress);
        CompositeStimulationHardwareWriter.ValidateChannel(parameters.Channel);
        if (!Enum.IsDefined(parameters.Polarity))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "tPCS极性无效。");
        }

        if (parameters.CurrentMilliampere is < MinimumCurrentMilliampere or > MaximumCurrentMilliampere
            || decimal.Round(parameters.CurrentMilliampere, 2) != parameters.CurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                $"电流幅值必须在{MinimumCurrentMilliampere:0.00}～{MaximumCurrentMilliampere:0.00}mA之间，最小步进0.01mA。");
        }

        _ = DirectCurrentStimulationClient.ConvertCurrentToDa(parameters.CurrentMilliampere);
        ValidateIntegerMilliseconds(
            parameters.RampWidthMilliseconds,
            MinimumRampWidthMilliseconds,
            MaximumRampWidthMilliseconds,
            "上升宽度");
        ValidateIntegerMilliseconds(
            parameters.PulseWidthMilliseconds,
            MinimumPulseWidthMilliseconds,
            MaximumPulseWidthMilliseconds,
            "脉冲宽度");
        ValidateIntegerMilliseconds(
            parameters.IntervalWidthMilliseconds,
            MinimumIntervalWidthMilliseconds,
            MaximumIntervalWidthMilliseconds,
            "间隔宽度");
        if (parameters.TreatmentDurationSeconds < MinimumTreatmentDurationSeconds
            || parameters.TreatmentDurationSeconds > MaximumTreatmentDurationSeconds
            || decimal.Round(parameters.TreatmentDurationSeconds, 1) != parameters.TreatmentDurationSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                $"治疗时间必须在{MinimumTreatmentDurationSeconds:0.0}～{MaximumTreatmentDurationSeconds:0.0}s之间，最小步进0.1s。");
        }

        if (parameters.TreatmentDurationSeconds * 1_000m < parameters.PulseWidthMilliseconds)
        {
            throw new ArgumentException("治疗时间不足以容纳一次完整脉冲。", nameof(parameters));
        }
    }

    private static void ValidateIntegerMilliseconds(decimal value, decimal minimum, decimal maximum, string name)
    {
        if (value < minimum || value > maximum || decimal.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"{name}必须在{minimum:0}～{maximum:0}ms之间，且必须是整数。");
        }
    }

    private static CompositeStimulationHardwarePlan ToHardwarePlan(PulseCurrentStimulationPlan plan) =>
        new(
            plan.Parameters.BoardAddress,
            plan.Parameters.Channel,
            plan.EnableMask,
            plan.ConfigurationVersion,
            plan.TotalTimeMilliseconds,
            [ToHardwareSegment(plan.InitialRampSegment), ToHardwareSegment(plan.PulseTrainSegment)]);

    private static StimulationWaveformHardwareSegment ToHardwareSegment(
        PulseCurrentWaveformSegmentPlan segment) =>
        new(
            segment.WaveformType,
            segment.DurationMicroseconds,
            FrequencyHz: 0,
            Amplitude: 0,
            Offset: 0,
            PhaseDegree: 0,
            DutyPermilleOrOrder: 0,
            segment.LowDa,
            segment.HighDa,
            segment.RiseMicroseconds,
            segment.HighHoldMicroseconds,
            segment.FallMicroseconds,
            segment.LowHoldMicroseconds,
            SampleCount: 0,
            segment.RepeatCount,
            Flags: 0);

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

    private static uint MillisecondsToMicroseconds(decimal milliseconds, string name) =>
        ConvertToUInt32(milliseconds * 1_000m, name);

    private static uint ConvertToUInt32(decimal value, string name)
    {
        if (value is < uint.MinValue or > uint.MaxValue)
        {
            throw new OverflowException($"{name}换算后的硬件整数超出UInt32范围。");
        }

        return decimal.ToUInt32(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }
}
