namespace RuinaoSoftwareWpf;

internal sealed class IntegrityCheckService : IIntegrityCheckService
{
    private readonly IAuditTrailService auditTrail;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;
    private readonly IReleaseIntegrityVerifier releaseIntegrityVerifier;
    private readonly IReleaseIntegrityStateStore stateStore;
    private readonly TimeProvider timeProvider;

    public IntegrityCheckService(
        IAuditTrailService auditTrail,
        IAccountService accountService,
        ILoggingService logger,
        IReleaseIntegrityVerifier releaseIntegrityVerifier,
        IReleaseIntegrityStateStore stateStore,
        TimeProvider timeProvider)
    {
        this.auditTrail = auditTrail;
        this.accountService = accountService;
        this.logger = logger;
        this.releaseIntegrityVerifier = releaseIntegrityVerifier;
        this.stateStore = stateStore;
        this.timeProvider = timeProvider;
    }

    public async Task<ReleaseIntegrityStatus> GetReleaseStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new ReleaseIntegrityStatus(ReleaseIntegrityStatusKind.NeverChecked, null);
        }

        var manifestIdentity = await releaseIntegrityVerifier
            .GetManifestIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = snapshot.ToResult();
        if (!string.Equals(
                snapshot.ManifestIdentity,
                manifestIdentity,
                StringComparison.Ordinal))
        {
            return new ReleaseIntegrityStatus(ReleaseIntegrityStatusKind.ReleaseChanged, result);
        }

        return new ReleaseIntegrityStatus(
            snapshot.IsValid
                ? ReleaseIntegrityStatusKind.Passed
                : ReleaseIntegrityStatusKind.Failed,
            result);
    }

    public async Task<IntegrityCheckResult> CheckReleaseFilesAsync(
        IProgress<IntegrityCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var actor = accountService.CurrentUser
            ?? throw new UnauthorizedAccessException("请先登录后再执行校验");
        var releaseResult = await releaseIntegrityVerifier
            .VerifyAsync(progress, cancellationToken)
            .ConfigureAwait(false);
        var result = new IntegrityCheckResult(
            IntegrityCheckKind.ReleaseFiles,
            releaseResult.IsValid,
            releaseResult.VerifiedFileCount,
            releaseResult.IsValid ? "软件发布文件完整性校验通过" : MapReleaseError(releaseResult.ErrorCode),
            timeProvider.GetLocalNow());
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

        var manifestIdentity = await releaseIntegrityVerifier
            .GetManifestIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        await stateStore.SaveAsync(
                new ReleaseIntegritySnapshot(
                    result.IsValid,
                    result.VerifiedCount,
                    result.Message,
                    result.CompletedAt,
                    manifestIdentity),
                cancellationToken)
            .ConfigureAwait(false);
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
