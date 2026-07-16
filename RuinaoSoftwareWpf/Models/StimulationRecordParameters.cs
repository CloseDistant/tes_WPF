namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Text.Json;

public static class StimulationRecordParameters
{
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

    public static PrescriptionDefinition CreateTiPrescription(TiGroup group, string prescriptionName)
    {
        return CreatePrescription(group, prescriptionName, "TI");
    }

    public static PrescriptionDefinition CreateDirectCurrentPrescription(TiGroup group, string prescriptionName)
    {
        return CreatePrescription(group, prescriptionName, "tDCS");
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
            string.IsNullOrWhiteSpace(stimulationType) ? "TI" : stimulationType,
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
