namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

public sealed class LocalPatientService : IPatientService
{
    private const int MaxRetryCount = 3;
    private readonly ILoggingService logger;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly PatientDataProtector dataProtector;
    private readonly IAccountService accountService;
    private readonly IAuthorizationService authorizationService;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool ready;
    private PatientRecord? currentPatient;
    private long? currentPatientOwnerUserId;

    public LocalPatientService(
        ILoggingService logger,
        IAppDatabaseInitializer databaseInitializer,
        PatientDataProtector dataProtector,
        IAccountService accountService,
        IAuthorizationService authorizationService)
    {
        this.logger = logger;
        this.databaseInitializer = databaseInitializer;
        this.dataProtector = dataProtector;
        this.accountService = accountService;
        this.authorizationService = authorizationService;
        this.accountService.CurrentUserChanged += (_, _) => ClearCurrentPatient();
    }

    public event EventHandler? CurrentPatientChanged;

    public PatientRecord? CurrentPatient => currentPatient;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        ClearCurrentPatient();
    }

    public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        PatientSaveRequestValidator.EnsureValid(request, PatientFormMode.Create);
        return ExecuteWriteAsync(async () =>
        {
            await EnsureReadyAsync(cancellationToken);
            var owner = RequirePatientManager();
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.Now;
            var patientCode = string.IsNullOrWhiteSpace(request.PatientCode)
                ? await GeneratePatientCodeAsync(context, now, cancellationToken)
                : request.PatientCode.Trim();

            if (await context.Patients.AnyAsync(item => item.PatientCode == patientCode, cancellationToken))
            {
                throw new InvalidOperationException($"患者 ID 已存在：{patientCode}");
            }

            var entity = new PatientEntity
            {
                OwnerUserId = owner.UserId,
                UpdatedByUserId = owner.UserId,
                PatientCode = patientCode,
                Name = request.Name.Trim(),
                Gender = request.Sex!.Value.ToStorageCode(),
                BirthDateUnixMs = ToUnixMs(request.BirthDate!.Value),
                IdCardEncrypted = dataProtector.Protect(request.IdCardNumber),
                PhoneEncrypted = dataProtector.Protect(request.Phone),
                EmergencyContactName = Normalize(request.EmergencyContactName),
                EmergencyContactPhoneEncrypted = dataProtector.Protect(request.EmergencyContactPhone),
                HomeAddress = Normalize(request.HomeAddress),
                ClinicalInfo = Normalize(request.ClinicalInfo),
                CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
                UpdatedAtUnixMs = now.ToUnixTimeMilliseconds()
            };
            context.Patients.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            currentPatient = ToRecord(entity);
            currentPatientOwnerUserId = owner.UserId;
            CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            await accountService.RecordAuditAsync(
                owner.UserId,
                null,
                "create_patient",
                "success",
                $"创建患者：patientCode={patientCode}",
                cancellationToken);
            logger.Info($"新增患者：patientCode={patientCode}, ownerUserId={owner.UserId}");
            return currentPatient;
        }, cancellationToken);
    }

    public async Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        RequirePatientManager();
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        return await GeneratePatientCodeAsync(context, DateTimeOffset.Now, cancellationToken);
    }

    public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        PatientSaveRequestValidator.EnsureValid(request, PatientFormMode.Edit);
        return ExecuteWriteAsync(async () =>
        {
            await EnsureReadyAsync(cancellationToken);
            var owner = RequirePatientManager();
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Patients.FirstOrDefaultAsync(
                    item => item.PatientCode == request.PatientCode && item.OwnerUserId == owner.UserId,
                    cancellationToken)
                ?? throw new InvalidOperationException($"未找到患者：{request.PatientCode}");

            entity.Name = request.Name.Trim();
            entity.Gender = request.Sex!.Value.ToStorageCode();
            entity.BirthDateUnixMs = ToUnixMs(request.BirthDate!.Value);
            entity.IdCardEncrypted = dataProtector.Protect(request.IdCardNumber);
            entity.PhoneEncrypted = dataProtector.Protect(request.Phone);
            entity.EmergencyContactName = Normalize(request.EmergencyContactName);
            entity.EmergencyContactPhoneEncrypted = dataProtector.Protect(request.EmergencyContactPhone);
            entity.HomeAddress = Normalize(request.HomeAddress);
            // 临床信息仅在创建患者时记录；编辑界面不提供修改入口，更新时保留原值。
            entity.UpdatedByUserId = owner.UserId;
            entity.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            await context.SaveChangesAsync(cancellationToken);

            var record = ToRecord(entity);
            if (currentPatient?.PatientCode == record.PatientCode && currentPatientOwnerUserId == owner.UserId)
            {
                currentPatient = record;
                CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            }

            await accountService.RecordAuditAsync(
                owner.UserId,
                null,
                "update_patient",
                "success",
                $"更新患者：patientCode={record.PatientCode}",
                cancellationToken);
            logger.Info($"更新患者：patientCode={record.PatientCode}, ownerUserId={owner.UserId}");
            return record;
        }, cancellationToken);
    }

    public async Task<PageResult<PatientRecord>> GetPatientsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken);
        var owner = RequirePatientManager();
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var searchText = request.NormalizedSearchText;
        var query = context.Patients
            .AsNoTracking()
            .Where(item => item.OwnerUserId == owner.UserId);
        if (searchText.Length > 0)
        {
            query = query.Where(item =>
                item.PatientCode.Contains(searchText) ||
                (item.Name != null && item.Name.Contains(searchText)));
        }

        var entities = await query
            .OrderByDescending(item => item.UpdatedAtUnixMs)
            .ThenByDescending(item => item.Id)
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = entities.Count > request.SafePageSize;
        if (hasMore)
        {
            entities.RemoveAt(entities.Count - 1);
        }

        return new PageResult<PatientRecord>(entities.Select(ToRecord).ToList(), hasMore);
    }

    public Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(patientCode))
        {
            throw new InvalidOperationException("请选择患者");
        }

        return ExecuteWriteAsync(async () =>
        {
            await EnsureReadyAsync(cancellationToken);
            var owner = RequirePatientManager();
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Patients.FirstOrDefaultAsync(
                    item => item.PatientCode == patientCode && item.OwnerUserId == owner.UserId,
                    cancellationToken)
                ?? throw new InvalidOperationException($"未找到患者：{patientCode}");
            currentPatient = ToRecord(entity);
            currentPatientOwnerUserId = owner.UserId;
            CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            await accountService.RecordAuditAsync(
                owner.UserId,
                null,
                "switch_patient",
                "success",
                $"切换患者：patientCode={patientCode}",
                cancellationToken);
            logger.Info($"切换患者：patientCode={patientCode}, ownerUserId={owner.UserId}");
            return currentPatient;
        }, cancellationToken);
    }

    public async Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var owner = RequirePatientManager();
        return currentPatient is not null && currentPatientOwnerUserId == owner.UserId
            ? currentPatient.PatientCode
            : throw new InvalidOperationException("请先新增或选择患者，再开始采集。");
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (ready)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (ready)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var currentCiphertextExists = await context.Patients.AnyAsync(item =>
                (item.IdCardEncrypted != null && item.IdCardEncrypted.StartsWith("v2:"))
                || (item.PhoneEncrypted != null && item.PhoneEncrypted.StartsWith("v2:"))
                || (item.EmergencyContactPhoneEncrypted != null
                    && item.EmergencyContactPhoneEncrypted.StartsWith("v2:")),
                cancellationToken);
            dataProtector.Initialize(currentCiphertextExists);
            // 登录路径不执行旧密文全表迁移；v1 密文仍可按条读取，避免启动时扫描全部患者。
            ready = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    private async Task MigrateLegacyCiphertextAsync(CaptureDbContext context, CancellationToken cancellationToken)
    {
        const int batchSize = 200;
        long lastId = 0;
        var migratedCount = 0;
        while (true)
        {
            var entities = await context.Patients
                .Where(item => item.Id > lastId)
                .OrderBy(item => item.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (entities.Count == 0)
            {
                break;
            }

            foreach (var entity in entities)
            {
                var changed = false;
                if (TryReprotectLegacy(entity.IdCardEncrypted, "IdCardNumber", out var idCard))
                {
                    entity.IdCardEncrypted = idCard;
                    changed = true;
                }

                if (TryReprotectLegacy(entity.PhoneEncrypted, "Phone", out var phone))
                {
                    entity.PhoneEncrypted = phone;
                    changed = true;
                }

                if (TryReprotectLegacy(entity.EmergencyContactPhoneEncrypted, "EmergencyContactPhone", out var emergencyPhone))
                {
                    entity.EmergencyContactPhoneEncrypted = emergencyPhone;
                    changed = true;
                }

                if (changed)
                {
                    migratedCount++;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            lastId = entities[^1].Id;
            context.ChangeTracker.Clear();
        }

        if (migratedCount > 0)
        {
            logger.Info($"旧版患者敏感字段已分批迁移到自动密钥加密格式：patients={migratedCount}。");
        }
    }

    private bool TryReprotectLegacy(string? ciphertext, string fieldName, out string? migratedCiphertext)
    {
        migratedCiphertext = ciphertext;
        if (!PatientDataProtector.IsLegacyCiphertext(ciphertext))
        {
            return false;
        }

        var plainText = dataProtector.Unprotect(ciphertext, fieldName);
        if (plainText is null)
        {
            return false;
        }

        migratedCiphertext = dataProtector.Protect(plainText);
        return true;
    }

    private CurrentUserInfo RequirePatientManager()
    {
        return authorizationService.Demand(AppPermission.ManagePatients);
    }

    private void ClearCurrentPatient()
    {
        currentPatient = null;
        currentPatientOwnerUserId = null;
        CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<string> GeneratePatientCodeAsync(CaptureDbContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = $"P{now:yyyyMMdd}";
        var codes = await context.Patients.Where(item => item.PatientCode.StartsWith(prefix)).Select(item => item.PatientCode).ToListAsync(cancellationToken);
        var max = codes.Select(code => code.Length > prefix.Length
                && int.TryParse(code[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                    ? sequence
                    : 0)
            .DefaultIfEmpty()
            .Max();
        return $"{prefix}{max + 1:0000}";
    }

    private async Task<T> ExecuteWriteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception exception) when (IsRetryableSqliteException(exception) && attempt < MaxRetryCount)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(120 * attempt), cancellationToken);
                }
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static bool IsRetryableSqliteException(Exception exception)
    {
        if (exception is DbUpdateException { InnerException: not null } updateException)
        {
            return IsRetryableSqliteException(updateException.InnerException!);
        }

        return exception is SqliteException { SqliteErrorCode: 5 or 6 };
    }

    private PatientRecord ToRecord(PatientEntity entity)
    {
        var birthDate = FromUnixMs(entity.BirthDateUnixMs);
        return new PatientRecord(
            entity.PatientCode,
            entity.Name ?? string.Empty,
            PatientSexExtensions.FromStorageCode(entity.Gender),
            birthDate,
            CalculateAge(birthDate),
            dataProtector.Unprotect(entity.IdCardEncrypted, "IdCardNumber"),
            dataProtector.Unprotect(entity.PhoneEncrypted, "Phone") ?? string.Empty,
            entity.EmergencyContactName,
            dataProtector.Unprotect(entity.EmergencyContactPhoneEncrypted, "EmergencyContactPhone"),
            entity.HomeAddress,
            entity.ClinicalInfo,
            DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAtUnixMs).ToLocalTime(),
            DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAtUnixMs).ToLocalTime());
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long ToUnixMs(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now)).ToUnixTimeMilliseconds();

    private static DateOnly FromUnixMs(long? unixMs) => unixMs is null or <= 0
        ? new DateOnly(1999, 1, 1)
        : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value).LocalDateTime);

    public static int CalculateAge(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return Math.Max(0, age);
    }
}
