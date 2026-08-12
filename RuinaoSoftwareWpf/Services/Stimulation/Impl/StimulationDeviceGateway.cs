namespace RuinaoSoftwareWpf;

using System.Globalization;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 将纯应用层设备契约映射到当前正式硬件执行链。
/// </summary>
public sealed class StimulationDeviceGateway : IStimulationDeviceGateway
{
    private readonly IHardwareService hardwareService;
    private readonly TimeProvider timeProvider;
    private TiGroup? configuredGroup;
    private PrescriptionDefinition? configuredPrescription;
    private string configuredChannelNames = string.Empty;

    public StimulationDeviceGateway(
        IHardwareService hardwareService,
        TimeProvider timeProvider)
    {
        this.hardwareService = hardwareService;
        this.timeProvider = timeProvider;
    }

    public StimulationDeviceSnapshot Current => CreateSnapshot();

    public async Task<StimulationCommandResult> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        return Map(await hardwareService.ConnectAsync(cancellationToken));
    }

    public async Task<StimulationCommandResult> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await hardwareService.DisconnectAsync(cancellationToken);
        return new StimulationCommandResult(
            StimulationCommandStatus.Confirmed,
            CreateSnapshot(result.FooterStatus),
            Message: result.UserMessage ?? result.FooterStatus);
    }

    public async Task<StimulationCommandResult> CheckImpedanceAsync(
        CancellationToken cancellationToken = default)
    {
        return Map(await hardwareService.CheckImpedanceAsync(cancellationToken));
    }

    public Task<StimulationCommandResult> ConfigureAsync(
        StimulationProgram program,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        ValidateProgram(program);

        configuredGroup = ToLegacyGroup(program);
        configuredPrescription = ToLegacyPrescription(program);
        configuredChannelNames = string.Join(
            ", ",
            program.Channels.Select(channel => $"CH {channel.ChannelNumber}"));

        return Task.FromResult(new StimulationCommandResult(
            StimulationCommandStatus.Accepted,
            CreateSnapshot(),
            Message: "刺激参数已通过应用层契约校验，等待启动命令下发。"));
    }

    public async Task<StimulationCommandResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        if (configuredGroup is null || configuredPrescription is null)
        {
            return Rejected("STIMULATION_NOT_CONFIGURED", "尚未配置刺激参数。");
        }

        return Map(await hardwareService.StartGroupAsync(
            configuredGroup,
            configuredChannelNames,
            configuredPrescription,
            cancellationToken));
    }

    public async Task<StimulationCommandResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (configuredGroup is null)
        {
            return Rejected("STIMULATION_NOT_CONFIGURED", "尚未配置刺激参数。");
        }

        return Map(await hardwareService.StopGroupAsync(
            configuredGroup,
            configuredChannelNames,
            configuredPrescription?.StimulationType ?? StimulationModeCodes.TemporalInterference,
            cancellationToken));
    }

    public async Task<StimulationCommandResult> EmergencyStopAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (configuredGroup is null)
        {
            return Rejected("STIMULATION_NOT_CONFIGURED", reason);
        }

        return Map(await hardwareService.EmergencyStopGroupAsync(
            configuredGroup,
            configuredChannelNames,
            configuredPrescription?.StimulationType ?? StimulationModeCodes.TemporalInterference,
            cancellationToken));
    }

    private StimulationCommandResult Map(HardwareOperationResult result)
    {
        return new StimulationCommandResult(
            result.IsConnected
                ? StimulationCommandStatus.Confirmed
                : StimulationCommandStatus.Disconnected,
            CreateSnapshot(result.FooterStatus),
            result.IsConnected ? null : "DEVICE_DISCONNECTED",
            result.UserMessage ?? result.FooterStatus);
    }

    private StimulationCommandResult Rejected(string errorCode, string message)
    {
        return new StimulationCommandResult(
            StimulationCommandStatus.Rejected,
            CreateSnapshot(message),
            errorCode,
            message);
    }

    private StimulationDeviceSnapshot CreateSnapshot(string? detail = null)
    {
        var state = hardwareService.IsConnecting
            ? StimulationDeviceConnectionState.Connecting
            : hardwareService.IsConnected
                ? StimulationDeviceConnectionState.Connected
                : StimulationDeviceConnectionState.Disconnected;
        return new StimulationDeviceSnapshot(
            state,
            timeProvider.GetUtcNow(),
            detail);
    }

    private static void ValidateProgram(StimulationProgram program)
    {
        if (program.Channels.Count == 0)
        {
            throw new ArgumentException("刺激程序必须至少包含一个通道。", nameof(program));
        }

        if (program.Channels.Select(channel => channel.ChannelNumber).Distinct().Count()
            != program.Channels.Count)
        {
            throw new ArgumentException("刺激程序不能包含重复通道。", nameof(program));
        }

        foreach (var channel in program.Channels)
        {
            if (channel.ChannelNumber <= 0
                || channel.AnodeElectrodeNumber <= 0
                || channel.CathodeElectrodeNumber <= 0
                || channel.AnodeElectrodeNumber == channel.CathodeElectrodeNumber
                || channel.CurrentMilliampere < 0
                || channel.FrequencyHz < 0
                || channel.DurationSeconds <= 0
                || channel.RampUpSeconds < 0
                || channel.RampDownSeconds < 0)
            {
                throw new ArgumentException("刺激通道参数不合法。", nameof(program));
            }
        }
    }

    private static TiGroup ToLegacyGroup(StimulationProgram program)
    {
        var group = new TiGroup
        {
            Title = program.DisplayName
        };

        foreach (var channel in program.Channels)
        {
            group.Channels.Add(new ChannelConfig
            {
                Name = $"CH {channel.ChannelNumber}",
                Anode = $"E{channel.AnodeElectrodeNumber}",
                Cathode = $"E{channel.CathodeElectrodeNumber}",
                CurrentMA = channel.CurrentMilliampere.ToString(CultureInfo.InvariantCulture),
                FrequencyHz = channel.FrequencyHz.ToString(CultureInfo.InvariantCulture),
                RampUpS = channel.RampUpSeconds.ToString(CultureInfo.InvariantCulture),
                RampDownS = channel.RampDownSeconds.ToString(CultureInfo.InvariantCulture),
                DurationS = channel.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                IntervalS = channel.IntervalSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StimulationMode = program.DeliveryMode == StimulationDeliveryMode.Continuous
                    ? PrescriptionDeliveryModes.Continuous
                    : PrescriptionDeliveryModes.Interval
            });
        }

        return group;
    }

    private static PrescriptionDefinition ToLegacyPrescription(StimulationProgram program)
    {
        var maximumDurationSeconds = program.Channels.Max(channel => channel.DurationSeconds);
        var maximumIntervalSeconds = program.Channels
            .Where(channel => channel.IntervalSeconds.HasValue)
            .Select(channel => channel.IntervalSeconds!.Value)
            .DefaultIfEmpty()
            .Max();

        return new PrescriptionDefinition(
            program.ProgramId,
            program.DisplayName,
            string.Empty,
            program.StimulationType,
            (double)program.Channels.Max(channel => channel.CurrentMilliampere),
            program.DeliveryMode == StimulationDeliveryMode.Continuous
                ? PrescriptionDeliveryModes.Continuous
                : PrescriptionDeliveryModes.Interval,
            Math.Max(1, (int)Math.Ceiling(maximumDurationSeconds / 60d)),
            maximumIntervalSeconds == 0
                ? null
                : Math.Max(1, (int)Math.Ceiling(maximumIntervalSeconds / 60d)),
            program.DeliveryMode == StimulationDeliveryMode.Continuous
                ? null
                : Math.Max(1, (int)Math.Ceiling(maximumDurationSeconds / 60d)),
            string.Empty,
            program.Channels.Max(channel => channel.RampUpSeconds),
            program.Channels.Max(channel => channel.RampDownSeconds),
            string.Empty,
            false);
    }
}
