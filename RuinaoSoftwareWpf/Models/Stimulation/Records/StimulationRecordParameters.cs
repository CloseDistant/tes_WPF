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
                    JsonSerializer.Serialize(snapshot));
            })
            .ToArray();
        return new StimulationRunStartRequest(
            group.Title,
            reusableParameters.StimulationType,
            reusableParameters.Name,
            channels);
    }

    public static StimulationRunStartRequest CreatePulseRunStartRequest(
        IReadOnlyDictionary<PulseCurrentChannelConfig, PulseCurrentParameters> channels,
        string prescriptionName,
        string groupTitle)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
        {
            throw new InvalidOperationException("启动刺激时至少需要一个通道。");
        }

        var channelRequests = channels.Select(pair =>
        {
            var channel = pair.Key;
            var parameters = pair.Value;
            var reusableParameters = new PrescriptionDefinition(
                $"REC_{Guid.NewGuid():N}",
                string.IsNullOrWhiteSpace(prescriptionName) ? "手动设置" : prescriptionName,
                "电刺激实际参数",
                PrescriptionDefinition.PulseCurrentStimulationType,
                parameters.CurrentMilliamp,
                PrescriptionDeliveryModes.Interval,
                Math.Max(1, (int)Math.Round(parameters.TreatmentDurationSeconds / 60d, MidpointRounding.AwayFromZero)),
                null,
                null,
                channel.Name,
                0,
                0,
                "实际电刺激记录",
                false,
                PulseTreatmentDurationSeconds: (int)Math.Round(parameters.TreatmentDurationSeconds, MidpointRounding.AwayFromZero),
                PulseWidthMilliseconds: parameters.PulseWidthMilliseconds,
                PulseRiseWidthMilliseconds: parameters.RiseWidthMilliseconds,
                PulseIntervalWidthMilliseconds: parameters.IntervalWidthMilliseconds,
                PulseTreatmentDurationSecondsValue: parameters.TreatmentDurationSeconds);
            var snapshot = new ChannelParameterSnapshot(
                CurrentSnapshotSchemaVersion,
                channel.Name,
                string.Empty,
                string.Empty,
                parameters.CurrentMilliamp,
                parameters.RiseWidthMilliseconds / 1000d,
                0,
                parameters.TreatmentDurationSeconds,
                parameters.IntervalWidthMilliseconds / 1000d,
                parameters.PulseWidthMilliseconds / 1000d,
                null,
                parameters.Polarity,
                PrescriptionDeliveryModes.Interval,
                reusableParameters,
                parameters.PulseWidthMilliseconds,
                parameters.RiseWidthMilliseconds,
                parameters.IntervalWidthMilliseconds,
                parameters.PlannedTotalCount);
            return new StimulationChannelStartRequest(
                channel.Name,
                parameters.CurrentMilliamp,
                parameters.TreatmentDurationSeconds,
                parameters.Polarity,
                JsonSerializer.Serialize(snapshot),
                parameters.PlannedTotalCount);
        }).ToArray();

        return new StimulationRunStartRequest(
            string.IsNullOrWhiteSpace(groupTitle) ? "tPCS" : groupTitle,
            PrescriptionDefinition.PulseCurrentStimulationType,
            string.IsNullOrWhiteSpace(prescriptionName) ? "手动设置" : prescriptionName,
            channelRequests);
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

    public static PrescriptionDefinition CreateMonophasicPulseCurrentPrescription(
        TiGroup group,
        string prescriptionName)
    {
        return CreatePrescription(group, prescriptionName, StimulationModeCodes.MonophasicPulseCurrent);
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
            DirectCurrentRampDownSecondsValue = rampDownSeconds
        };

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
            reusableChannelParameters);
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
