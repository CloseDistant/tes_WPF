using RuinaoTesProtocol.V15;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 临时刺激配置。逻辑 CH1/CH2 映射到同一块业务板 0x01，
/// 分别使用板内刺激通道 1/2。
/// </summary>
public sealed record TemporaryBoardStimulationConfiguration(
    byte TargetAddress,
    TesV15StimulationConfiguration Configuration);

/// <summary>
/// 在硬件电流标定完成前，把主软件工程单位转换为 usbtest3 兼容的 V1.5 配置。
/// 固定原始值只属于临时产品适配策略，不应下沉到通用协议 DLL。
/// </summary>
public static class TemporaryStimulationConfigurationFactory
{
    public const uint DefaultLowLevel = 10000;
    public const uint DefaultHighLevel = 50000;
    public const byte TargetBoardAddress = 0x01;

    public static TemporaryBoardStimulationConfiguration CreateDirectCurrent(
        ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!DirectCurrentWaveformParameters.TryCreate(channel, out var parameters, out var error))
        {
            throw new ArgumentException(error, nameof(channel));
        }

        var logicalChannelNumber = ParseLogicalChannelNumber(channel.Name);
        var cycle = parameters!.IsContinuous
            ? (
                RiseSeconds: (decimal)parameters.RampUpSeconds,
                HoldSeconds: (decimal)(parameters.TotalDurationSeconds
                    - parameters.RampUpSeconds
                    - parameters.RampDownSeconds),
                FallSeconds: (decimal)parameters.RampDownSeconds,
                IntervalSeconds: 0M)
            : (
                RiseSeconds: (decimal)parameters.RampUpSeconds,
                HoldSeconds: (decimal)parameters.PlateauSeconds,
                FallSeconds: (decimal)parameters.RampDownSeconds,
                IntervalSeconds: (decimal)parameters.IntervalSeconds);
        var cyclePermille = TesV15EngineeringUnitConverter.ToTrapezoidCyclePermille(
            cycle.RiseSeconds,
            cycle.HoldSeconds,
            cycle.FallSeconds,
            cycle.IntervalSeconds);
        var totalTimeMs = TesV15EngineeringUnitConverter.SecondsToMilliseconds(
            (decimal)parameters.TotalDurationSeconds,
            "tDCS总刺激时间");
        var cycleDurationUs = TesV15EngineeringUnitConverter.SecondsToMicroseconds(
            cycle.RiseSeconds + cycle.HoldSeconds + cycle.FallSeconds + cycle.IntervalSeconds,
            "tDCS单周期时间");
        var (lowLevel, highLevel) = GetLevels(parameters.ReversePolarity);

        return new TemporaryBoardStimulationConfiguration(
            GetTargetAddress(logicalChannelNumber),
            TesV15StimulationRegisterCodec.CreateDirectCurrentCycle(
                checked((byte)logicalChannelNumber),
                totalTimeMs,
                cycleDurationUs,
                lowLevel,
                highLevel,
                cyclePermille.RisePermille,
                cyclePermille.HighHoldPermille,
                cyclePermille.FallPermille,
                cyclePermille.LowHoldPermille));
    }

    public static TemporaryBoardStimulationConfiguration CreatePulseCurrent(
        int logicalChannelNumber,
        PulseCurrentParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var totalTimeMs = TesV15EngineeringUnitConverter.SecondsToMilliseconds(
            parameters.TreatmentDurationSeconds,
            "tPCS治疗总时间");
        var riseDurationUs = TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
            (decimal)parameters.RiseWidthMilliseconds,
            "tPCS上升宽度");
        var plateauDurationUs = TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
            (decimal)parameters.PulseWidthMilliseconds,
            "tPCS脉冲平台宽度");
        var intervalDurationUs = TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(
            (decimal)parameters.IntervalWidthMilliseconds,
            "tPCS间隔宽度");
        var (lowLevel, highLevel) = GetLevels(
            string.Equals(parameters.Polarity, PulseCurrentPolarities.Reversed, StringComparison.Ordinal));

        return new TemporaryBoardStimulationConfiguration(
            GetTargetAddress(logicalChannelNumber),
            TesV15StimulationRegisterCodec.CreatePulseCurrent(
                checked((byte)logicalChannelNumber),
                totalTimeMs,
                lowLevel,
                highLevel,
                riseDurationUs,
                plateauDurationUs,
                intervalDurationUs));
    }

    public static byte GetTargetAddress(int logicalChannelNumber)
    {
        return logicalChannelNumber switch
        {
            1 or 2 => TargetBoardAddress,
            _ => throw new ArgumentOutOfRangeException(
                nameof(logicalChannelNumber),
                "临时刺激硬件只支持逻辑通道 CH1 和 CH2。"),
        };
    }

    public static int ParseLogicalChannelNumber(string channelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        var digits = new string(channelName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var channelNumber)
            ? channelNumber
            : throw new FormatException($"无法从“{channelName}”解析刺激通道编号。");
    }

    private static (uint LowLevel, uint HighLevel) GetLevels(bool reversePolarity)
    {
        return reversePolarity
            ? (DefaultHighLevel, DefaultLowLevel)
            : (DefaultLowLevel, DefaultHighLevel);
    }
}
