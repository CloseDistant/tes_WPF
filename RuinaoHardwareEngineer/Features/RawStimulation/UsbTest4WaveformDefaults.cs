namespace RuinaoHardwareEngineer.Features.RawStimulation;

/// <summary>
/// usbtest4 V1.6 中 11 种波形的原始初始值。
/// 选择新的波形类型时，应使用这里的整组默认值覆盖当前行。
/// </summary>
public static class UsbTest4WaveformDefaults
{
    public static UsbTest4RawWaveform Create(uint waveformType) =>
        waveformType switch
        {
            1 => Waveform(1, 1_000_000, offset: 20_000),
            2 => Waveform(2, 1_000_000, frequency: 20, amplitude: 10_000, dutyOrder: 500, sampleCount: 1_024),
            3 => Waveform(3, 1_000_000, frequency: 20, amplitude: 10_000, dutyOrder: 500, sampleCount: 1_024),
            4 => Waveform(4, 1_000_000, frequency: 10, amplitude: 12_000, dutyOrder: 500, sampleCount: 1_024),
            5 => Waveform(5, 1_000_000, frequency: 10, amplitude: 12_000, dutyOrder: 500, sampleCount: 1_024),
            6 => Waveform(6, 500_000, lowPositive: -10_000, highNegative: 10_000),
            7 => Waveform(7, 500_000, lowPositive: -10_000, highNegative: 10_000),
            8 => Waveform(
                8,
                2_000_000,
                lowPositive: -12_000,
                highNegative: 12_000,
                risePositive: 2_000,
                holdInterval: 2_500,
                fallNegative: 500,
                customPeriod: 2_000),
            9 => Waveform(
                9,
                1_000_000,
                amplitude: 8_000,
                lowPositive: -8_000,
                highNegative: 8_000,
                customPeriod: 12_345,
                sampleCount: 1_024,
                flags: 1),
            10 => Waveform(
                10,
                200_000,
                dutyOrder: 1,
                lowPositive: 12_000,
                highNegative: -12_000,
                risePositive: 5_000,
                holdInterval: 2_000,
                fallNegative: 5_000,
                customPeriod: 8_000,
                sampleCount: 1_024,
                repeatCount: 10),
            11 => Waveform(11, 1_000_000, customPeriod: 1),
            _ => Create(2),
        };

    private static UsbTest4RawWaveform Waveform(
        uint waveformType,
        uint durationUs,
        uint frequency = 0,
        uint amplitude = 0,
        int offset = 0,
        uint phase = 0,
        uint dutyOrder = 0,
        int lowPositive = 0,
        int highNegative = 0,
        uint risePositive = 0,
        uint holdInterval = 0,
        uint fallNegative = 0,
        uint customPeriod = 0,
        uint sampleCount = 0,
        uint repeatCount = 1,
        uint flags = 0) =>
        new(
            waveformType,
            durationUs,
            frequency,
            amplitude,
            unchecked((uint)offset),
            phase,
            dutyOrder,
            unchecked((uint)lowPositive),
            unchecked((uint)highNegative),
            risePositive,
            holdInterval,
            fallNegative,
            customPeriod,
            sampleCount,
            repeatCount,
            flags);
}
