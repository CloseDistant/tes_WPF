using RuinaoHardwareEngineer.Features.RawStimulation;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class UsbTest4RawStimulationLayoutTests
{
    [Fact]
    public void BuildControlRegisters_UsesUsbTest4OrderAndChannelBase()
    {
        var configuration = CreateConfiguration(channel: 2, waveformCount: 2);

        var registers = UsbTest4RawStimulationLayout.BuildControlRegisters(configuration);

        Assert.Equal(
            [0x2E00, 0x2E01, 0x3200, 0x3201, 0x3202, 0x3203, 0x3204, 0x3205],
            registers.Select(register => (int)register.Address));
        Assert.Equal(2U, registers[6].Value);
    }

    [Fact]
    public void BuildWaveformRegisters_LastWaveformOfChannelEight_EndsAt3fff()
    {
        var configuration = CreateConfiguration(channel: 8, waveformCount: 30);

        var registers = UsbTest4RawStimulationLayout.BuildWaveformRegisters(configuration, 29);

        Assert.Equal(16, registers.Count);
        Assert.Equal(0x3FF0, registers[0].Address);
        Assert.Equal(0x3FFF, registers[^1].Address);
    }

    [Fact]
    public void Validate_WhenEnableMaskExcludesChannel_Throws()
    {
        var configuration = CreateConfiguration(channel: 2, waveformCount: 1) with { EnableMask = 0x01 };

        var exception = Assert.Throws<ArgumentException>(
            () => UsbTest4RawStimulationLayout.Validate(configuration));

        Assert.Contains("使能掩码", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenMoreThanThirtyWaveforms_Throws()
    {
        var configuration = CreateConfiguration(channel: 1, waveformCount: 31);

        Assert.Throws<ArgumentException>(
            () => UsbTest4RawStimulationLayout.Validate(configuration));
    }

    private static UsbTest4RawStimulationConfiguration CreateConfiguration(int channel, int waveformCount)
    {
        var waveform = new UsbTest4RawWaveform(
            8,
            2_000_000,
            0,
            0,
            0,
            0,
            0,
            unchecked((uint)-12_000),
            12_000,
            2_000,
            2_500,
            500,
            2_000,
            0,
            1,
            0);
        return new UsbTest4RawStimulationConfiguration(
            0,
            1U << (channel - 1),
            0x16,
            channel,
            0,
            0,
            600_000,
            0,
            Enumerable.Repeat(waveform, waveformCount).ToArray());
    }
}
