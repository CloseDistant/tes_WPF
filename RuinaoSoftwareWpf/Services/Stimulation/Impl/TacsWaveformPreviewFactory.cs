using RuinaoTesHardware;

namespace RuinaoSoftwareWpf;

/// <summary>tACS独立业务适配器；底层复用共享DLL的正弦计划算法。</summary>
public sealed class TacsWaveformPreviewFactory : ITacsWaveformPreviewFactory
{
    public AlternatingCurrentWaveformPreview Create(TacsParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var plan = AlternatingCurrentStimulationClient.CreatePlan(
            new AlternatingCurrentStimulationParameters(
                BoardAddress: 1,
                Channel: 1,
                parameters.PeakCurrentMilliampere,
                parameters.RampUpSeconds,
                parameters.RampDownSeconds,
                parameters.FrequencyHz,
                parameters.TotalDurationSeconds));

        return new AlternatingCurrentWaveformPreview(
            decimal.ToDouble(parameters.PeakCurrentMilliampere),
            parameters.FrequencyHz,
            decimal.ToDouble(parameters.TotalDurationSeconds),
            plan.Segments.Select(segment => new AlternatingCurrentWaveformSegment(
                segment.StartMicroseconds / 1_000_000d,
                segment.DurationMicroseconds / 1_000_000d,
                decimal.ToDouble(segment.PeakCurrentMilliampere))).ToArray());
    }
}
