namespace RuinaoSoftwareWpf;

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
