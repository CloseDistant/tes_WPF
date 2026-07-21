namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class BackupRestoreService : IBackupRestoreService
{
    private const string Extension = ".rnbak";
    private const string Magic = "RNBAK01\n";
    private const int KdfIterations = 210_000;
    private const long MinimumFreeBytes = 100L * 1024 * 1024;
    private const string StatusFileName = "backup-status.json";
    private static readonly string[] PreservedStateKeys =
    [
        "software_activation_credential_v1",
        "auto_connect_on_startup"
    ];

    private readonly AuditTrailStorageOptions auditOptions;
    private readonly AuditTrailService auditTrail;
    private readonly IAuthorizationService authorization;
    private readonly IStimulationEngine stimulationEngine;
    private readonly IEegRecordingService eegRecording;
    private readonly IAssessmentActivityState assessmentActivity;
    private readonly ILoggingService logger;

    public BackupRestoreService(
        AuditTrailStorageOptions auditOptions,
        AuditTrailService auditTrail,
        IAuthorizationService authorization,
        IStimulationEngine stimulationEngine,
        IEegRecordingService eegRecording,
        IAssessmentActivityState assessmentActivity,
        ILoggingService logger)
    {
        this.auditOptions = auditOptions;
        this.auditTrail = auditTrail;
        this.authorization = authorization;
        this.stimulationEngine = stimulationEngine;
        this.eegRecording = eegRecording;
        this.assessmentActivity = assessmentActivity;
        this.logger = logger;
    }

    public Task<BackupLocationInfo> GetDefaultBackupLocationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var estimate = EstimateBackupSize();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (drive.DriveType != DriveType.Removable || !drive.IsReady)
                {
                    continue;
                }

                var directory = Path.Combine(drive.RootDirectory.FullName, "ruinao");
                Directory.CreateDirectory(directory);
                CleanupPartialBackupFiles(directory);
                return Task.FromResult(new BackupLocationInfo(
                    directory,
                    true,
                    drive.AvailableFreeSpace,
                    estimate,
                    string.Empty));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warning($"移动磁盘不可用于数据备份：drive={drive.Name}; reason={exception.Message}");
            }
        }

        return Task.FromResult(new BackupLocationInfo(
            null,
            false,
            0,
            estimate,
            "未检测到可用移动磁盘，请连接后重新检测"));
    }

    public async Task<BackupStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var path = GetStatusPath();
        if (!File.Exists(path))
        {
            return new BackupStatus(null, null);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BackupStatus>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? new BackupStatus(null, null);
        }
        catch (Exception exception)
        {
            logger.Warning($"最近备份状态读取失败：{exception.Message}");
            return new BackupStatus(null, null);
        }
    }

    public async Task<BackupOperationResult> CreateBackupAsync(
        string directoryPath,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var actor = authorization.Demand(AppPermission.ManageBackupRestore);
        DemandMaintenanceIdle();
        ValidatePassword(password);
        var directory = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(directory);
        CleanupPartialBackupFiles(directory);
        var available = new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace;
        var estimated = EstimateBackupSize();
        var required = Math.Max(MinimumFreeBytes, estimated * 2);
        if (available < required)
        {
            throw new IOException($"目标磁盘空间不足。预计需要 {FormatSize(required)}，当前可用 {FormatSize(available)}。");
        }

        var operationDirectory = CreateOperationDirectory("backup");
        try
        {
            progress?.Report(new BackupOperationProgress("生成快照", 8, "正在生成数据库快照"));
            var businessSnapshot = Path.Combine(operationDirectory, "ruinao_app.db");
            var auditSnapshot = Path.Combine(operationDirectory, "security_audit.db");
            await CreateSqliteSnapshotAsync(AppDatabasePathProvider.MainDatabasePath, businessSnapshot, cancellationToken)
                .ConfigureAwait(false);
            await CreateSqliteSnapshotAsync(auditOptions.DatabasePath, auditSnapshot, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new BackupOperationProgress("数据校验", 35, "正在校验备份内容"));
            await VerifySqliteAsync(businessSnapshot, cancellationToken).ConfigureAwait(false);
            await VerifySqliteAsync(auditSnapshot, cancellationToken).ConfigureAwait(false);
            var patientKey = await File.ReadAllBytesAsync(AppDatabasePathProvider.PatientKeyPath, cancellationToken)
                .ConfigureAwait(false);
            PatientDataProtector.ValidateKeyFile(patientKey);

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["data/ruinao_app.db"] = await File.ReadAllBytesAsync(businessSnapshot, cancellationToken).ConfigureAwait(false),
                ["data/security/security_audit.db"] = await File.ReadAllBytesAsync(auditSnapshot, cancellationToken).ConfigureAwait(false),
                ["keys/patient.key"] = patientKey
            };
            var manifest = CreateManifest(files);
            files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest);

            progress?.Report(new BackupOperationProgress("加密封装", 58, "正在生成加密备份包"));
            var archive = CreateArchive(files);
            var destination = GetAvailableBackupPath(directory);
            var partial = destination + ".partial";
            TryDeleteFile(partial);
            try
            {
                await WriteEncryptedPackageAsync(partial, archive, password, cancellationToken).ConfigureAwait(false);

                progress?.Report(new BackupOperationProgress("数据校验", 88, "正在复核备份包"));
                await ValidatePackageAsync(partial, password, cancellationToken).ConfigureAwait(false);
                File.Move(partial, destination);
            }
            finally
            {
                TryDeleteFile(partial);
            }
            var status = new BackupStatus(DateTimeOffset.Now, Path.GetFileName(destination));
            await SaveStatusAsync(status, cancellationToken).ConfigureAwait(false);
            await auditTrail.TryAppendAsync(
                new AuditEventInput(
                    AuditEventCategory.DataExport,
                    "DATA_BACKUP_CREATE",
                    AuditActor.From(actor),
                    "BackupPackage",
                    Path.GetFileName(destination),
                    AuditEventResult.Success,
                    Reason: $"size={new FileInfo(destination).Length}"),
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new BackupOperationProgress("完成", 100, Path.GetFileName(destination)));
            return new BackupOperationResult(true, "数据备份已创建", destination);
        }
        catch (Exception exception)
        {
            logger.Error("数据备份失败", exception);
            throw;
        }
        finally
        {
            TryDeleteDirectory(operationDirectory);
        }
    }

    public async Task<BackupOperationResult> RestoreBackupAsync(
        string backupFilePath,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        authorization.Demand(AppPermission.ManageBackupRestore);
        DemandMaintenanceIdle();
        ValidatePassword(password);
        var source = Path.GetFullPath(backupFilePath);
        if (!string.Equals(Path.GetExtension(source), Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("只能恢复 .rnbak 格式的数据备份");
        }

        var operationDirectory = CreateOperationDirectory("restore");
        var localPackage = Path.Combine(operationDirectory, "source.rnbak");
        var rollbackDirectory = Path.Combine(operationDirectory, "rollback");
        Directory.CreateDirectory(rollbackDirectory);
        var replaced = false;
        try
        {
            progress?.Report(new BackupOperationProgress("读取备份", 8, "正在复制并验证备份文件"));
            await CopyFileAsync(source, localPackage, cancellationToken).ConfigureAwait(false);
            var files = await ReadAndValidatePackageAsync(localPackage, password, cancellationToken).ConfigureAwait(false);
            progress?.Report(new BackupOperationProgress("数据校验", 35, "正在检查数据库和患者数据密钥"));
            var stagedBusiness = WriteStagedFile(operationDirectory, "ruinao_app.db", files["data/ruinao_app.db"]);
            var stagedAudit = WriteStagedFile(operationDirectory, "security_audit.db", files["data/security/security_audit.db"]);
            await VerifySqliteAsync(stagedBusiness, cancellationToken).ConfigureAwait(false);
            await VerifySqliteAsync(stagedAudit, cancellationToken).ConfigureAwait(false);
            await PreserveLocalMachineStateAsync(
                AppDatabasePathProvider.MainDatabasePath,
                stagedBusiness,
                cancellationToken).ConfigureAwait(false);

            var stagedPatientKey = WriteStagedFile(operationDirectory, "patient.key", files["keys/patient.key"]);
            PatientDataProtector.ValidateKeyFile(files["keys/patient.key"]);
            progress?.Report(new BackupOperationProgress("恢复数据", 65, "正在替换本机数据"));
            SqliteConnection.ClearAllPools();
            var replacements = new[]
            {
                (AppDatabasePathProvider.MainDatabasePath, stagedBusiness),
                (auditOptions.DatabasePath, stagedAudit),
                (AppDatabasePathProvider.PatientKeyPath, stagedPatientKey)
            };
            var recoveryFiles = new List<RestoreFileState>();
            foreach (var (target, _) in replacements)
            {
                var existed = File.Exists(target);
                var rollback = Path.Combine(rollbackDirectory, Path.GetFileName(target));
                if (existed) File.Copy(target, rollback, true);
                recoveryFiles.Add(new RestoreFileState(target, rollback, existed));
            }
            BackupRestoreRecoveryGuard.WritePending(new RestoreRecoveryState(operationDirectory, recoveryFiles));
            auditTrail.SuspendWrites();
            replaced = true;
            foreach (var (target, staged) in replacements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(staged, target + ".restore", true);
                File.Move(target + ".restore", target, true);
            }
            progress?.Report(new BackupOperationProgress("恢复校验", 90, "正在确认恢复结果"));
            await VerifySqliteAsync(AppDatabasePathProvider.MainDatabasePath, cancellationToken).ConfigureAwait(false);
            await VerifySqliteAsync(auditOptions.DatabasePath, cancellationToken).ConfigureAwait(false);
            BackupRestoreRecoveryGuard.Complete();
            progress?.Report(new BackupOperationProgress("完成", 100, Path.GetFileName(source)));
            logger.Info($"数据恢复完成：package={Path.GetFileName(source)}。软件需退出后重新打开。");
            return new BackupOperationResult(true, "数据已成功恢复，请退出软件后重新打开。", source);
        }
        catch (Exception exception)
        {
            if (replaced)
            {
                if (!BackupRestoreRecoveryGuard.TryRecoverPending(out var recoveryMessage))
                {
                    logger.Error(recoveryMessage);
                    throw new DataRestoreRequiresExitException(
                        "数据恢复失败且无法自动回滚。软件必须立即退出，请联系维护人员。",
                        exception);
                }
            }
            auditTrail.ResumeWrites();
            logger.Error("数据恢复失败", exception);
            throw;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (!File.Exists(BackupRestoreRecoveryGuard.StatePath)) TryDeleteDirectory(operationDirectory);
        }
    }

    private void DemandMaintenanceIdle()
    {
        var stimulationActive = stimulationEngine.CurrentState is StimulationExecutionState.Armed
            or StimulationExecutionState.Starting
            or StimulationExecutionState.Running
            or StimulationExecutionState.Stopping
            or StimulationExecutionState.Paused
            or StimulationExecutionState.Faulted;
        if (stimulationActive || eegRecording.IsRecording || assessmentActivity.IsActiveForSessionSecurity)
        {
            throw new InvalidOperationException("当前存在采集或电刺激任务，请结束后再执行数据备份或恢复");
        }
    }

    private long EstimateBackupSize()
    {
        const long packageOverheadBytes = 128L * 1024;
        long size = packageOverheadBytes;
        foreach (var path in new[]
                 {
                     AppDatabasePathProvider.MainDatabasePath,
                     auditOptions.DatabasePath,
                     AppDatabasePathProvider.PatientKeyPath
                 })
        {
            size += GetFileLength(path);
            if (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                size += GetFileLength(path + "-wal");
            }
        }
        return size;
    }

    private static long GetFileLength(string path) => File.Exists(path)
        ? new FileInfo(path).Length
        : 0;

    private static async Task CreateSqliteSnapshotAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("数据库文件不存在", sourcePath);
        await using var source = EncryptedSqliteDatabase.CreateConnection(
            sourcePath,
            SqliteOpenMode.ReadOnly,
            pooling: false);
        await using var destination = EncryptedSqliteDatabase.CreateConnection(
            destinationPath,
            SqliteOpenMode.ReadWriteCreate,
            pooling: false);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }

    private static async Task VerifySqliteAsync(string path, CancellationToken cancellationToken)
    {
        await EncryptedSqliteDatabase.VerifyCanOpenAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static BackupManifest CreateManifest(IReadOnlyDictionary<string, byte[]> files)
    {
        return new BackupManifest(
            1,
            "1.0.0",
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"),
            files.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)),
                StringComparer.Ordinal));
    }

    private static byte[] CreateArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in files)
            {
                var entry = archive.CreateEntry(item.Key, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(item.Value);
            }
        }
        return output.ToArray();
    }

    private static async Task WriteEncryptedPackageAsync(
        string path,
        byte[] archive,
        string password,
        CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, KdfIterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[archive.Length];
        var tag = new byte[16];
        try
        {
            using (var aes = new AesGcm(key, tag.Length))
            {
                aes.Encrypt(nonce, archive, cipher, tag, Encoding.ASCII.GetBytes(Magic));
            }

            var header = JsonSerializer.SerializeToUtf8Bytes(new PackageHeader(
                1,
                KdfIterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce)));
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                useAsync: true);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Magic), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(BitConverter.GetBytes(header.Length), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(cipher, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task ValidatePackageAsync(string path, string password, CancellationToken cancellationToken)
    {
        _ = await ReadAndValidatePackageAsync(path, password, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, byte[]>> ReadAndValidatePackageAsync(
        string path,
        string password,
        CancellationToken cancellationToken)
    {
        var package = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var magic = Encoding.ASCII.GetBytes(Magic);
        if (package.Length < magic.Length + 4 + 16 || !package.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new InvalidDataException("备份文件格式无效");
        }

        var headerLength = BitConverter.ToInt32(package, magic.Length);
        var payloadOffset = magic.Length + 4 + headerLength;
        if (headerLength <= 0 || payloadOffset + 16 > package.Length)
        {
            throw new InvalidDataException("备份文件头无效");
        }
        var header = JsonSerializer.Deserialize<PackageHeader>(package.AsSpan(magic.Length + 4, headerLength))
            ?? throw new InvalidDataException("备份文件头无法读取");
        if (header.Version != 1) throw new InvalidDataException("不支持的备份格式版本");
        var salt = Convert.FromBase64String(header.Salt);
        var nonce = Convert.FromBase64String(header.Nonce);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, header.Iterations, HashAlgorithmName.SHA256, 32);
        var cipherLength = package.Length - payloadOffset - 16;
        var plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                nonce,
                package.AsSpan(payloadOffset, cipherLength),
                package.AsSpan(payloadOffset + cipherLength, 16),
                plain,
                Encoding.ASCII.GetBytes(Magic));
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("备份密码错误或备份文件已损坏", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var input = new MemoryStream(plain);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(entry.FullName))
            {
                throw new InvalidDataException("备份包包含无效路径");
            }
            using var stream = entry.Open();
            using var output = new MemoryStream();
            stream.CopyTo(output);
            files[entry.FullName] = output.ToArray();
        }

        if (!files.TryGetValue("manifest.json", out var manifestBytes))
        {
            throw new InvalidDataException("备份清单缺失");
        }
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestBytes)
            ?? throw new InvalidDataException("备份清单无效");
        if (manifest.FormatVersion != 1 || !string.Equals(manifest.SoftwareVersion, "1.0.0", StringComparison.Ordinal))
        {
            throw new InvalidDataException("备份版本与当前软件不兼容");
        }
        foreach (var expected in manifest.Sha256)
        {
            if (!files.TryGetValue(expected.Key, out var value)
                || !string.Equals(Convert.ToHexString(SHA256.HashData(value)), expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"备份内容校验失败：{expected.Key}");
            }
        }
        return files;
    }

    private static async Task PreserveLocalMachineStateAsync(
        string currentDatabase,
        string stagedDatabase,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(currentDatabase))
        {
            await using var source = new CaptureDbContext(currentDatabase);
            var preserved = await source.AppStates
                .AsNoTracking()
                .Where(item => PreservedStateKeys.Contains(item.Key))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in preserved)
            {
                states[item.Key] = item.Value;
            }
        }

        await using var target = new CaptureDbContext(stagedDatabase);
        foreach (var key in PreservedStateKeys)
        {
            var entity = await target.AppStates.FindAsync([key], cancellationToken).ConfigureAwait(false);
            if (states.TryGetValue(key, out var value))
            {
                if (entity is null)
                {
                    target.AppStates.Add(new AppStateEntity
                    {
                        Key = key,
                        Value = value,
                        UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }
                else
                {
                    entity.Value = value;
                    entity.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }
            else if (entity is not null)
            {
                target.AppStates.Remove(entity);
            }
        }
        await target.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetAvailableBackupPath(string directory)
    {
        var prefix = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "_数据备份";
        var path = Path.Combine(directory, prefix + Extension);
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(directory, $"{prefix}_{index:00}{Extension}");
        }
        return path;
    }

    private static string CreateOperationDirectory(string operation)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!,
            ".maintenance",
            $"{operation}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteStagedFile(string directory, string fileName, byte[] bytes)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveStatusAsync(BackupStatus status, CancellationToken cancellationToken)
    {
        var path = GetStatusPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(status), cancellationToken).ConfigureAwait(false);
    }

    private static string GetStatusPath() => Path.Combine(
        Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!,
        StatusFileName);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            var parent = Directory.GetParent(directory)?.FullName;
            if (parent is not null
                && string.Equals(Path.GetFileName(parent), ".maintenance", StringComparison.Ordinal)
                && Directory.Exists(parent)
                && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch
        {
            // 临时目录清理由下次维护处理，不覆盖主操作结果。
        }
    }

    private void CleanupPartialBackupFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     $"*{Extension}.partial",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                File.Delete(path);
                logger.Info($"已清理未完成的数据备份：file={Path.GetFileName(path)}");
            }
            catch (IOException)
            {
                // A backup still in use is left untouched.
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.Warning($"未完成备份清理失败：file={Path.GetFileName(path)}; reason={exception.Message}");
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The next backup operation will retry cleanup.
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("备份密码至少需要8位字符", nameof(password));
        }
    }

    private static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : $"{bytes / (1024d * 1024):0.0} MB";

    private sealed record PackageHeader(int Version, int Iterations, string Salt, string Nonce);

    private sealed record BackupManifest(
        int FormatVersion,
        string SoftwareVersion,
        DateTimeOffset CreatedAtUtc,
        string PackageId,
        Dictionary<string, string> Sha256);
}

public sealed class DataRestoreRequiresExitException : InvalidOperationException
{
    public DataRestoreRequiresExitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
