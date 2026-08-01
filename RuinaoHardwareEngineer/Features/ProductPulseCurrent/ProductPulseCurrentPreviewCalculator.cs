using RuinaoTesHardware;

namespace RuinaoHardwareEngineer.Features.ProductPulseCurrent;

/// <summary>
/// 工程师界面使用的软件模拟结果。这里只验证产品参数和展示两段候选映射，
/// 不代表硬件已经支持该配置，也不能用于发送刺激命令。
/// </summary>
public sealed record ProductPulseCurrentPreview(
    decimal SignedCurrentMilliampere,
    int SignedDa,
    uint RampDurationMicroseconds,
    uint PulseDurationMicroseconds,
    uint IntervalDurationMicroseconds,
    uint PulseCycleMicroseconds,
    uint TreatmentDurationMilliseconds,
    uint TotalPulseCount);

public static class ProductPulseCurrentPreviewCalculator
{
    public static ProductPulseCurrentPreview Calculate(
        decimal currentMilliampere,
        decimal rampWidthMilliseconds,
        decimal pulseWidthMilliseconds,
        decimal intervalWidthMilliseconds,
        decimal treatmentDurationSeconds,
        bool reversed)
    {
        if (rampWidthMilliseconds < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rampWidthMilliseconds),
                "上升宽度不能小于0ms。");
        }

        if (pulseWidthMilliseconds <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pulseWidthMilliseconds),
                "脉冲宽度必须大于0ms。");
        }

        if (intervalWidthMilliseconds < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalWidthMilliseconds),
                "间隔宽度不能小于0ms。");
        }

        if (treatmentDurationSeconds <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(treatmentDurationSeconds),
                "治疗时间必须大于0s。");
        }

        var treatmentMilliseconds = treatmentDurationSeconds * 1_000m;
        if (treatmentMilliseconds < rampWidthMilliseconds + pulseWidthMilliseconds)
        {
            throw new ArgumentException(
                "治疗时间不足以容纳第一次完整脉冲：至少需要上升宽度加脉冲宽度。");
        }

        var totalCount = decimal.Floor(
            (treatmentMilliseconds - rampWidthMilliseconds + intervalWidthMilliseconds)
            / (pulseWidthMilliseconds + intervalWidthMilliseconds));
        if (totalCount < 1m || totalCount > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(treatmentDurationSeconds),
                "按当前时间参数计算出的脉冲总次数超出支持范围。");
        }

        var da = DirectCurrentStimulationClient.ConvertCurrentToDa(currentMilliampere);
        var signedDa = reversed ? -da : da;
        return new ProductPulseCurrentPreview(
            reversed ? -currentMilliampere : currentMilliampere,
            signedDa,
            ToUInt32Microseconds(rampWidthMilliseconds, "上升宽度"),
            ToUInt32Microseconds(pulseWidthMilliseconds, "脉冲宽度"),
            ToUInt32Microseconds(intervalWidthMilliseconds, "间隔宽度"),
            ToUInt32Microseconds(
                pulseWidthMilliseconds + intervalWidthMilliseconds,
                "脉冲周期"),
            ToUInt32(treatmentMilliseconds, "治疗时间"),
            decimal.ToUInt32(totalCount));
    }

    private static uint ToUInt32Microseconds(decimal milliseconds, string name) =>
        ToUInt32(milliseconds * 1_000m, name);

    private static uint ToUInt32(decimal value, string name)
    {
        var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        if (rounded is < uint.MinValue or > uint.MaxValue)
        {
            throw new OverflowException($"{name}换算后的硬件整数超出UInt32范围。");
        }

        return decimal.ToUInt32(rounded);
    }
}
