namespace RuinaoHardwareEngineer.Features.RawStimulation;

public enum UsbTest4StimulusValueMode
{
    DirectDa,
    Current,
}

/// <summary>
/// 复刻 usbtest4 V1.6 工程界面的 DA/电流显示换算。
/// 最大正电流目前只是换算标定值；在 0x2E04 的协议编码未确认前，不将其写入硬件。
/// </summary>
public static class UsbTest4StimulusValueConverter
{
    public const decimal DefaultMaxCurrentMilliampere = 15.000M;
    public const decimal MinimumMaxCurrentMilliampere = 10.000M;
    public const decimal MaximumMaxCurrentMilliampere = 20.000M;

    public static decimal ValidateMaxCurrentMilliampere(decimal value)
    {
        if (value is < MinimumMaxCurrentMilliampere or > MaximumMaxCurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"最大正电流必须在{MinimumMaxCurrentMilliampere:0.###}到{MaximumMaxCurrentMilliampere:0.###}mA之间。");
        }

        return value;
    }

    public static uint CurrentAmplitudeToRegister(decimal currentMilliampere, decimal maxCurrentMilliampere)
    {
        maxCurrentMilliampere = ValidateMaxCurrentMilliampere(maxCurrentMilliampere);
        var current = Math.Min(Math.Abs(currentMilliampere), maxCurrentMilliampere);
        var da = Math.Round(current / maxCurrentMilliampere * short.MaxValue, MidpointRounding.AwayFromZero);
        return (uint)da;
    }

    public static uint CurrentToRegister(decimal currentMilliampere, decimal maxCurrentMilliampere)
    {
        maxCurrentMilliampere = ValidateMaxCurrentMilliampere(maxCurrentMilliampere);
        var current = Math.Clamp(currentMilliampere, -maxCurrentMilliampere, maxCurrentMilliampere);
        var da = Math.Round(current / maxCurrentMilliampere * short.MaxValue, MidpointRounding.AwayFromZero);
        da = Math.Clamp(da, short.MinValue, short.MaxValue);
        return unchecked((uint)(int)da);
    }

    public static decimal RegisterAmplitudeToCurrent(uint registerValue, decimal maxCurrentMilliampere)
    {
        maxCurrentMilliampere = ValidateMaxCurrentMilliampere(maxCurrentMilliampere);
        var da = Math.Min(registerValue, (uint)short.MaxValue);
        return Math.Round(da / (decimal)short.MaxValue * maxCurrentMilliampere, 3, MidpointRounding.AwayFromZero);
    }

    public static decimal RegisterToCurrent(uint registerValue, decimal maxCurrentMilliampere)
    {
        maxCurrentMilliampere = ValidateMaxCurrentMilliampere(maxCurrentMilliampere);
        var da = DecodeSigned(registerValue);
        return Math.Round(da / (decimal)short.MaxValue * maxCurrentMilliampere, 3, MidpointRounding.AwayFromZero);
    }

    public static int DecodeSigned(uint registerValue) =>
        registerValue <= ushort.MaxValue
            ? (short)registerValue
            : unchecked((int)registerValue);

    public static bool UsesSignedLevelCurrent(uint waveformType) =>
        waveformType is 6 or 7 or 8 or 9;

    public static bool UsesAmplitudeLevelCurrent(uint waveformType) =>
        waveformType == 10;
}
