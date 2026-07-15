namespace RuinaoHardwareDebugWpf;

/// <summary>
/// EF Core 本地数据库实体。
/// 实体只保留业务查询需要的字段，时间统一使用 Unix 毫秒，便于和 EEG、血氧、电刺激等高频数据按时间窗口对齐。
/// </summary>
internal sealed class PatientEntity
{
    public long Id { get; set; }
    public long? OwnerUserId { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Gender { get; set; }
    public long? BirthDateUnixMs { get; set; }
    public string? IdCardEncrypted { get; set; }
    public string? PhoneEncrypted { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhoneEncrypted { get; set; }
    public string? HomeAddress { get; set; }
    public string? ClinicalInfo { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class AppStateEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class FeatureVisibilityEntity
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public long? UpdatedByUserId { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class PrescriptionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Indication { get; set; } = string.Empty;
    public string StimulationType { get; set; } = string.Empty;
    public double CurrentMilliamp { get; set; }
    public string DeliveryMode { get; set; } = string.Empty;
    public int TotalDurationMinutes { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? SessionDurationMinutes { get; set; }
    public string Course { get; set; } = string.Empty;
    public int RampUpSeconds { get; set; }
    public int RampDownSeconds { get; set; }
    public string EvidenceGrade { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class StimulationRecordEntity
{
    public long Id { get; set; }
    public long? OperatorUserId { get; set; }
    public string? PatientCode { get; set; }
    public string Action { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public string SelectedChannelNames { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StimulationType { get; set; }
    public string? PrescriptionName { get; set; }
    public string AdverseReactionRecord { get; set; } = string.Empty;
    public string? ParameterSnapshotJson { get; set; }
    public long EventTimeUnixMs { get; set; }
}

internal sealed class AssessmentSessionEntity
{
    public long Id { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string PatientCode { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public string UploadStatus { get; set; } = "local_only";
    public string? UploadBatchId { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class AssessmentModuleRecordEntity
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CameraName { get; set; }
    public string? OutputDir { get; set; }
    public string? RawVideoPath { get; set; }
    public string? NormalizedVideoPath { get; set; }
    public string? AudioPath { get; set; }
    public string? MergedVideoPath { get; set; }
    public string? FormPayloadJson { get; set; }
    public string? ResultSummary { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class AssessmentEventEntity
{
    public long Id { get; set; }
    public long? SessionId { get; set; }
    public long? ModuleRecordId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long EventTimeUnixMs { get; set; }
    public long? StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
    public AssessmentModuleRecordEntity? ModuleRecord { get; set; }
}

internal sealed class SensorSampleEntity
{
    public long Id { get; set; }
    public long? SessionId { get; set; }
    public long? ModuleRecordId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceName { get; set; }
    public long SampleTimeUnixMs { get; set; }
    public long? SequenceNo { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

internal sealed class SessionTimelineEventEntity
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public long SequenceNo { get; set; }
    public long EventTimeUnixMs { get; set; }
    public long SessionElapsedMs { get; set; }
    public long MonotonicTicks { get; set; }
    public long MonotonicFrequency { get; set; }
    public long? SourceTimeUnixMs { get; set; }
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
}

internal sealed class EegRecordingEntity
{
    public long Id { get; set; }
    public long ModuleRecordId { get; set; }
    public string RecordName { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public int ChannelCount { get; set; }
    public int SampleRateHz { get; set; }
    public int PageSeconds { get; set; }
    public int SegmentSeconds { get; set; }
    public string DataType { get; set; } = "float32";
    public string ChannelNamesJson { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long SampleCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

internal sealed class EegDataSegmentEntity
{
    public long Id { get; set; }
    public long EegRecordingId { get; set; }
    public int SegmentIndex { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long StartSampleIndex { get; set; }
    public long SampleCount { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long ByteLength { get; set; }
    public string Status { get; set; } = string.Empty;
}

internal sealed class EegMarkerEntity
{
    public long Id { get; set; }
    public long EegRecordingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public long EventTimeUnixMs { get; set; }
    public long ExperimentElapsedMs { get; set; }
    public long SampleIndex { get; set; }
    public int PageIndex { get; set; }
    public int PageSampleIndex { get; set; }
    public string Source { get; set; } = string.Empty;
}
