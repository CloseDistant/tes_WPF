namespace RuinaoTesHardware;

/// <summary>
/// tACS产品级共享硬件API。第一版把渐升和渐降分别离散为2个严格等时正弦段。
/// 命令成功仅表示设备已接受配置，不代表示波器输出已经验证。
/// </summary>
public sealed class AlternatingCurrentStimulationClient
{
    public const decimal MaximumCalibrationCurrentMilliampere = 15.000m;
    public const uint ConfigurationVersion = 0x16;
    public const uint SineWaveformType = 2;
    public const uint SineDutyPermille = 500;
    public const uint SineSampleCount = 1_024;
    public const int RampSegmentCount = 2;
    public const decimal LowerEnvelopeCoefficient = 0.33m;
    public const decimal UpperEnvelopeCoefficient = 0.67m;

    private readonly CompositeStimulationHardwareWriter writer;

    public AlternatingCurrentStimulationClient(BackplaneClient client)
    {
        writer = new CompositeStimulationHardwareWriter(client);
    }

    public async Task<AlternatingCurrentStimulationConfigurationResult> ConfigureAsync(
        AlternatingCurrentStimulationParameters parameters,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(parameters);
        var hardwarePlan = ToHardwarePlan(plan);
        var waveformCommands = new List<StimulationHardwareCommandResult>(plan.Segments.Count);
        for (var index = 0; index < plan.Segments.Count; index++)
        {
            var segmentNumber = index + 1;
            var write = await ExecuteHardwareOperationAsync(
                $"下发tACS第{segmentNumber}段正弦配置",
                () => writer.WriteWaveformAsync(hardwarePlan, index, options, cancellationToken));
            waveformCommands.Add(ToProductResult(
                write,
                $"tACS第{segmentNumber}段正弦配置已被硬件接受，尚未执行状态回读验证。"));
        }

        var controlWrite = await ExecuteHardwareOperationAsync(
            "下发tACS通道总控制配置",
            () => writer.WriteControlAsync(hardwarePlan, options, cancellationToken));
        return new AlternatingCurrentStimulationConfigurationResult(
            plan,
            waveformCommands,
            ToProductResult(controlWrite, "tACS通道总控制配置已被硬件接受，尚未执行状态回读验证。"));
    }

