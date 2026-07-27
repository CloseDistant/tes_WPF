namespace RuinaoSoftwareWpf.Tests;

using RuinaoTesProtocol.V14;
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
        Assert.Equal(0U, registers[12].Value);
    }

    [Fact]
    public void PulseCurrent_UsesOneUsbTest3TrapezoidWithLowHoldInterval()
    {
        var configuration = TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            lowLevel: 10_000,
            highLevel: 50_000,
            riseDurationUs: 5_000,
            plateauDurationUs: 10_000,
            intervalDurationUs: 20_000);

        var active = Assert.Single(configuration.Waveforms);
        var activeRegisters = TesV15StimulationRegisterCodec.BuildWaveformRegisters(configuration, 0);

        Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, active.Mode);
        Assert.Equal(35_000U, active.DurationUs);
        Assert.Equal(120_000U, configuration.TotalTimeMs);
        Assert.Equal(8U, activeRegisters[0].Value);
        Assert.Equal(10_000U, activeRegisters[7].Value);
        Assert.Equal(50_000U, activeRegisters[8].Value);
        Assert.Equal(143U, activeRegisters[9].Value);
        Assert.Equal(286U, activeRegisters[10].Value);
        Assert.Equal(0U, activeRegisters[11].Value);
        Assert.Equal(571U, activeRegisters[12].Value);
        Assert.Equal(1U, configuration.ChannelFlags & 1U);
        Assert.Equal(1000U, activeRegisters.Skip(9).Take(4).Sum(register => register.Value));
        Assert.DoesNotContain(configuration.Waveforms, waveform => waveform.Mode == TesV15StimulationMode.Constant);
        Assert.DoesNotContain(configuration.Waveforms, waveform => (uint)waveform.Mode == 10U);
    }

    [Fact]
    public void DirectCurrentInterval_UsesUsbTest3LowHoldInSingleTrapezoid()
    {
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrentCycle(
            channelNumber: 1,
            totalTimeMs: 120_000,
            cycleDurationUs: 15_000_000,
            lowLevel: 30_000,
            highLevel: 36_000,
            risePermille: 67,
            highHoldPermille: 533,
            fallPermille: 67,
            lowHoldPermille: 333);

        var active = Assert.Single(configuration.Waveforms);
        var waveformRegisters = TesV15StimulationRegisterCodec.BuildWaveformRegisters(configuration, 0);
        var controlRegisters = TesV15StimulationRegisterCodec.BuildControlRegisters(configuration);
        Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, active.Mode);
        Assert.Equal(15_000_000U, active.DurationUs);
        Assert.Equal(new TesV14RegisterValue(0x3029, 67), waveformRegisters[9]);
        Assert.Equal(new TesV14RegisterValue(0x302A, 533), waveformRegisters[10]);
        Assert.Equal(new TesV14RegisterValue(0x302B, 67), waveformRegisters[11]);
        Assert.Equal(new TesV14RegisterValue(0x302C, 333), waveformRegisters[12]);
        Assert.Equal(new TesV14RegisterValue(0x3004, 1), controlRegisters[6]);
        Assert.Equal(1U, configuration.ChannelFlags & 1U);
        Assert.Equal(new TesV14RegisterValue(0x3005, 1), controlRegisters[7]);
        Assert.DoesNotContain(configuration.Waveforms, waveform => waveform.Mode == TesV15StimulationMode.Constant);
        Assert.DoesNotContain(configuration.Waveforms, waveform => (uint)waveform.Mode == 10U);
    }

    [Fact]
    public void ControlRegisters_UseUsbTest3V15Addresses()
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
    public void EngineeringUnits_UsbTest3FourEqualPhasesBecomeFourEqualPermille()
    {
        var values = TesV15EngineeringUnitConverter.ToTrapezoidCyclePermille(
            rampUpDuration: 0.5M,
            highHoldDuration: 0.5M,
            rampDownDuration: 0.5M,
            lowHoldDuration: 0.5M);

        Assert.Equal((250U, 250U, 250U, 250U), values);
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

    [Fact]
    public void RecommendedRange_StillRejectsCurrentAndDacAboveProductLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TesV15EngineeringUnitConverter.DirectCurrentToDac(
                currentMilliampere: 16M,
                zeroCurrentDac: 30_000,
                dacCountsPerMilliampere: 3_000M,
                reversePolarity: false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TesV15StimulationRegisterCodec.CreateDirectCurrent(
                channelNumber: 1,
                totalTimeMs: 1_000,
                lowLevel: 60_001,
                highLevel: 70_000,
                risePermille: 100,
                holdPermille: 800,
                fallPermille: 100));
    }

    [Fact]
    public void ProtocolRange_AllowsCurrentAndDacAboveProductLimits()
    {
        var dac = TesV15EngineeringUnitConverter.DirectCurrentToDac(
            currentMilliampere: 20M,
            zeroCurrentDac: 70_000,
            dacCountsPerMilliampere: 3_000M,
            reversePolarity: false,
            validationMode: TesV15ParameterValidationMode.ProtocolRange);
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrent(
            channelNumber: 1,
            totalTimeMs: 1_000,
            lowLevel: dac.BaselineDac,
            highLevel: dac.TargetDac,
            risePermille: 100,
            holdPermille: 800,
            fallPermille: 100,
            validationMode: TesV15ParameterValidationMode.ProtocolRange);

        Assert.Equal((70_000U, 130_000U), dac);
        Assert.Equal(70_000U, configuration.Waveforms[0].LowLevelOrPositiveValue);
        Assert.Equal(130_000U, configuration.Waveforms[0].HighLevelOrNegativeValue);
    }
}
