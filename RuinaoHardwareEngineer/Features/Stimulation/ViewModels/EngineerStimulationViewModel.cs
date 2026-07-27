using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using RuinaoHardwareEngineer.Features.Stimulation.Services;
using RuinaoTesHardware;
using RuinaoTesProtocol.V15;

namespace RuinaoHardwareEngineer.Features.Stimulation.ViewModels;

public sealed class EngineerStimulationViewModel : INotifyPropertyChanged
{
    private readonly IEngineerStimulationService service;
    private string selectedMode = DirectCurrentMode;
    private int selectedBoardAddress;
    private int selectedChannel = 1;
    private string totalDurationSeconds = "120";
    private string directLowLevel = "10000";
    private string directHighLevel = "50000";
    private string directRisePermille = "200";
    private string directHoldPermille = "500";
    private string directFallPermille = "300";
    private string pulsePositiveValue = "36000";
    private string pulseNegativeValue = "24000";
    private string pulsePositiveDurationUs = "5000";
    private string pulseInterphaseIntervalUs = "2000";
    private string pulseNegativeDurationUs = "5000";
    private string pulsePeriodIntervalUs = "8000";
    private bool pulsePositiveFirst = true;
    private bool pulseValuesAreMicroampere;
    private bool isConfigured;
    private bool isRunning;
    private string statusText = "尚未下发刺激配置";

    public const string DirectCurrentMode = "tDCS · 梯形";
    public const string PulseCurrentMode = "tPCS · 电刺激脉冲";

    public EngineerStimulationViewModel(IEngineerStimulationService service)
    {
        this.service = service;
    }

    public IReadOnlyList<string> Modes { get; } = [DirectCurrentMode, PulseCurrentMode];
    public IReadOnlyList<int> BoardAddresses { get; } = [0, 1];
    public IReadOnlyList<int> Channels { get; } = Enumerable.Range(1, 8).ToArray();

    public string SelectedMode
    {
        get => selectedMode;
        set
        {
            if (SetProperty(ref selectedMode, value))
            {
                IsConfigured = false;
                OnPropertyChanged(nameof(IsDirectCurrent));
                OnPropertyChanged(nameof(IsPulseCurrent));
            }
        }
    }

    public int SelectedBoardAddress
    {
        get => selectedBoardAddress;
        set
        {
            if (SetProperty(ref selectedBoardAddress, value))
            {
                IsConfigured = false;
            }
        }
    }

