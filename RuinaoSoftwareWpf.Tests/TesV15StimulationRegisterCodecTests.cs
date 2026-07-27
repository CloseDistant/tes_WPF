namespace RuinaoSoftwareWpf.Tests;

using RuinaoTesProtocol.V15;
using Xunit;

public sealed class TesV15StimulationRegisterCodecTests
{
    [Fact]
    public void DirectCurrent_UsesOneTrapezoidForWholeRunTime()
    {
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrent(
            channelNumber: 3,
            totalTimeMs: 120_000,
            lowLevel: 10_000,
            highLevel: 50_000,
            risePermille: 200,
            holdPermille: 500,
            fallPermille: 300);

        var waveform = Assert.Single(configuration.Waveforms);
        var registers = TesV15StimulationRegisterCodec.BuildWaveformRegisters(configuration, 0);

        Assert.Equal(0x04U, configuration.EnableMask);
        Assert.Equal(0x15U, configuration.ConfigVersion);
        Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, waveform.Mode);
        Assert.Equal(120_000_000U, waveform.DurationUs);
        Assert.Equal(0x3420, registers[0].Address);
        Assert.Equal(8U, registers[0].Value);
        Assert.Equal(10_000U, registers[7].Value);
        Assert.Equal(50_000U, registers[8].Value);
        Assert.Equal(200U, registers[9].Value);
        Assert.Equal(500U, registers[10].Value);
        Assert.Equal(300U, registers[11].Value);
    }

    [Fact]
    public void PulseCurrent_UsesTrapezoidWithZeroFallAndConstantInterval()
    {
        var configuration = TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            baselineLevel: 30_000,
            targetLevel: 36_000,
            riseDurationUs: 5_000,
            plateauDurationUs: 10_000,
            intervalDurationUs: 20_000);

        Assert.Equal(2, configuration.Waveforms.Count);
        var active = configuration.Waveforms[0];
        var interval = configuration.Waveforms[1];
        var activeRegisters = TesV15StimulationRegisterCodec.BuildWaveformRegisters(configuration, 0);
        var intervalRegisters = TesV15StimulationRegisterCodec.BuildWaveformRegisters(configuration, 1);

        Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, active.Mode);
        Assert.Equal(15_000U, active.DurationUs);
        Assert.Equal(120_000U, configuration.TotalTimeMs);
        Assert.Equal(8U, activeRegisters[0].Value);
        Assert.Equal(30_000U, activeRegisters[7].Value);
        Assert.Equal(36_000U, activeRegisters[8].Value);
        Assert.Equal(333U, activeRegisters[9].Value);
        Assert.Equal(667U, activeRegisters[10].Value);
        Assert.Equal(0U, activeRegisters[11].Value);
        Assert.Equal(TesV15StimulationMode.Constant, interval.Mode);
        Assert.Equal(20_000U, interval.DurationUs);
        Assert.Equal(1U, intervalRegisters[0].Value);
        Assert.Equal(30_000U, intervalRegisters[4].Value);
        Assert.Equal(1U, configuration.ChannelFlags & 1U);
        Assert.DoesNotContain(configuration.Waveforms, waveform => (uint)waveform.Mode == 10U);
    }

    [Fact]
    public void DirectCurrentInterval_UsesTrapezoidAndConstantBaseline()
    {
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            activeDurationUs: 10_000_000,
            intervalDurationUs: 5_000_000,
            lowLevel: 30_000,
            highLevel: 36_000,
            risePermille: 100,
            holdPermille: 800,
            fallPermille: 100);

        Assert.Collection(
            configuration.Waveforms,
            active => Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, active.Mode),
            interval =>
            {
                Assert.Equal(TesV15StimulationMode.Constant, interval.Mode);
                Assert.Equal(30_000U, interval.Offset);
            });
        Assert.Equal(1U, configuration.ChannelFlags & 1U);
        Assert.DoesNotContain(configuration.Waveforms, waveform => (uint)waveform.Mode == 10U);
    }

    [Fact]
    public void ControlRegisters_UseUsbTest2V15Addresses()
    {
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrent(
            8,
            1_000,
            10_000,
            50_000,
            200,
            500,
            300);

        var registers = TesV15StimulationRegisterCodec.BuildControlRegisters(configuration);

        Assert.Equal(0x80U, registers[0].Value);
        Assert.Equal(0x2E00, registers[0].Address);
        Assert.Equal(0x15U, registers[1].Value);
        Assert.Equal(0x2E01, registers[1].Address);
        Assert.Equal(0x3E00, registers[2].Address);
        Assert.Equal(7U, registers[2].Value);
        Assert.Equal(1U, registers[6].Value);
    }

    [Fact]
    public void DirectCurrent_RejectsTrapezoidPartsThatDoNotFillDuration()
    {
        Assert.Throws<ArgumentException>(() =>
            TesV15StimulationRegisterCodec.CreateDirectCurrent(
                1,
                1_000,
                10_000,
                50_000,
                200,
                500,
                200));
    }

    [Fact]
    public void EngineeringUnits_PulseCurrent_ConvertMilliampereAndMilliseconds()
    {
        var currentMicroampere =
            TesV15EngineeringUnitConverter.MilliampereToMicroampere(2.5M);
        var durationMicroseconds =
            TesV15EngineeringUnitConverter.MillisecondsToMicroseconds(5.25M, "正相宽度");

        Assert.Equal(2500U, currentMicroampere);
        Assert.Equal(5250U, durationMicroseconds);
    }

    [Fact]
    public void EngineeringUnits_DirectCurrent_UseExplicitDacCalibration()
    {
        var dac = TesV15EngineeringUnitConverter.DirectCurrentToDac(
            currentMilliampere: 2M,
            zeroCurrentDac: 30_000,
            dacCountsPerMilliampere: 3_000M,
            reversePolarity: false);
        var reversed = TesV15EngineeringUnitConverter.DirectCurrentToDac(
            currentMilliampere: 2M,
            zeroCurrentDac: 30_000,
            dacCountsPerMilliampere: 3_000M,
            reversePolarity: true);

        Assert.Equal((30_000U, 36_000U), dac);
        Assert.Equal((30_000U, 24_000U), reversed);
    }

    [Fact]
    public void EngineeringUnits_TrapezoidTimesBecomePermille()
    {
        var values = TesV15EngineeringUnitConverter.ToTrapezoidPermille(
            totalSeconds: 100M,
            rampUpSeconds: 20M,
            rampDownSeconds: 30M);

        Assert.Equal((200U, 500U, 300U), values);
    }

    [Fact]
    public void EngineeringUnits_DirectCurrentRejectsMissingCalibration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TesV15EngineeringUnitConverter.DirectCurrentToDac(
                currentMilliampere: 2M,
                zeroCurrentDac: 30_000,
                dacCountsPerMilliampere: 0,
                reversePolarity: false));
    }
}
