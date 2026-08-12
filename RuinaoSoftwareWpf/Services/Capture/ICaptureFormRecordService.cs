namespace RuinaoSoftwareWpf;

/// <summary>
/// 保存不包含音视频的数字表型表单记录。
/// </summary>
public interface ICaptureFormRecordService
{
    Task<CaptureFormRecordInfo> SaveFormModuleRecordAsync(
        long assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default);
}
