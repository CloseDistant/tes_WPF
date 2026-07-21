namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class SoftwareActivationService : ISoftwareActivationService
{
    private const string ActivationStateKey = "software_activation_credential_v1";
    private const string ProductCode = "RUINAO-TDCS-CONTROL";
    private const int CredentialSchemaVersion = 1;
    private const string SoftwareVersion = "1.0.0";

    private static readonly byte[] CredentialEntropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("RuinaoSoftwareWpf.SoftwareActivationCredential.v1"));

    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly IAuditTrailService auditTrail;
    private readonly ILoggingService logger;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private int initialized;
    private int isActivated;

    public SoftwareActivationService(
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        IAuditTrailService auditTrail,
        ILoggingService logger,
        TimeProvider timeProvider)
    {
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.auditTrail = auditTrail;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    public bool IsActivated => Volatile.Read(ref isActivated) == 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref initialized) == 1)
        {
            return;
        }

        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref initialized) == 1)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var storedCredential = await context.AppStates
                .AsNoTracking()
                .Where(item => item.Key == ActivationStateKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken);

            var activated = TryValidateCredential(storedCredential);
            Volatile.Write(ref isActivated, activated ? 1 : 0);
            Volatile.Write(ref initialized, 1);
            logger.Info(activated ? "软件激活凭据校验通过" : "未检测到有效的软件激活凭据");
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task<SoftwareActivationResult> ActivateAsync(
        string activationCode,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (IsActivated)
        {
            return new SoftwareActivationResult(true, "软件已激活");
        }

        if (string.IsNullOrWhiteSpace(activationCode))
        {
            return new SoftwareActivationResult(false, "请输入激活码");
        }

        if (!SoftwareActivationCodeVerifier.Verify(activationCode))
        {
            await TryWriteAuditAsync(
                AuditEventResult.Failed,
                "ACTIVATION_CODE_INVALID",
                "激活码验证未通过",
                cancellationToken);
            return new SoftwareActivationResult(false, "激活码无效，请核对后重试");
        }

        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (IsActivated)
            {
                return new SoftwareActivationResult(true, "软件已激活");
            }

            var credential = new ActivationCredential(
                CredentialSchemaVersion,
                ProductCode,
                SoftwareVersion,
                timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));
            var protectedCredential = ProtectCredential(credential);

            await databaseWriteCoordinator.ExecuteAsync(
                AppDatabasePathProvider.MainDatabasePath,
                async () =>
                {
                    await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
                    var state = await context.AppStates.FirstOrDefaultAsync(
                        item => item.Key == ActivationStateKey,
                        cancellationToken);
                    if (state is null)
                    {
                        state = new AppStateEntity { Key = ActivationStateKey };
                        context.AppStates.Add(state);
                    }

                    state.Value = protectedCredential;
                    state.UpdatedAtUnixMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                    await context.SaveChangesAsync(cancellationToken);
                },
                cancellationToken);

            Volatile.Write(ref isActivated, 1);
            await TryWriteAuditAsync(
                AuditEventResult.Success,
                null,
                $"软件版本 {SoftwareVersion} 激活成功",
                cancellationToken);
            logger.Info("软件激活成功，激活凭据已保存");
            return new SoftwareActivationResult(true, "激活成功");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("软件激活凭据保存失败", exception);
            await TryWriteAuditAsync(
                AuditEventResult.Failed,
                "ACTIVATION_CREDENTIAL_SAVE_FAILED",
                "激活凭据保存失败",
                cancellationToken);
            return new SoftwareActivationResult(false, "激活信息保存失败，请重试");
        }
        finally
        {
            stateGate.Release();
        }
    }

    private static string ProtectCredential(ActivationCredential credential)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(credential);
        var protectedPayload = ProtectedData.Protect(
            payload,
            CredentialEntropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedPayload);
    }

    private bool TryValidateCredential(string? storedCredential)
    {
        if (string.IsNullOrWhiteSpace(storedCredential))
        {
            return false;
        }

        try
        {
            var protectedPayload = Convert.FromBase64String(storedCredential);
            var payload = ProtectedData.Unprotect(
                protectedPayload,
                CredentialEntropy,
                DataProtectionScope.CurrentUser);
            var credential = JsonSerializer.Deserialize<ActivationCredential>(payload);
            return credential is not null
                && credential.SchemaVersion == CredentialSchemaVersion
                && string.Equals(credential.ProductCode, ProductCode, StringComparison.Ordinal)
                && credential.ActivatedAtUnixMs > 0
                && !string.IsNullOrWhiteSpace(credential.Nonce);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            logger.Warning($"软件激活凭据无效：{exception.GetType().Name}");
            return false;
        }
    }

    private async Task TryWriteAuditAsync(
        AuditEventResult result,
        string? failureCode,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditTrail.AppendAsync(
                new AuditEventInput(
                    AuditEventCategory.AccountAuthorization,
                    "SOFTWARE_ACTIVATION",
                    AuditActor.System,
                    "Software",
                    ProductCode,
                    result,
                    failureCode,
                    reason),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error("软件激活安全审计写入失败", exception);
        }
    }

    private sealed record ActivationCredential(
        int SchemaVersion,
        string ProductCode,
        string SoftwareVersion,
        long ActivatedAtUnixMs,
        string Nonce);
}
