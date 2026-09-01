using RuinaoTesHardware;

namespace RuinaoSoftwareWpf;

/// <summary>复用共享DLL的严格等时分段计划，不在正式软件复制硬件波形算法。</summary>
public sealed class TiWaveformPreviewFactory : ITiWaveformPreviewFactory
{
    public AlternatingCurrentWaveformPreview Create(TiAlternatingCurrentParameters parameters)
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