    public async Task<StimulationHardwareCommandResult> StartAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "开始tACS",
            () => writer.StartAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "tACS业务板开始命令已被硬件接受，实际输出需由测量设备确认。");
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
            $"开始tACS通道0x{channelMask:X2}",
            () => writer.StartAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"tACS指定通道开始命令已被硬件接受：0x0002=0x{channelMask:X8}；实际输出需由测量设备确认。");
    }

    public async Task<StimulationHardwareCommandResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "停止tACS",
            () => writer.StopAsync(boardAddress, 0, options, cancellationToken));
        return ToProductResult(result, "tACS业务板停止命令已被硬件接受，硬件停止状态尚未回读验证。");
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
            $"停止tACS通道0x{channelMask:X2}",
            () => writer.StopAsync(boardAddress, channelMask, options, cancellationToken));
        return ToProductResult(
            result,
            $"tACS指定通道停止命令已被硬件接受：0x0003=0x{channelMask:X8}；硬件停止状态尚未回读验证。");
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

    public static AlternatingCurrentStimulationPlan CreatePlan(
        AlternatingCurrentStimulationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Validate(parameters);

        var totalMicroseconds = SecondsToMicroseconds(parameters.TotalDurationSeconds, "刺激总时间");
        var rampUpMicroseconds = SecondsToMicroseconds(parameters.RampUpSeconds, "渐升时间");
        var rampDownMicroseconds = SecondsToMicroseconds(parameters.RampDownSeconds, "渐降时间");
        var stableMicroseconds = checked(totalMicroseconds - rampUpMicroseconds - rampDownMicroseconds);
        var segments = new List<AlternatingCurrentWaveformSegmentPlan>(5);
        uint startMicroseconds = 0;

        if (rampUpMicroseconds > 0)
        {
            var duration = rampUpMicroseconds / RampSegmentCount;
            for (var step = 1; step <= RampSegmentCount; step++)
            {
                AddSegment(
                    segments,
                    parameters,
                    AlternatingCurrentWaveformStage.RampUp,
                    startMicroseconds,
                    duration,
                    step == 1 ? LowerEnvelopeCoefficient : UpperEnvelopeCoefficient);
                startMicroseconds = checked(startMicroseconds + duration);
            }
        }

        if (stableMicroseconds > 0)
        {
            AddSegment(
                segments,
                parameters,
                AlternatingCurrentWaveformStage.Stable,
                startMicroseconds,
                stableMicroseconds,
                1m);
            startMicroseconds = checked(startMicroseconds + stableMicroseconds);
        }

        if (rampDownMicroseconds > 0)
        {
            var duration = rampDownMicroseconds / RampSegmentCount;
            for (var step = 1; step <= RampSegmentCount; step++)
            {
                AddSegment(
                    segments,
                    parameters,
                    AlternatingCurrentWaveformStage.RampDown,
                    startMicroseconds,
                    duration,
                    step == 1 ? UpperEnvelopeCoefficient : LowerEnvelopeCoefficient);
                startMicroseconds = checked(startMicroseconds + duration);
            }
        }

        if (startMicroseconds != totalMicroseconds)
        {
            throw new InvalidOperationException("tACS分段时长之和与刺激总时间不一致。");
        }

        return new AlternatingCurrentStimulationPlan(
            parameters,
            CompositeStimulationHardwareWriter.CreateSingleChannelMask(parameters.Channel),
            ConfigurationVersion,
            SecondsToMilliseconds(parameters.TotalDurationSeconds, "刺激总时间"),
            segments);
    }

    public static uint ConvertPeakCurrentToDa(decimal peakCurrentMilliampere)
    {
        if (peakCurrentMilliampere is < 0m or > MaximumCalibrationCurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(peakCurrentMilliampere),
                $"正弦单边峰值必须在0～{MaximumCalibrationCurrentMilliampere:0.000}mA之间。");
        }

        return decimal.ToUInt32(decimal.Round(
            peakCurrentMilliampere / MaximumCalibrationCurrentMilliampere * short.MaxValue,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static void AddSegment(
        ICollection<AlternatingCurrentWaveformSegmentPlan> segments,
        AlternatingCurrentStimulationParameters parameters,
        AlternatingCurrentWaveformStage stage,
        uint startMicroseconds,
        uint durationMicroseconds,
        decimal envelopeCoefficient)
    {
        var peakCurrent = decimal.Round(
            parameters.PeakCurrentMilliampere * envelopeCoefficient,
            6,
            MidpointRounding.AwayFromZero);
        segments.Add(new AlternatingCurrentWaveformSegmentPlan(
            segments.Count + 1,
            stage,
            startMicroseconds,
            durationMicroseconds,
            envelopeCoefficient,
            peakCurrent,
            parameters.FrequencyHz,
            ConvertPeakCurrentToDa(peakCurrent),
            CalculatePhaseDegree(parameters.FrequencyHz, startMicroseconds)));
    }

    private static uint CalculatePhaseDegree(uint frequencyHz, uint startMicroseconds)
    {
        var degree = decimal.Remainder(
            frequencyHz * (decimal)startMicroseconds * 360m / 1_000_000m,
            360m);
        return decimal.ToUInt32(decimal.Round(degree, 0, MidpointRounding.AwayFromZero)) % 360U;
    }

    private static CompositeStimulationHardwarePlan ToHardwarePlan(
        AlternatingCurrentStimulationPlan plan) =>
        new(
            plan.Parameters.BoardAddress,
            plan.Parameters.Channel,
            plan.EnableMask,
            plan.ConfigurationVersion,
            plan.TotalTimeMilliseconds,
            plan.Segments.Select(ToHardwareSegment).ToArray());

    private static StimulationWaveformHardwareSegment ToHardwareSegment(
        AlternatingCurrentWaveformSegmentPlan segment) =>
        new(
            SineWaveformType,
            segment.DurationMicroseconds,
            segment.FrequencyHz,
            segment.AmplitudeDa,
            Offset: 0,
            segment.PhaseDegree,
            SineDutyPermille,
            LowLevelOrPositiveValue: 0,
            HighLevelOrNegativeValue: 0,
            RisePermilleOrPositiveDurationMicroseconds: 0,
            HoldPermilleOrInterphaseIntervalMicroseconds: 0,
            FallPermilleOrNegativeDurationMicroseconds: 0,
            CustomIdOrSeedOrPeriodIntervalMicroseconds: 0,
            SineSampleCount,
            RepeatCount: 1,
            Flags: 0);

    private static void Validate(AlternatingCurrentStimulationParameters parameters)
    {
        CompositeStimulationHardwareWriter.ValidateBoardAddress(parameters.BoardAddress);
        CompositeStimulationHardwareWriter.ValidateChannel(parameters.Channel);
        ValidateDecimalParameter(
            AlternatingCurrentParameterKind.PeakCurrentMilliampere,
            parameters.PeakCurrentMilliampere);
        ValidateDecimalParameter(AlternatingCurrentParameterKind.RampUpSeconds, parameters.RampUpSeconds);
        ValidateDecimalParameter(AlternatingCurrentParameterKind.RampDownSeconds, parameters.RampDownSeconds);
        ValidateDecimalParameter(AlternatingCurrentParameterKind.FrequencyHz, parameters.FrequencyHz);
        ValidateDecimalParameter(
            AlternatingCurrentParameterKind.TotalDurationSeconds,
            parameters.TotalDurationSeconds);

        if (parameters.RampUpSeconds + parameters.RampDownSeconds > parameters.TotalDurationSeconds)
        {
            throw new ArgumentException("刺激总时间不能小于渐升时间与渐降时间之和。", nameof(parameters));
        }
    }

    private static void ValidateDecimalParameter(AlternatingCurrentParameterKind kind, decimal value)
    {
        if (!AlternatingCurrentParameterRules.TryValidate(
                kind,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                out _,
                out var error))
        {
            throw new ArgumentOutOfRangeException(kind.ToString(), error);
        }
    }

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
        catch (OperationCanceledException)
        {
            throw;
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
