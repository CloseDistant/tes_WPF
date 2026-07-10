namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 采集数据库实体构造工厂。
/// 把时间戳填充和默认字段集中在这里，仓储入口只关注流程。
/// </summary>
internal static class CaptureRecordEntityFactory
{
    public static AssessmentModuleRecordEntity CreateTaskModuleRecord(
        long sessionId,
        string moduleCode,
        string moduleName,
        string recordType,
        string cameraName,
        string outputDirectory,
        string rawVideoPath,
        string normalizedVideoPath,
        string audioPath,
        string mergedVideoPath,
        DateTimeOffset now)
    {
        var nowUnixMs = now.ToUnixTimeMilliseconds();
        return new AssessmentModuleRecordEntity
        {
            SessionId = sessionId,
            ModuleCode = moduleCode,
            ModuleName = moduleName,
            RecordType = recordType,
            Status = "recording",
            CameraName = cameraName,
            OutputDir = outputDirectory,
            RawVideoPath = rawVideoPath,
            NormalizedVideoPath = normalizedVideoPath,
            AudioPath = audioPath,
            MergedVideoPath = mergedVideoPath,
            StartedAtUnixMs = nowUnixMs,
            CreatedAtUnixMs = nowUnixMs,
            UpdatedAtUnixMs = nowUnixMs
        };
    }

    public static AssessmentModuleRecordEntity CreateFormModuleRecord(
        long sessionId,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status,
        DateTimeOffset now)
    {
        var nowUnixMs = now.ToUnixTimeMilliseconds();
        return new AssessmentModuleRecordEntity
        {
            SessionId = sessionId,
            ModuleCode = moduleCode,
            ModuleName = moduleName,
            RecordType = CaptureModuleTypes.Form,
            Status = status,
            FormPayloadJson = formPayloadJson,
            ResultSummary = status,
            StartedAtUnixMs = nowUnixMs,
            EndedAtUnixMs = nowUnixMs,
            CreatedAtUnixMs = nowUnixMs,
            UpdatedAtUnixMs = nowUnixMs
        };
    }

    public static AssessmentEventEntity CreateModuleEvent(
        long sessionId,
        long moduleRecordId,
        string eventType,
        string? message,
        string? payloadJson,
        DateTimeOffset now,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null)
    {
        return new AssessmentEventEntity
        {
            SessionId = sessionId,
            ModuleRecordId = moduleRecordId,
            EventType = eventType,
            EventTimeUnixMs = now.ToUnixTimeMilliseconds(),
            StartedAtUnixMs = startedAt?.ToUnixTimeMilliseconds(),
            EndedAtUnixMs = endedAt?.ToUnixTimeMilliseconds(),
            Message = message ?? string.Empty,
            PayloadJson = payloadJson ?? string.Empty
        };
    }
}
