namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Text.Json;

public static class StimulationRecordParameters
{
    public const int CurrentSnapshotSchemaVersion = 1;

    public static string ToJson(PrescriptionDefinition prescription) =>
        JsonSerializer.Serialize(prescription);

    public static PrescriptionDefinition? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PrescriptionDefinition>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static StimulationRunStartRequest CreateRunStartRequest(
        TiGroup group,
        PrescriptionDefinition reusableParameters)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(reusableParameters);
        if (group.Channels.Count == 0)
        {
            throw new InvalidOperationException("启动刺激时至少需要一个通道。");
        }

        var channels = group.Channels
            .Select(channel =>
            {
                var snapshot = CreateChannelSnapshot(channel, reusableParameters);
                return new StimulationChannelStartRequest(
                    channel.Name,
                    snapshot.CurrentMilliamp,
                    snapshot.PlannedDurationSeconds,
                    snapshot.Polarity,
                    JsonSerializer.Serialize(snapshot),
                    snapshot.PlannedTotalCount);
            })
            .ToArray();
        return new StimulationRunStartRequest(
            group.Title,
            reusableParameters.StimulationType,
            reusableParameters.Name,
            channels);
    }

    public static PrescriptionDefinition? PrescriptionFromSnapshotJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ChannelParameterSnapshot>(json);
            return snapshot?.ReusableParameters ?? FromJson(json);
        }
        catch (JsonException)
        {
            return FromJson(json);
        }
    }

    public static PrescriptionDefinition CreateTiPrescription(TiGroup group, string prescriptionName)
    {
        return CreatePrescription(
            group,
            prescriptionName,
            StimulationModeCodes.TemporalInterference);
    }

    public static PrescriptionDefinition CreateDirectCurrentPrescription(TiGroup group, string prescriptionName)
    {
        return CreatePrescription(group, prescriptionName, StimulationModeCodes.DirectCurrent);
    }

    public static PrescriptionDefinition CreateTacsPrescription(TiGroup group, string prescriptionName)
    {
        ArgumentNullException.ThrowIfNull(group);
        var channel = group.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("启动tACS时至少需要一个通道。");
        var current = ParseDouble(channel.CurrentMA) ?? 0;
        var rampUp = ParseDouble(channel.RampUpS) ?? 0;
        var rampDown = ParseDouble(channel.RampDownS) ?? 0;
        var duration = ParseDouble(channel.DurationS) ?? 0;
        var frequency = ParseInt(channel.FrequencyHz) ?? 0;
        return new PrescriptionDefinition(
            $"REC_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(prescriptionName) ? group.Title : prescriptionName,
            "电刺激实验实际参数",
            StimulationModeCodes.AlternatingCurrent,
            current,
            PrescriptionDeliveryModes.Continuous,
            duration <= 0 ? 0 : Math.Max(1, (int)Math.Round(duration / 60d, MidpointRounding.AwayFromZero)),
            null,
            null,
            BuildCourse(group),
            (int)Math.Round(rampUp, MidpointRounding.AwayFromZero),
            (int)Math.Round(rampDown, MidpointRounding.AwayFromZero),
            "实际电刺激记录",
            false,
            TacsPeakCurrentMilliampereValue: current,
            TacsRampUpSecondsValue: rampUp,
            TacsRampDownSecondsValue: rampDown,
            TacsFrequencyHzValue: frequency,
            TacsTotalDurationSecondsValue: duration,
            TacsParameterVersion: 1);
    }

    public static PrescriptionDefinition CreateMonophasicPulseCurrentPrescription(
        TiGroup group,
        string prescriptionName)
    {
        return CreatePrescription(group, prescriptionName, StimulationModeCodes.MonophasicPulseCurrent);
    }

    public static PrescriptionDefinition CreatePulseCurrentPrescription(
        TiGroup group,
        string prescriptionName)
    {
        ArgumentNullException.ThrowIfNull(group);
        var channel = group.Channels.FirstOrDefault();
        var treatmentSeconds = ParseDouble(channel?.DurationS) ?? 0;
        var currentMilliamp = ParseDouble(channel?.CurrentMA) ?? 0;
        return new PrescriptionDefinition(
            $"REC_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(prescriptionName) ? group.Title : prescriptionName,
            "电刺激实验实际参数",
            StimulationModeCodes.PulseCurrent,
            currentMilliamp,
            PrescriptionDeliveryModes.Interval,
            treatmentSeconds <= 0 ? 0 : Math.Max(1, (int)Math.Round(treatmentSeconds / 60d, MidpointRounding.AwayFromZero)),
            null,
            null,
            BuildCourse(group),
            0,
            0,
            "实际电刺激记录",
            false,
            group.Channels.Select(item => string.Equals(item.Polarity, "调转", StringComparison.Ordinal) ? "调转" : "不掉转").ToArray(),
            PulseTreatmentDurationSeconds: (int)Math.Round(treatmentSeconds, MidpointRounding.AwayFromZero),
            PulseWidthMilliseconds: ParseInt(channel?.PulseWidthMilliseconds),
            PulseRiseWidthMilliseconds: ParseInt(channel?.PulseRiseWidthMilliseconds),
            PulseIntervalWidthMilliseconds: ParseInt(channel?.PulseIntervalWidthMilliseconds),
            PulseTreatmentDurationSecondsValue: treatmentSeconds);
    }

    private static PrescriptionDefinition CreatePrescription(
        TiGroup group,
        string prescriptionName,
        string stimulationType)
    {
        var channel = group.Channels.FirstOrDefault();
        var currentMilliamp = ParseDouble(channel?.CurrentMA) ?? 0;
        var totalDurationMinutes = SecondsToMinutes(channel?.DurationS);
        var intervalMinutes = SecondsToNullableMinutes(channel?.IntervalS);
        var rampUpSeconds = ParseInt(channel?.RampUpS) ?? 0;
        var rampDownSeconds = ParseInt(channel?.RampDownS) ?? 0;
        var isContinuous = string.Equals(channel?.StimulationMode, "连续", StringComparison.Ordinal)
            || string.Equals(channel?.StimulationMode, PrescriptionDeliveryModes.Continuous, StringComparison.Ordinal);
        var deliveryMode = isContinuous
            ? PrescriptionDeliveryModes.Continuous
            : PrescriptionDeliveryModes.Interval;

        return new PrescriptionDefinition(
            $"REC_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(prescriptionName) ? group.Title : prescriptionName,
            "电刺激实验实际参数",
            stimulationType,
            currentMilliamp,
            deliveryMode,
            totalDurationMinutes,
            isContinuous ? null : intervalMinutes,
            isContinuous ? null : totalDurationMinutes,
            BuildCourse(group),
            rampUpSeconds,
            rampDownSeconds,
            "实际电刺激记录",
            false,
            group.Channels.Select((item, index) =>
                    string.Equals(item.Polarity, "调转", StringComparison.Ordinal) ? "调转" : "不掉转")
                .ToArray());
    }

    private static ChannelParameterSnapshot CreateChannelSnapshot(
        ChannelConfig channel,
        PrescriptionDefinition reusableParameters)
    {
        var currentMilliamp = ParseDouble(channel.CurrentMA) ?? 0;
        var rampUpSeconds = ParseDouble(channel.RampUpS) ?? 0;
        var rampDownSeconds = ParseDouble(channel.RampDownS) ?? 0;
        var durationSeconds = ParseDouble(channel.DurationS) ?? 0;
        var intervalSeconds = ParseDouble(channel.IntervalS) ?? 0;
        var singleDurationSeconds = ParseDouble(channel.SingleDurationS) ?? 0;
        var frequencyHz = ParseDouble(channel.FrequencyHz);
        var polarity = string.Equals(channel.Polarity, "调转", StringComparison.Ordinal)
            ? "调转"
            : "不调转";
        var reusableChannelParameters = reusableParameters with
        {
            CurrentMilliamp = currentMilliamp,
            ChannelPolarities = null,
            DirectCurrentTotalDurationSecondsValue = durationSeconds,
            DirectCurrentIntervalSecondsValue = intervalSeconds,
            DirectCurrentSingleDurationSecondsValue = singleDurationSeconds,
            DirectCurrentRampUpSecondsValue = rampUpSeconds,
            DirectCurrentRampDownSecondsValue = rampDownSeconds,
            PulseTreatmentDurationSecondsValue = durationSeconds,
            PulseWidthMilliseconds = ParseInt(channel.PulseWidthMilliseconds),
            PulseRiseWidthMilliseconds = ParseInt(channel.PulseRiseWidthMilliseconds),
            PulseIntervalWidthMilliseconds = ParseInt(channel.PulseIntervalWidthMilliseconds),
            TacsPeakCurrentMilliampereValue = reusableParameters.IsTacs ? currentMilliamp : reusableParameters.TacsPeakCurrentMilliampereValue,
            TacsRampUpSecondsValue = reusableParameters.IsTacs ? rampUpSeconds : reusableParameters.TacsRampUpSecondsValue,
            TacsRampDownSecondsValue = reusableParameters.IsTacs ? rampDownSeconds : reusableParameters.TacsRampDownSecondsValue,
            TacsFrequencyHzValue = reusableParameters.IsTacs && frequencyHz.HasValue
                ? (int)Math.Round(frequencyHz.Value, MidpointRounding.AwayFromZero)
                : reusableParameters.TacsFrequencyHzValue,
            TacsTotalDurationSecondsValue = reusableParameters.IsTacs ? durationSeconds : reusableParameters.TacsTotalDurationSecondsValue,
            TacsParameterVersion = reusableParameters.IsTacs ? 1 : reusableParameters.TacsParameterVersion
        };

        var plannedPulseCount = long.TryParse(
            channel.PlannedPulseCount,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedCount)
            ? parsedCount
            : (long?)null;

        return new ChannelParameterSnapshot(
            CurrentSnapshotSchemaVersion,
            channel.Name,
            channel.Anode,
            channel.Cathode,
            currentMilliamp,
            rampUpSeconds,
            rampDownSeconds,
            durationSeconds,
            intervalSeconds,
            singleDurationSeconds,
            frequencyHz,
            polarity,
            channel.StimulationMode,
            reusableChannelParameters,
            ParseInt(channel.PulseWidthMilliseconds),
            ParseInt(channel.PulseRiseWidthMilliseconds),
            ParseInt(channel.PulseIntervalWidthMilliseconds),
            plannedPulseCount);
    }

    public static PrescriptionDefinition CreateFallbackRecord(
        long recordId,
        string groupTitle,
        string selectedChannelNames,
        string? stimulationType,
        string? prescriptionName)
    {
        return new PrescriptionDefinition(
            $"REC_{recordId}",
            string.IsNullOrWhiteSpace(prescriptionName) ? groupTitle : prescriptionName,
            "电刺激实验实际参数",
            string.IsNullOrWhiteSpace(stimulationType)
                ? StimulationModeCodes.TemporalInterference
                : stimulationType,
            0,
            PrescriptionDeliveryModes.Continuous,
            0,
            null,
            null,
            string.IsNullOrWhiteSpace(selectedChannelNames) ? groupTitle : selectedChannelNames,
            0,
            0,
            "旧记录未保存参数快照",
            false);
    }

    private static string BuildCourse(TiGroup group)
    {
        var channels = string.Join(" + ", group.Channels.Select(item => item.Name).Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(channels) ? group.Title : $"{group.Title}；{channels}";
    }

    private static int SecondsToMinutes(string? value)
    {
        var seconds = ParseDouble(value) ?? 0;
        return seconds <= 0 ? 0 : Math.Max(1, (int)Math.Round(seconds / 60, MidpointRounding.AwayFromZero));
    }

    private static int? SecondsToNullableMinutes(string? value)
    {
        var seconds = ParseDouble(value);
        if (seconds is null || seconds <= 0)
        {
            return null;
        }

        return Math.Max(1, (int)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero));
    }

    private static double? ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
