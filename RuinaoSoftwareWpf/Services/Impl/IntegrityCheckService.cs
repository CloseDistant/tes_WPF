namespace RuinaoSoftwareWpf;

internal sealed class IntegrityCheckService : IIntegrityCheckService
{
    private readonly IAuditTrailService auditTrail;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;

    public IntegrityCheckService(
        IAuditTrailService auditTrail,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.auditTrail = auditTrail;
        this.accountService = accountService;
        this.logger = logger;
    }

    public async Task<IntegrityCheckResult> CheckReleaseFilesAsync(
        IProgress<IntegrityCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var actor = accountService.CurrentUser
            ?? throw new UnauthorizedAccessException("请先登录后再执行校验");
        var releaseResult = await ApplicationHardeningGuard.VerifyDirectoryAsync(
            AppContext.BaseDirectory,
            progress,
            cancellationToken).ConfigureAwait(false);
        var result = new IntegrityCheckResult(
            IntegrityCheckKind.ReleaseFiles,
            releaseResult.IsValid,
            releaseResult.VerifiedFileCount,
            releaseResult.IsValid ? "软件发布文件完整性校验通过" : MapReleaseError(releaseResult.ErrorCode),
            DateTimeOffset.Now);
        var written = await auditTrail.TryAppendAsync(
            new AuditEventInput(
                AuditEventCategory.IntegrityCheck,
                "RELEASE_INTEGRITY_CHECK",
                AuditActor.From(actor),
                "ReleaseFiles",
                result.Kind.ToString(),
                result.IsValid ? AuditEventResult.Success : AuditEventResult.Failed,
                result.IsValid ? null : "RELEASE_CHECK_FAILED",
                result.Message),
            cancellationToken).ConfigureAwait(false);
        if (!written)
        {
            logger.Warning("发布文件校验结果未能写入安全审计");
        }
        return result;
    }

    private static string MapReleaseError(string errorCode)
    {
        return errorCode switch
        {
            "manifest-missing" => "未找到发布文件清单",
            "manifest-authentication-failed" => "发布文件清单认证失败",
            "file-missing" => "发布文件缺失",
            "file-size-mismatch" => "发布文件大小不一致",
            "file-hash-mismatch" => "发布文件内容校验失败",
            "file-set-mismatch" => "发布目录文件集合不一致",
            _ => $"发布文件校验失败（{errorCode}）"
        };
    }
}
