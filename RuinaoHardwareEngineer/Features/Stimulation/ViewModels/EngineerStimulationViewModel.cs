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
    private string directCurrentMilliampere = "2";
    private string directZeroCurrentDac = "30000";
    private string directDacCountsPerMilliampere = string.Empty;
    private string directRampUpSeconds = "10";
    private string directRampDownSeconds = "10";
    private bool directReversePolarity;
    private string pulseCurrentMilliampere = "2";
    private string pulsePositiveDurationMilliseconds = "5";
    private string pulseInterphaseIntervalMilliseconds = "2";
    private string pulseNegativeDurationMilliseconds = "5";
    private string pulsePeriodIntervalMilliseconds = "8";
    private bool pulsePositiveFirst = true;
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
                OnPropertyChanged(nameof(ConversionPreview));
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

    public string DirectCurrentMilliampere
    {
        get => directCurrentMilliampere;
        set => SetConfigurationProperty(ref directCurrentMilliampere, value);
    }

    public string DirectZeroCurrentDac
    {
        get => directZeroCurrentDac;
        set => SetConfigurationProperty(ref directZeroCurrentDac, value);
    }

    public string DirectDacCountsPerMilliampere
    {
        get => directDacCountsPerMilliampere;
        set => SetConfigurationProperty(ref directDacCountsPerMilliampere, value);
    }

    public string DirectRampUpSeconds
    {
        get => directRampUpSeconds;
        set => SetConfigurationProperty(ref directRampUpSeconds, value);
    }

    public string DirectRampDownSeconds
    {
        get => directRampDownSeconds;
        set => SetConfigurationProperty(ref directRampDownSeconds, value);
    }

    public bool DirectReversePolarity
    {
        get => directReversePolarity;
        set => SetConfigurationProperty(ref directReversePolarity, value);
    }

    public string PulseCurrentMilliampere
    {
        get => pulseCurrentMilliampere;
        set => SetConfigurationProperty(ref pulseCurrentMilliampere, value);
    }

    public string PulsePositiveDurationMilliseconds
    {
        get => pulsePositiveDurationMilliseconds;
        set => SetConfigurationProperty(ref pulsePositiveDurationMilliseconds, value);
    }

    public string PulseInterphaseIntervalMilliseconds
    {
        get => pulseInterphaseIntervalMilliseconds;
        set => SetConfigurationProperty(ref pulseInterphaseIntervalMilliseconds, value);
    }

    public string PulseNegativeDurationMilliseconds
    {
        get => pulseNegativeDurationMilliseconds;
        set => SetConfigurationProperty(ref pulseNegativeDurationMilliseconds, value);
    }

    public string PulsePeriodIntervalMilliseconds
    {
        get => pulsePeriodIntervalMilliseconds;
        set => SetConfigurationProperty(ref pulsePeriodIntervalMilliseconds, value);
    }

    public bool PulsePositiveFirst
    {
        get => pulsePositiveFirst;
        set => SetConfigurationProperty(ref pulsePositiveFirst, value);
    }

    public bool IsDirectCurrent => string.Equals(SelectedMode, DirectCurrentMode, StringComparison.Ordinal);
    public bool IsPulseCurrent => string.Equals(SelectedMode, PulseCurrentMode, StringComparison.Ordinal);

    public string ConversionPreview
    {
        get
        {
            try
            {
                var configuration = BuildConfiguration();
                var waveform = configuration.Waveforms[0];
                return IsDirectCurrent
                    ? $"换算结果：总时间={configuration.TotalTimeMs}ms，基线DAC={waveform.LowLevelOrPositiveValue}，"
                        + $"目标DAC={waveform.HighLevelOrNegativeValue}，"
                        + $"上升/平台/下降={waveform.RisePermilleOrPositiveDurationUs}/"
                        + $"{waveform.HoldPermilleOrInterphaseIntervalUs}/"
                        + $"{waveform.FallPermilleOrNegativeDurationUs}‰"
                    : $"换算结果：电流={waveform.LowLevelOrPositiveValue}μA，单周期={waveform.DurationUs}μs，"
                        + $"总时间={configuration.TotalTimeMs}ms，flags=0x{waveform.Flags:X8}";
            }
            catch (Exception exception) when (exception is FormatException
                or ArgumentException
                or OverflowException)
            {
                return $"换算待完善：{exception.Message}";
            }
        }
    }

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
        var totalSeconds = ParseDecimal(TotalDurationSeconds, "总运行时间");
        var totalTimeMs = TesV15EngineeringUnitConverter.SecondsToMilliseconds(
            totalSeconds,
            "总运行时间");
        if (IsDirectCurrent)
        {
            var currentMilliampere = ParseDecimal(DirectCurrentMilliampere, "电流");
            var dacValues = TesV15EngineeringUnitConverter.DirectCurrentToDac(
                currentMilliampere,
                ParseUInt(DirectZeroCurrentDac, "零电流DAC值"),
                ParseDecimal(DirectDacCountsPerMilliampere, "每mA对应DAC计数"),
                DirectReversePolarity);
            var trapezoid = TesV15EngineeringUnitConverter.ToTrapezoidPermille(
                totalSeconds,
                ParseDecimal(DirectRampUpSeconds, "渐升时间"),
                ParseDecimal(DirectRampDownSeconds, "渐降时间"));
            return TesV15StimulationRegisterCodec.CreateDirectCurrent(
                channel,
                totalTimeMs,
                dacValues.BaselineDac,
                dacValues.TargetDac,
                trapezoid.RisePermille,
                trapezoid.HoldPermille,
                trapezoid.FallPermille);
        }

        var pulseMicroampere = TesV15EngineeringUnitConverter.MilliampereToMicroampere(
            ParseDecimal(PulseCurrentMilliampere, "电流"));
        return TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channel,
            totalTimeMs,
            PulsePositiveFirst,
            pulseMicroampere,
            pulseMicroampere,
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulsePositiveDurationMilliseconds, "正相持续时间"),
                "正相持续时间"),
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulseInterphaseIntervalMilliseconds, "相间隔"),
                "相间隔"),
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulseNegativeDurationMilliseconds, "负相持续时间"),
                "负相持续时间"),
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulsePeriodIntervalMilliseconds, "周期剩余间隔"),
                "周期剩余间隔"),
            valuesAreMicroampere: true);
    }

    private static uint ParseUInt(string text, string fieldName)
    {
        if (!uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{fieldName}必须是0到{uint.MaxValue}之间的整数。");
        }

        return value;
    }

    private static decimal ParseDecimal(string text, string fieldName)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{fieldName}必须是有效数字，小数点请使用“.”。");
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
            OnPropertyChanged(nameof(ConversionPreview));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
