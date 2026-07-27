namespace RuinaoSoftwareWpf.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class IntegrityCheckPersistenceTests
{
    [Fact]
    public async Task GetReleaseStatusAsync_WithoutSnapshot_ReturnsNeverChecked()
    {
        var service = CreateService(
            new InMemoryReleaseIntegrityStateStore(),
            new StubReleaseIntegrityVerifier("manifest-a", isValid: true, verifiedCount: 1),
            new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero));

        var status = await service.GetReleaseStatusAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseIntegrityStatusKind.NeverChecked, status.Kind);
        Assert.Null(status.LastResult);
    }

    [Fact]
    public async Task CheckReleaseFilesAsync_NewServiceInstanceRestoresPassedResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var completedAt = new DateTimeOffset(2026, 7, 24, 9, 30, 0, TimeSpan.Zero);
        var store = new InMemoryReleaseIntegrityStateStore();
        var verifier = new StubReleaseIntegrityVerifier("manifest-a", isValid: true, verifiedCount: 12);
        var first = CreateService(store, verifier, completedAt);

        var result = await first.CheckReleaseFilesAsync(cancellationToken: cancellationToken);

        var restarted = CreateService(store, verifier, completedAt.AddMinutes(5));
        var status = await restarted.GetReleaseStatusAsync(cancellationToken);
        Assert.True(result.IsValid);
        Assert.Equal(ReleaseIntegrityStatusKind.Passed, status.Kind);
        Assert.Equal(completedAt, status.LastResult?.CompletedAt);
        Assert.Equal(12, status.LastResult?.VerifiedCount);
    }

    [Fact]
    public async Task GetReleaseStatusAsync_WhenManifestChanges_ReturnsReleaseChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var completedAt = new DateTimeOffset(2026, 7, 24, 9, 30, 0, TimeSpan.Zero);
        var store = new InMemoryReleaseIntegrityStateStore
        {
            Snapshot = new ReleaseIntegritySnapshot(
                IsValid: true,
                VerifiedCount: 8,
                Message: "软件发布文件完整性校验通过",
                CompletedAt: completedAt,
                ManifestIdentity: "manifest-a")
        };
        var service = CreateService(
            store,
            new StubReleaseIntegrityVerifier("manifest-b", isValid: true, verifiedCount: 8),
            completedAt);

        var status = await service.GetReleaseStatusAsync(cancellationToken);

        Assert.Equal(ReleaseIntegrityStatusKind.ReleaseChanged, status.Kind);
        Assert.Equal(completedAt, status.LastResult?.CompletedAt);
    }

    [Fact]
    public async Task SqliteStateStore_NewInstanceLoadsSavedSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-integrity-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "state.db");
        Directory.CreateDirectory(directory);
        try
        {
            var initializer = new TestDatabaseInitializer(databasePath);
            var coordinator = new InlineDatabaseWriteCoordinator();
            var logger = new TestLoggingService();
            var expected = new ReleaseIntegritySnapshot(
                IsValid: false,
                VerifiedCount: 3,
                Message: "发布文件内容校验失败",
                CompletedAt: new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero),
                ManifestIdentity: "manifest-c");
            var first = new SqliteReleaseIntegrityStateStore(
                initializer,
                coordinator,
                logger,
                databasePath,
                encrypted: false);

            await first.SaveAsync(expected, cancellationToken);

            var restarted = new SqliteReleaseIntegrityStateStore(
                initializer,
                coordinator,
                logger,
                databasePath,
                encrypted: false);
            Assert.Equal(expected, await restarted.LoadAsync(cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IntegrityCheckService CreateService(
        IReleaseIntegrityStateStore store,
        IReleaseIntegrityVerifier verifier,
        DateTimeOffset now)
    {
        return new IntegrityCheckService(
            new TestAuditTrailService(),
            new TestAccountService(),
            new TestLoggingService(),
            verifier,
            store,
            new FixedTimeProvider(now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubReleaseIntegrityVerifier(
        string? manifestIdentity,
        bool isValid,
        int verifiedCount) : IReleaseIntegrityVerifier
    {
        public Task<ReleaseIntegrityResult> VerifyAsync(
            IProgress<IntegrityCheckProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                isValid
                    ? new ReleaseIntegrityResult(true, string.Empty, verifiedCount)
                    : ReleaseIntegrityResult.Failure("file-hash-mismatch"));
        }

        public Task<string?> GetManifestIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(manifestIdentity);
        }
    }

    private sealed class InMemoryReleaseIntegrityStateStore : IReleaseIntegrityStateStore
    {
        public ReleaseIntegritySnapshot? Snapshot { get; set; }

        public Task<ReleaseIntegritySnapshot?> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Snapshot);
        }

        public Task SaveAsync(
            ReleaseIntegritySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Snapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDatabaseInitializer(string databasePath) : IAppDatabaseInitializer
    {
        public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            await using var context = new CaptureDbContext(databasePath, encrypted: false);
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private sealed class InlineDatabaseWriteCoordinator : IAppDatabaseWriteCoordinator
    {
        public Task ExecuteAsync(
            string databasePath,
            Func<Task> operation,
            CancellationToken cancellationToken = default)
        {
            return operation();
        }

        public Task<T> ExecuteAsync<T>(
            string databasePath,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation();
        }
    }

    private sealed class TestAccountService : IAccountService
    {
        public CurrentUserInfo? CurrentUser { get; } =
            new(1, "Admin", "管理员", AccountRoles.Admin, MustChangePassword: false);

        public event EventHandler? CurrentUserChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetRememberedLoginNameAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetRememberedLoginNameAsync(string? loginName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountPasswordVerificationResult> VerifyCurrentPasswordAsync(string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurrentUserInfo> CreateUserAsync(CreateAccountRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PageResult<AccountListItemInfo>> GetAccountListPageAsync(PageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetActiveLoginNamesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordAuditAsync(long? operatorUserId, long? targetUserId, string action, string result, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsCurrentUserAdmin() => true;
    }

    private sealed class TestAuditTrailService : IAuditTrailService
    {
        public event EventHandler<AuditTrailWriteFailedEventArgs>? WriteFailed
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendAsync(AuditEventInput auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryAppendAsync(AuditEventInput auditEvent, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string context, byte[] frame) { }
        public void HardwareRx(string context, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
