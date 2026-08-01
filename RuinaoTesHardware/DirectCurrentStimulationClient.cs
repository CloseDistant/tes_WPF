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

    private const ushort StartRegister = 0x0002;
    private const ushort StopRegister = 0x0003;
    private readonly BackplaneClient client;

    public DirectCurrentStimulationClient(BackplaneClient client)
    {
        this.client = client;
    }

    public async Task<DirectCurrentConfigurationResult> ConfigureAsync(
        DirectCurrentStimulationParameters parameters,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(parameters);
        var waveformWrite = await ExecuteHardwareOperationAsync(
            "下发类型8梯形配置",
            () => client.WriteRegistersAsync(
                parameters.BoardAddress,
                BuildWaveformRegisters(plan),
                options,
                cancellationToken));
        var controlWrite = await ExecuteHardwareOperationAsync(
            "下发通道总控制配置",
            () => client.WriteRegistersAsync(
                parameters.BoardAddress,
                BuildControlRegisters(plan),
                options,
                cancellationToken));

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
            () => WriteCommandAsync(boardAddress, StartRegister, options, cancellationToken));
        return ToProductResult(result, "开始刺激命令已被硬件接受，实际输出需由测量设备确认。");
    }

    public async Task<DirectCurrentCommandResult> StopAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteHardwareOperationAsync(
            "停止直流电刺激",
            () => WriteCommandAsync(boardAddress, StopRegister, options, cancellationToken));
        return ToProductResult(result, "停止刺激命令已被硬件接受，硬件停止状态尚未回读验证。");
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

    private async Task<BackplaneRegisterOperationResult> WriteCommandAsync(
        byte boardAddress,
        ushort registerAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken)
    {
        if (boardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(nameof(boardAddress), "业务板地址必须在0x00～0x07之间。");
        }

        return await client.WriteRegistersAsync(
            boardAddress,
            [new TesV14RegisterValue(registerAddress, 0)],
            options,
            cancellationToken);
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

    private static IReadOnlyList<TesV14RegisterValue> BuildWaveformRegisters(
        DirectCurrentStimulationPlan plan)
    {
        var waveBase = checked((ushort)(GetChannelBase(plan.Parameters.Channel) + 0x20));
        return
        [
            new(waveBase, plan.WaveformType),
            new((ushort)(waveBase + 0x01), plan.DurationMicroseconds),
            new((ushort)(waveBase + 0x02), 0),
            new((ushort)(waveBase + 0x03), 0),
            new((ushort)(waveBase + 0x04), 0),
            new((ushort)(waveBase + 0x05), 0),
            new((ushort)(waveBase + 0x06), 0),
            new((ushort)(waveBase + 0x07), unchecked((uint)plan.LowDa)),
            new((ushort)(waveBase + 0x08), unchecked((uint)plan.HighDa)),
            new((ushort)(waveBase + 0x09), plan.RiseMicroseconds),
            new((ushort)(waveBase + 0x0A), plan.HighHoldMicroseconds),
            new((ushort)(waveBase + 0x0B), plan.FallMicroseconds),
            new((ushort)(waveBase + 0x0C), plan.LowHoldMicroseconds),
            new((ushort)(waveBase + 0x0D), 0),
            new((ushort)(waveBase + 0x0E), 1),
            new((ushort)(waveBase + 0x0F), 0),
        ];
    }

    private static IReadOnlyList<TesV14RegisterValue> BuildControlRegisters(
        DirectCurrentStimulationPlan plan)
    {
        var channelBase = GetChannelBase(plan.Parameters.Channel);
        return
        [
            new(0x2E00, plan.EnableMask),
            new(0x2E01, plan.ConfigurationVersion),
            new(channelBase, (uint)(plan.Parameters.Channel - 1)),
            new((ushort)(channelBase + 0x01), 0),
            new((ushort)(channelBase + 0x02), 0),
            new((ushort)(channelBase + 0x03), plan.TotalTimeMilliseconds),
            new((ushort)(channelBase + 0x04), 1),
            new((ushort)(channelBase + 0x05), 0),
        ];
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

    private static ushort GetChannelBase(int channel) =>
        checked((ushort)(0x3000 + (channel - 1) * 0x0200));

    private static void Validate(DirectCurrentStimulationParameters parameters)
    {
        if (parameters.BoardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "业务板地址必须在0x00～0x07之间。");
        }

        if (parameters.Channel is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "刺激通道必须在1～8之间。");
        }

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