    public int SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (SetProperty(ref selectedChannel, value))
            {
                IsConfigured = false;
            }
        }
    }

    public string TotalDurationSeconds
    {
        get => totalDurationSeconds;
        set => SetConfigurationProperty(ref totalDurationSeconds, value);
    }

    public string DirectLowLevel
    {
        get => directLowLevel;
        set => SetConfigurationProperty(ref directLowLevel, value);
    }

    public string DirectHighLevel
    {
        get => directHighLevel;
        set => SetConfigurationProperty(ref directHighLevel, value);
    }

    public string DirectRisePermille
    {
        get => directRisePermille;
        set => SetConfigurationProperty(ref directRisePermille, value);
    }

    public string DirectHoldPermille
    {
        get => directHoldPermille;
        set => SetConfigurationProperty(ref directHoldPermille, value);
    }

    public string DirectFallPermille
    {
        get => directFallPermille;
        set => SetConfigurationProperty(ref directFallPermille, value);
    }

    public string PulsePositiveValue
    {
        get => pulsePositiveValue;
        set => SetConfigurationProperty(ref pulsePositiveValue, value);
    }

    public string PulseNegativeValue
    {
        get => pulseNegativeValue;
        set => SetConfigurationProperty(ref pulseNegativeValue, value);
    }

    public string PulsePositiveDurationUs
    {
        get => pulsePositiveDurationUs;
        set => SetConfigurationProperty(ref pulsePositiveDurationUs, value);
    }

    public string PulseInterphaseIntervalUs
    {
        get => pulseInterphaseIntervalUs;
        set => SetConfigurationProperty(ref pulseInterphaseIntervalUs, value);
    }

    public string PulseNegativeDurationUs
    {
        get => pulseNegativeDurationUs;
        set => SetConfigurationProperty(ref pulseNegativeDurationUs, value);
    }

    public string PulsePeriodIntervalUs
    {
        get => pulsePeriodIntervalUs;
        set => SetConfigurationProperty(ref pulsePeriodIntervalUs, value);
    }

    public bool PulsePositiveFirst
    {
        get => pulsePositiveFirst;
        set => SetConfigurationProperty(ref pulsePositiveFirst, value);
    }

    public bool PulseValuesAreMicroampere
    {
        get => pulseValuesAreMicroampere;
        set => SetConfigurationProperty(ref pulseValuesAreMicroampere, value);
    }

    public bool IsDirectCurrent => string.Equals(SelectedMode, DirectCurrentMode, StringComparison.Ordinal);
    public bool IsPulseCurrent => string.Equals(SelectedMode, PulseCurrentMode, StringComparison.Ordinal);

    public bool IsConfigured
    {
        get => isConfigured;
        private set => SetProperty(ref isConfigured, value);
    }

    public bool IsRunning
    {
        get => isRunning;
        private set => SetProperty(ref isRunning, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public async Task ConfigureAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var configuration = BuildConfiguration();
        var targetAddress = checked((byte)SelectedBoardAddress);
        var result = await service.ConfigureAsync(targetAddress, configuration, options, cancellationToken);
        IsConfigured = true;
        IsRunning = false;
        StatusText = $"配置成功 · 业务板0x{targetAddress:X2} · 通道{result.ChannelNumber} · {SelectedMode}";
    }

    public async Task StartAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("请先下发当前刺激配置并收到硬件回复。");
        }

        var targetAddress = checked((byte)SelectedBoardAddress);
        await service.StartAsync(targetAddress, options, cancellationToken);
        IsRunning = true;
        StatusText = $"开始命令已收到硬件回复 · 业务板0x{targetAddress:X2} · 通道{SelectedChannel}";
    }

    public async Task StopAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var targetAddress = checked((byte)SelectedBoardAddress);
        await service.StopAsync(targetAddress, options, cancellationToken);
        IsRunning = false;
        StatusText = $"停止命令已收到硬件回复 · 业务板0x{targetAddress:X2}";
    }

    public async Task ReadStatusAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var targetAddress = checked((byte)SelectedBoardAddress);
        var status = await service.ReadStatusAsync(targetAddress, options, cancellationToken);
        var channelMask = 1U << (SelectedChannel - 1);
        IsRunning = (status.RunStateMask & channelMask) != 0;
        StatusText = $"配置状态=0x{status.ConfigurationStatus:X8} · 运行掩码=0x{status.RunStateMask:X8}";
    }

    public void ResetConnectionState()
    {
        IsConfigured = false;
        IsRunning = false;
        StatusText = "设备已断联，需要重新下发刺激配置";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private TesV15StimulationConfiguration BuildConfiguration()
    {
        var channel = checked((byte)SelectedChannel);
        var totalSeconds = ParseUInt(TotalDurationSeconds, "总运行时间");
        var totalTimeMs = checked(totalSeconds * 1000U);
        if (IsDirectCurrent)
        {
            return TesV15StimulationRegisterCodec.CreateDirectCurrent(
                channel,
                totalTimeMs,
                ParseUInt(DirectLowLevel, "低电平DAC值"),
                ParseUInt(DirectHighLevel, "高电平DAC值"),
                ParseUInt(DirectRisePermille, "上升占比"),
                ParseUInt(DirectHoldPermille, "平台占比"),
                ParseUInt(DirectFallPermille, "下降占比"));
        }

        return TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channel,
            totalTimeMs,
            PulsePositiveFirst,
            ParseUInt(PulsePositiveValue, "正相刺激值"),
            ParseUInt(PulseNegativeValue, "负相刺激值"),
            ParseUInt(PulsePositiveDurationUs, "正相持续时间"),
            ParseUInt(PulseInterphaseIntervalUs, "相间隔"),
            ParseUInt(PulseNegativeDurationUs, "负相持续时间"),
            ParseUInt(PulsePeriodIntervalUs, "周期剩余间隔"),
            valuesAreMicroampere: PulseValuesAreMicroampere);
    }

    private static uint ParseUInt(string text, string fieldName)
    {
        if (!uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{fieldName}必须是0到{uint.MaxValue}之间的整数。");
        }

        return value;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetConfigurationProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            IsConfigured = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
