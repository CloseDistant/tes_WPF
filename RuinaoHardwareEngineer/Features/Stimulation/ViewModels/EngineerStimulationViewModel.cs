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
    private int selectedBoardAddress = 1;
    private int selectedChannel = 1;
    private string totalDurationSeconds = "120";
    private string zeroCurrentDac = "30000";
    private string dacCountsPerMilliampere = string.Empty;
    private bool reversePolarity;

    private string directCurrentMilliampere = "2";
    private string directRampUpSeconds = "10";
    private string directRampDownSeconds = "10";
    private bool directIntervalMode;
    private string directSingleDurationSeconds = "60";
    private string directIntervalSeconds = "5";

    private string pulseCurrentMilliampere = "2";
    private string pulseRiseWidthMilliseconds = "5";
    private string pulseWidthMilliseconds = "10";
    private string pulseIntervalWidthMilliseconds = "20";

    private bool isConfigurationConfirmed;
    private bool isRunning;
    private bool isStartCommandConfirmed;
    private string statusText = "尚未下发刺激配置";

    public const string DirectCurrentMode = "tDCS · 梯形";
    public const string PulseCurrentMode = "tPCS · 梯形脉冲";

    public EngineerStimulationViewModel(IEngineerStimulationService service)
    {
        this.service = service;
    }

    public IReadOnlyList<string> Modes { get; } = [DirectCurrentMode, PulseCurrentMode];
    // 当前实物业务板使用0x01；放在首项可避免ComboBox初始化期间短暂选中0x00并回写。
    public IReadOnlyList<int> BoardAddresses { get; } = [1, 0];
    public IReadOnlyList<int> Channels { get; } = Enumerable.Range(1, 8).ToArray();

    public string SelectedMode
    {
        get => selectedMode;
        set
        {
            if (SetProperty(ref selectedMode, value))
            {
                InvalidateConfiguration();
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
                InvalidateConfiguration();
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
                InvalidateConfiguration();
            }
        }
    }

    public string TotalDurationSeconds
    {
        get => totalDurationSeconds;
        set => SetConfigurationProperty(ref totalDurationSeconds, value);
    }

    public string ZeroCurrentDac
    {
        get => zeroCurrentDac;
        set => SetConfigurationProperty(ref zeroCurrentDac, value);
    }

    public string DacCountsPerMilliampere
    {
        get => dacCountsPerMilliampere;
        set => SetConfigurationProperty(ref dacCountsPerMilliampere, value);
    }

    public bool ReversePolarity
    {
        get => reversePolarity;
        set => SetConfigurationProperty(ref reversePolarity, value);
    }

    public string DirectCurrentMilliampere
    {
        get => directCurrentMilliampere;
        set => SetConfigurationProperty(ref directCurrentMilliampere, value);
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

    public bool DirectIntervalMode
    {
        get => directIntervalMode;
        set => SetConfigurationProperty(ref directIntervalMode, value);
    }

    public string DirectSingleDurationSeconds
    {
        get => directSingleDurationSeconds;
        set => SetConfigurationProperty(ref directSingleDurationSeconds, value);
    }

    public string DirectIntervalSeconds
    {
        get => directIntervalSeconds;
        set => SetConfigurationProperty(ref directIntervalSeconds, value);
    }

    public string PulseCurrentMilliampere
    {
        get => pulseCurrentMilliampere;
        set => SetConfigurationProperty(ref pulseCurrentMilliampere, value);
    }

    public string PulseRiseWidthMilliseconds
    {
        get => pulseRiseWidthMilliseconds;
        set => SetConfigurationProperty(ref pulseRiseWidthMilliseconds, value);
    }

    public string PulseWidthMilliseconds
    {
        get => pulseWidthMilliseconds;
        set => SetConfigurationProperty(ref pulseWidthMilliseconds, value);
    }

    public string PulseIntervalWidthMilliseconds
    {
        get => pulseIntervalWidthMilliseconds;
        set => SetConfigurationProperty(ref pulseIntervalWidthMilliseconds, value);
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
                var active = configuration.Waveforms[0];
                var intervalDescription = configuration.Waveforms.Count == 2
                    ? $"，间隔段={configuration.Waveforms[1].DurationUs}μs(type=1)"
                    : string.Empty;
                return $"换算结果：type={(uint)active.Mode}，刺激段={active.DurationUs}μs，"
                    + $"基线/目标DAC={active.LowLevelOrPositiveValue}/{active.HighLevelOrNegativeValue}，"
                    + $"上升/平台/渐降={active.RisePermilleOrPositiveDurationUs}/"
                    + $"{active.HoldPermilleOrInterphaseIntervalUs}/"
                    + $"{active.FallPermilleOrNegativeDurationUs}‰，"
                    + $"波形数={configuration.Waveforms.Count}，循环标志={configuration.ChannelFlags & 1U}"
                    + intervalDescription;
            }
            catch (Exception exception) when (exception is FormatException
                or ArgumentException
                or OverflowException)
            {
                return $"换算待完善：{exception.Message}";
            }
        }
    }

    /// <summary>全部配置帧是否已收到业务板逐帧回复。</summary>
    public bool IsConfigurationConfirmed
    {
        get => isConfigurationConfirmed;
        private set => SetProperty(ref isConfigurationConfirmed, value);
    }

    /// <summary>仅由状态寄存器回读更新，不根据开始命令的USB发送结果推断。</summary>
    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value))
            {
                OnPropertyChanged(nameof(CanEditConfiguration));
            }
        }
    }

    public bool IsStartCommandConfirmed
    {
        get => isStartCommandConfirmed;
        private set
        {
            if (SetProperty(ref isStartCommandConfirmed, value))
            {
                OnPropertyChanged(nameof(CanEditConfiguration));
            }
        }
    }

    public bool CanEditConfiguration => !IsRunning && !IsStartCommandConfirmed;

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
        if (configuration.Waveforms.Any(waveform => (uint)waveform.Mode == 10U))
        {
            throw new InvalidOperationException("当前产品模式禁止生成类型10电刺激脉冲。");
        }

        var targetAddress = checked((byte)SelectedBoardAddress);
        var result = await service.ConfigureAsync(targetAddress, configuration, options, cancellationToken);
        IsConfigurationConfirmed = true;
        IsRunning = false;
        IsStartCommandConfirmed = false;
        StatusText = $"配置成功，已收到业务板逐帧回复 · 业务板0x{targetAddress:X2} "
            + $"· 通道{result.ChannelNumber} · {SelectedMode}";
    }

    public async Task StartAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigurationConfirmed)
        {
            throw new InvalidOperationException("请先下发配置并收到业务板的完整回复。");
        }

        var targetAddress = checked((byte)SelectedBoardAddress);
        await service.StartAsync(targetAddress, options, cancellationToken);
        IsStartCommandConfirmed = true;
        StatusText = $"开始命令0x0002已收到业务板回复 · 业务板0x{targetAddress:X2} "
            + $"· 通道{SelectedChannel}";
    }

    public async Task StopAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var targetAddress = checked((byte)SelectedBoardAddress);
        await service.StopAsync(targetAddress, options, cancellationToken);
        IsStartCommandConfirmed = false;
        IsRunning = false;
        StatusText = $"停止命令0x0003已收到业务板回复 · 业务板0x{targetAddress:X2}";
    }

    public async Task ReadStatusAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var targetAddress = checked((byte)SelectedBoardAddress);
        var status = await service.ReadStatusAsync(targetAddress, options, cancellationToken);
        var channelMask = 1U << (SelectedChannel - 1);
        IsRunning = (status.RunStateMask & channelMask) != 0;
        IsStartCommandConfirmed = IsRunning;
        StatusText = $"配置状态=0x{status.ConfigurationStatus:X8} · 运行掩码=0x{status.RunStateMask:X8}";
    }

    public void ResetConnectionState()
    {
        IsConfigurationConfirmed = false;
        IsRunning = false;
        IsStartCommandConfirmed = false;
        StatusText = "设备已断联，需要重新下发刺激配置";
    }

    public void ReportOperationFailure(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        if (string.Equals(operation, "下发配置", StringComparison.Ordinal))
        {
            IsConfigurationConfirmed = false;
            IsStartCommandConfirmed = false;
        }

        StatusText = $"{operation}失败 · {exception.Message}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private TesV15StimulationConfiguration BuildConfiguration()
    {
        var channel = checked((byte)SelectedChannel);
        var totalSeconds = ParseDecimal(TotalDurationSeconds, "总运行时间");
        var totalTimeMs = TesV15EngineeringUnitConverter.SecondsToMilliseconds(
            totalSeconds,
            "总运行时间");
        var currentMilliampere = ParseDecimal(
            IsDirectCurrent ? DirectCurrentMilliampere : PulseCurrentMilliampere,
            "电流");
        var dacValues = TesV15EngineeringUnitConverter.DirectCurrentToDac(
            currentMilliampere,
            ParseUInt(ZeroCurrentDac, "零电流DAC值"),
            ParseDecimal(DacCountsPerMilliampere, "每mA对应DAC计数"),
            ReversePolarity);

        if (IsDirectCurrent)
        {
            var activeSeconds = DirectIntervalMode
                ? ParseDecimal(DirectSingleDurationSeconds, "单次时长")
                : totalSeconds;
            var rampUpSeconds = ParseDecimal(DirectRampUpSeconds, "渐升时间");
            var rampDownSeconds = ParseDecimal(DirectRampDownSeconds, "渐降时间");
            var trapezoid = TesV15EngineeringUnitConverter.ToTrapezoidPermille(
                activeSeconds,
                rampUpSeconds,
                rampDownSeconds);
            var intervalUs = DirectIntervalMode
                ? TesV15EngineeringUnitConverter.SecondsToMicroseconds(
                    ParseDecimal(DirectIntervalSeconds, "间隔时间"),
                    "间隔时间")
                : 0U;
            return TesV15StimulationRegisterCodec.CreateDirectCurrent(
                channel,
                totalTimeMs,
                TesV15EngineeringUnitConverter.SecondsToMicroseconds(activeSeconds, "单次时长"),
                intervalUs,
                dacValues.BaselineDac,
                dacValues.TargetDac,
                trapezoid.RisePermille,
                trapezoid.HoldPermille,
                trapezoid.FallPermille);
        }

        return TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channel,
            totalTimeMs,
            dacValues.BaselineDac,
            dacValues.TargetDac,
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulseRiseWidthMilliseconds, "上升宽度"),
                "上升宽度"),
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulseWidthMilliseconds, "脉冲宽度"),
                "脉冲宽度"),
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
                ParseDecimal(PulseIntervalWidthMilliseconds, "间隔宽度"),
                "间隔宽度"));
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
            InvalidateConfiguration();
        }
    }

    private void InvalidateConfiguration()
    {
        IsConfigurationConfirmed = false;
        IsStartCommandConfirmed = false;
        OnPropertyChanged(nameof(ConversionPreview));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
