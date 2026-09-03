namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 网新接口 1 返回的患者摘要。字段只保留当前匹配页面需要展示的内容。
/// </summary>
public sealed record ExternalFollowUpPatient(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("deptName")]
    string DepartmentName,
    [property: JsonPropertyName("batchNo")]
    string BatchNumber,
    [property: JsonPropertyName("patientId")]
    string PatientId,
    [property: JsonPropertyName("phone")]
    string Phone);

public sealed record ExternalFollowUpPatientPage(
    long PageNumber,
    long PageSize,
    long TotalPage,
    long Total,
    IReadOnlyList<ExternalFollowUpPatient> Items);

/// <summary>
/// 网新接口 2 返回的单条随访明细。接口返回的时间保留为文本，避免不同环境的日期格式影响查询结果；
/// ID、状态和数量字段允许服务端返回 null。
/// </summary>
public sealed record ExternalFollowUpDetail(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? Id,
    [property: JsonPropertyName("followUpId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? FollowUpId,
    [property: JsonPropertyName("settingId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? SettingId,
    [property: JsonPropertyName("settingName")] string? SettingName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("followUpStartTime")]
    [property: JsonConverter(typeof(FlexibleStringJsonConverter))]
    string? FollowUpStartTime,
    [property: JsonPropertyName("followUpEndTime")]
    [property: JsonConverter(typeof(FlexibleStringJsonConverter))]
    string? FollowUpEndTime,
    [property: JsonPropertyName("questionnaireStatus")]
    [property: JsonConverter(typeof(FlexibleNullableInt32JsonConverter))]
    int? QuestionnaireStatus,
    [property: JsonPropertyName("questionnaireStatusName")] string? QuestionnaireStatusName,
    [property: JsonPropertyName("questionnaireCompleteTime")]
    [property: JsonConverter(typeof(FlexibleStringJsonConverter))]
    string? QuestionnaireCompleteTime,
    [property: JsonPropertyName("flowStatus")]
    [property: JsonConverter(typeof(FlexibleNullableInt32JsonConverter))]
    int? FlowStatus,
    [property: JsonPropertyName("flowStatusName")] string? FlowStatusName,
    [property: JsonPropertyName("flowId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? FlowId,
    [property: JsonPropertyName("flowName")] string? FlowName,
    [property: JsonPropertyName("flowCompleteTime")]
    [property: JsonConverter(typeof(FlexibleStringJsonConverter))]
    string? FlowCompleteTime,
    [property: JsonPropertyName("pcFlowId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? PcFlowId,
    [property: JsonPropertyName("pcFlowName")] string? PcFlowName,
    [property: JsonPropertyName("pcFlowCompleteTime")]
    [property: JsonConverter(typeof(FlexibleStringJsonConverter))]
    string? PcFlowCompleteTime,
    [property: JsonPropertyName("assessmentRecordId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? AssessmentRecordId,
    [property: JsonPropertyName("pcAssessmentRecordId")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? PcAssessmentRecordId,
    [property: JsonPropertyName("scaleCount")]
    [property: JsonConverter(typeof(FlexibleNullableInt64JsonConverter))]
    long? ScaleCount);

/// <summary>
/// 兼容网新服务端将日期返回为字符串、数字时间戳或 null 的实际响应格式。
/// </summary>
public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"不支持将 JSON {reader.TokenType} 转换为文本。")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}

public sealed class FlexibleNullableInt64JsonConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new JsonException($"不支持将 JSON {reader.TokenType} 转换为可空整数。");
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}

public sealed class FlexibleNullableInt32JsonConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String
            && int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new JsonException($"不支持将 JSON {reader.TokenType} 转换为可空整数。");
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}
