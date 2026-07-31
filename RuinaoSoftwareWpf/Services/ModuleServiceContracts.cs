namespace RuinaoSoftwareWpf;

/// <summary>
/// 数据处理服务接口（占位）。
/// 后续可接入实时温度/阻抗数据解析、滤波、存储等。
/// </summary>
public interface IDataProcessingService
{
}

public enum PatientSex
{
    Male = 1,
    Female = 2
}

public static class PatientSexExtensions
{
    public static string ToDisplayText(this PatientSex sex) => sex == PatientSex.Female ? "女" : "男";

    public static string ToStorageCode(this PatientSex sex) => sex == PatientSex.Female ? "F" : "M";

    public static PatientSex FromStorageCode(string? value) => value?.Trim() switch
    {
        "F" or "female" or "Female" or "女" or "女性" => PatientSex.Female,
        _ => PatientSex.Male
    };
}

public sealed record PatientRecord(
    string PatientCode,
    string Name,
    PatientSex Sex,
    DateOnly BirthDate,
    int Age,
    string? IdCardNumber,
    string Phone,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? HomeAddress,
    string? ClinicalInfo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PatientSaveRequest(
    string? PatientCode,
    string Name,
    PatientSex? Sex,
    DateOnly? BirthDate,
    string? IdCardNumber,
    string? Phone,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? HomeAddress,
    string? ClinicalInfo);

public enum StimulationTreatmentStatus
{
    Running,
    Ended,
    Incomplete
}

public enum StimulationEndType
{
    NormalCompletion,
    ManualTermination,
    AbnormalTermination
}

public static class StimulationEndReasonCodes
{
    public const string DurationCompleted = "DURATION_COMPLETED";
    public const string ChannelStop = "CHANNEL_STOP";
    public const string EmergencyStop = "EMERGENCY_STOP";
    public const string SoftwareInterrupted = "SOFTWARE_INTERRUPTED";
    public const string DeviceDisconnected = "DEVICE_DISCONNECTED";
    public const string ImpedanceAbnormal = "IMPEDANCE_ABNORMAL";
    public const string CommunicationLost = "COMMUNICATION_LOST";
    public const string DeviceError = "DEVICE_ERROR";
}

public sealed record StimulationChannelStartRequest(
    string ChannelName,
    double CurrentMilliamp,
    double PlannedDurationSeconds,
    string Polarity,
    string ParameterSnapshotJson,
    long? PlannedTotalCount = null);

public sealed record StimulationRunStartRequest(
    string GroupTitle,
    string StimulationType,
    string? PrescriptionName,
    IReadOnlyList<StimulationChannelStartRequest> Channels);

public sealed record StimulationChannelEndItem(
    string ChannelName,
    long? CompletedCount = null);

public sealed record StimulationChannelsEndRequest(
    string StimulationType,
    IReadOnlyList<StimulationChannelEndItem> Channels,
    StimulationEndType EndType,
    string EndReasonCode,
    string? EndReasonDetail = null);

public sealed record StimulationChannelTreatmentRecord(
    long Id,
    string ChannelName,
    StimulationTreatmentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    StimulationEndType? EndType,
    string? EndReasonCode,
    string? EndReasonDetail,
    long? PlannedTotalCount,
    long? CompletedCount);

public sealed record StimulationTreatmentRecord(
    long Id,
    string RunId,
    string PatientDisplay,
    string StimulationType,
    DateOnly TreatmentDate,
    string PrescriptionName,
    string AdverseReactionRecord,
    PrescriptionDefinition ParameterRecord,
    IReadOnlyList<StimulationChannelTreatmentRecord> Channels);

/// <summary>
/// 患者服务接口。
/// </summary>
public interface IPatientService
{
    event EventHandler? CurrentPatientChanged;

    PatientRecord? CurrentPatient { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default);

    Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default);

    Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default);

    Task<PageResult<PatientRecord>> GetPatientsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);

    Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default);

    Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default);

}

public interface IStimulationRecordService
{
    Task<string> StartRunAsync(
        StimulationRunStartRequest request,
        CancellationToken cancellationToken = default);

    Task EndChannelsAsync(
        StimulationChannelsEndRequest request,
        CancellationToken cancellationToken = default);

    Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 配置服务接口（占位）。
/// 后续可接入 appsettings.json、设备参数配置、用户偏好设置等。
/// </summary>
public interface IConfigService
{
}

/// <summary>
/// 报告服务接口（占位）。
/// 后续可接入治疗报告生成、导出 PDF、打印等。
/// </summary>
public interface IReportService
{
}
