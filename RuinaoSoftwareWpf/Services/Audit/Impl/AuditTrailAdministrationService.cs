namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal sealed class AuditTrailAdministrationService : IAuditTrailAdministrationService
{
    private readonly IAuditTrailStore store;
    private readonly IAccountService accountService;
    private readonly TimeProvider timeProvider;

    public AuditTrailAdministrationService(
        IAuditTrailStore store,
        IAccountService accountService,
        TimeProvider timeProvider)
    {
        this.store = store;
        this.accountService = accountService;
        this.timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<string>> GetActorLoginNamesAsync(
        CancellationToken cancellationToken = default)
    {
        var currentUser = RequireSignedInUser();
        if (currentUser.RoleId != AccountRoles.Admin)
        {
            return new[] { currentUser.LoginName };
        }

        var actors = (await accountService.GetActiveLoginNamesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var historicalActors = await store.GetActorLoginNamesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var historicalActor in historicalActors)
        {
            if (!actors.Contains(historicalActor, StringComparer.OrdinalIgnoreCase))
            {
                actors.Add(historicalActor);
            }
        }

        actors.Sort(StringComparer.OrdinalIgnoreCase);
        return actors;
    }

    public async Task<AuditQueryResult> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var currentUser = RequireSignedInUser();
        return await store.QueryAsync(ApplyActorScope(query, currentUser), cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditExportResult> ExportCsvAsync(
        AuditQuery query,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var currentUser = RequireSignedInUser();
        query = ApplyActorScope(query, currentUser);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("导出路径不能为空", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("安全审计只能导出为CSV文件", nameof(filePath));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            long exportedCount = 0;
            await using (var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync("UTC时间,本地时间,操作者,角色,事件类型,事件,目标类型,结果,说明,软件版本");
                var pageNumber = 1;
                while (true)
                {
                    var page = await store.QueryAsync(
                        query with { PageNumber = pageNumber, PageSize = 200 },
                        cancellationToken).ConfigureAwait(false);
                    foreach (var item in page.Items)
                    {
                        await writer.WriteLineAsync(string.Join(',', new[]
                        {
                            Csv(item.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                            Csv(item.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                            Csv(item.ActorLoginName),
                            Csv(AuditDisplayNames.Role(item.ActorRoleId)),
                            Csv(AuditDisplayNames.Category(item.Category)),
                            Csv(AuditDisplayNames.Action(item.ActionCode)),
                            Csv(item.TargetType),
                            Csv(AuditDisplayNames.Result(item.Result)),
                            Csv(item.Reason),
                            Csv(item.SoftwareVersion)
                        }));
                        exportedCount++;
                    }

                    if (exportedCount >= page.TotalCount || page.Items.Count == 0)
                    {
                        break;
                    }

                    pageNumber++;
                }
            }

            string sha256;
            await using (var stream = File.OpenRead(fullPath))
            {
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            return new AuditExportResult(fullPath, exportedCount, sha256, timeProvider.GetUtcNow());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private CurrentUserInfo RequireSignedInUser()
    {
        return accountService.CurrentUser
            ?? throw new UnauthorizedAccessException("请先登录后再访问安全审计");
    }

    internal static AuditQuery ApplyActorScope(AuditQuery query, CurrentUserInfo currentUser)
    {
        return currentUser.RoleId == AccountRoles.Admin
            ? query
            : query with { ActorLoginName = currentUser.LoginName };
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
