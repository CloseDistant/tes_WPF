namespace RuinaoSoftwareWpf.Tests;

using Microsoft.Data.Sqlite;
using Xunit;

public sealed class AuditTrailTests
{
    [Fact]
    public void FilterOptions_ToStringReturnsDisplayName()
    {
        Assert.Equal("全部事件", new AuditCategoryOption(null, "全部事件").ToString());
        Assert.Equal("全部操作者", new AuditActorOption(null, "全部操作者").ToString());
    }

    [Fact]
    public void BuildUtcDateRange_CoversEntireSelectedDay()
    {
        var selectedDate = new DateOnly(2026, 7, 17);
        var range = AuditTrailViewModel.BuildUtcDateRange(selectedDate, selectedDate);
        Assert.Equal(selectedDate.ToDateTime(TimeOnly.MinValue), range.StartUtc.ToLocalTime().DateTime);
        Assert.Equal(selectedDate.ToDateTime(TimeOnly.MaxValue), range.EndUtc.ToLocalTime().DateTime);
    }

    [Fact]
    public void ApplyActorScope_AdminKeepsSelectedActor()
    {
        var query = CreateQuery("Doctor01");
        var admin = new CurrentUserInfo(1, "Admin", "管理员", AccountRoles.Admin, false);
        Assert.Equal("Doctor01", AuditTrailAdministrationService.ApplyActorScope(query, admin).ActorLoginName);
    }

    [Theory]
    [InlineData(AccountRoles.Doctor)]
    [InlineData(AccountRoles.Technician)]
    public void ApplyActorScope_NonAdminIsForcedToCurrentAccount(int roleId)
    {
        var currentUser = new CurrentUserInfo(2, "CurrentUser", "当前用户", roleId, false);
        Assert.Equal(
            "CurrentUser",
            AuditTrailAdministrationService.ApplyActorScope(CreateQuery("Admin"), currentUser).ActorLoginName);
    }

    [Theory]
    [InlineData("login", AuditEventCategory.IdentitySession, "LOGIN")]
    [InlineData("create_patient", AuditEventCategory.PatientManagement, "CREATE_PATIENT")]
    [InlineData("Start tDCS group 1", AuditEventCategory.StimulationDevice, "STIMULATION_START")]
    public void LegacyActionMapping_UsesStableCategoryAndActionCode(
        string source,
        AuditEventCategory expectedCategory,
        string expectedActionCode)
    {
        var actual = AuditActionCatalog.FromLegacyAction(source);
        Assert.Equal(expectedCategory, actual.Category);
        Assert.Equal(expectedActionCode, actual.ActionCode);
    }

    [Fact]
    public async Task AuditStore_UsesEncryptedDatabaseAndSupportsQueries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory("audit");
        var databasePath = Path.Combine(directory, "audit.db");
        try
        {
            var service = new AuditTrailService(
                new AuditTrailStorageOptions(databasePath, ApplyDirectoryAcl: false),
                new TestLoggingService(),
                TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await service.AppendAsync(CreateEvent("Admin", AccountRoles.Admin, "LOGIN", now), cancellationToken);
            await service.AppendAsync(CreateEvent("Doctor01", AccountRoles.Doctor, "IMPEDANCE_CHECK", now.AddSeconds(1)), cancellationToken);

            var query = await service.QueryAsync(
                new AuditQuery(now.AddMinutes(-1), now.AddMinutes(1), null, null, PageSize: 50),
                cancellationToken);
            Assert.Equal(2, query.TotalCount);
            Assert.Equal(new[] { "Admin", "Doctor01" }, await service.GetActorLoginNamesAsync(cancellationToken));
            Assert.False(HasPlaintextSqliteHeader(databasePath));

            await using var context = new AuditTrailDbContext(databasePath);
            var properties = context.Model.FindEntityType(typeof(AuditEventEntity))!
                .GetProperties()
                .Select(item => item.Name)
                .ToArray();
            Assert.DoesNotContain("PreviousHmac", properties);
            Assert.DoesNotContain("EventHmac", properties);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AuditDatabase_CopyCanBeOpenedWithProductKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory("audit-copy");
        var sourcePath = Path.Combine(directory, "source.db");
        var copyPath = Path.Combine(directory, "copy.db");
        try
        {
            var source = new AuditTrailService(
                new AuditTrailStorageOptions(sourcePath, ApplyDirectoryAcl: false),
                new TestLoggingService(),
                TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await source.AppendAsync(CreateEvent("Admin", AccountRoles.Admin, "LOGIN", now), cancellationToken);
            SqliteConnection.ClearAllPools();
            File.Copy(sourcePath, copyPath);

            var copied = new AuditTrailService(
                new AuditTrailStorageOptions(copyPath, ApplyDirectoryAcl: false),
                new TestLoggingService(),
                TimeProvider.System);
            var result = await copied.QueryAsync(
                new AuditQuery(now.AddMinutes(-1), now.AddMinutes(1), null, null),
                cancellationToken);
            Assert.Single(result.Items);
            Assert.Equal("Admin", result.Items[0].ActorLoginName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryAppendAsync_WhenDatabaseCannotBeWritten_LogsAndRaisesNotification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory("audit-failure");
        var invalidDatabasePath = Path.Combine(directory, "database-as-directory");
        Directory.CreateDirectory(invalidDatabasePath);
        try
        {
            var logger = new TestLoggingService();
            var service = new AuditTrailService(
                new AuditTrailStorageOptions(invalidDatabasePath, ApplyDirectoryAcl: false),
                logger,
                TimeProvider.System);
            AuditTrailWriteFailedEventArgs? notification = null;
            service.WriteFailed += (_, args) => notification = args;

            Assert.False(await service.TryAppendAsync(
                CreateEvent("system", null, "LOGIN", DateTimeOffset.UtcNow),
                cancellationToken));
            Assert.Equal("安全审计记录写入失败，请联系管理员。", notification?.UserMessage);
            Assert.Contains(logger.Errors, item => item.Contains("安全审计记录写入失败", StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AuditEventInput CreateEvent(string loginName, int? roleId, string action, DateTimeOffset occurredAt)
    {
        return new AuditEventInput(
            AuditEventCategory.IdentitySession,
            action,
            new AuditActor(roleId is null ? null : 1, loginName, roleId),
            "Workstation",
            "local",
            AuditEventResult.Success,
            OccurredAtUtc: occurredAt);
    }

    private static AuditQuery CreateQuery(string? actorLoginName)
    {
        var now = DateTimeOffset.UtcNow;
        return new AuditQuery(now.AddHours(-1), now, null, actorLoginName);
    }

    private static string CreateTemporaryDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ruinao-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool HasPlaintextSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public List<string> Errors { get; } = [];
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) => Errors.Add(message);
        public void Hardware(string message) { }
        public void HardwareTx(string context, byte[] frame) { }
        public void HardwareRx(string context, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
