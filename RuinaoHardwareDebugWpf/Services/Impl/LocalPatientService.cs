namespace RuinaoHardwareDebugWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

public sealed class LocalPatientService : IPatientService
{
    private const string CurrentPatientKey = "current_patient_code";
    private const int MaxRetryCount = 3;
    private readonly ILoggingService logger;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly PatientDataProtector dataProtector;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool ready;
    private PatientRecord? currentPatient;

    public LocalPatientService(
        ILoggingService logger,
        IAppDatabaseInitializer databaseInitializer,
        PatientDataProtector dataProtector)
    {
        this.logger = logger;
        this.databaseInitializer = databaseInitializer;
        this.dataProtector = dataProtector;
    }

    public event EventHandler? CurrentPatientChanged;

    public PatientRecord? CurrentPatient => currentPatient;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var currentCode = await context.AppStates
            .Where(item => item.Key == CurrentPatientKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var entity = string.IsNullOrWhiteSpace(currentCode)
            ? null
            : await context.Patients.FirstOrDefaultAsync(item => item.PatientCode == currentCode, cancellationToken);
        entity ??= await context.Patients.OrderByDescending(item => item.UpdatedAtUnixMs).FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return;
        }

        currentPatient = ToRecord(entity);
        await SaveCurrentPatientCodeAsync(context, entity.PatientCode, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
        logger.Info($"当前患者已恢复：{entity.PatientCode}");
    }

    public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        PatientSaveRequestValidator.EnsureValid(request, PatientFormMode.Create);
        return ExecuteWriteAsync(async () =>
        {
            await EnsureReadyAsync(cancellationToken);
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
            await SaveCurrentPatientCodeAsync(context, patientCode, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            currentPatient = ToRecord(entity);
            CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            logger.Info($"新增患者：{patientCode}");
            return currentPatient;
        }, cancellationToken);
    }

    public async Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        return await GeneratePatientCodeAsync(context, DateTimeOffset.Now, cancellationToken);
    }

    public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        PatientSaveRequestValidator.EnsureValid(request, PatientFormMode.Edit);
        return ExecuteWriteAsync(async () =>
        {
            await EnsureReadyAsync(cancellationToken);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Patients.FirstOrDefaultAsync(item => item.PatientCode == request.PatientCode, cancellationToken)
                ?? throw new InvalidOperationException($"未找到患者：{request.PatientCode}");

            entity.Name = request.Name.Trim();
            entity.Gender = request.Sex!.Value.ToStorageCode();
            entity.BirthDateUnixMs = ToUnixMs(request.BirthDate!.Value);
            entity.IdCardEncrypted = dataProtector.Protect(request.IdCardNumber);
            entity.PhoneEncrypted = dataProtector.Protect(request.Phone);
            entity.EmergencyContactName = Normalize(request.EmergencyContactName);
            entity.EmergencyContactPhoneEncrypted = dataProtector.Protect(request.EmergencyContactPhone);
            entity.HomeAddress = Normalize(request.HomeAddress);
            entity.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            await context.SaveChangesAsync(cancellationToken);

            var record = ToRecord(entity);
            if (currentPatient?.PatientCode == record.PatientCode)
            {
                currentPatient = record;
                CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            }

            logger.Info($"更新患者：{record.PatientCode}");
            return record;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientRecord>> GetPatientsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var entities = await context.Patients.OrderByDescending(item => item.UpdatedAtUnixMs).ToListAsync(cancellationToken);
        return entities.Select(ToRecord).ToList();
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
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Patients.FirstOrDefaultAsync(item => item.PatientCode == patientCode, cancellationToken)
                ?? throw new InvalidOperationException($"未找到患者：{patientCode}");
            await SaveCurrentPatientCodeAsync(context, patientCode, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            currentPatient = ToRecord(entity);
            CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
            logger.Info($"切换患者：{patientCode}");
            return currentPatient;
        }, cancellationToken);
    }

    public async Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        if (currentPatient is null)
        {
            await InitializeAsync(cancellationToken);
        }

        return currentPatient?.PatientCode ?? throw new InvalidOperationException("请先新增或选择患者，再开始采集。");
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
            var encryptedValues = await context.Patients
                .Select(item => new { item.IdCardEncrypted, item.PhoneEncrypted, item.EmergencyContactPhoneEncrypted })
                .ToListAsync(cancellationToken);
            dataProtector.Initialize(encryptedValues.Any(item =>
                PatientDataProtector.IsCurrentCiphertext(item.IdCardEncrypted)
                || PatientDataProtector.IsCurrentCiphertext(item.PhoneEncrypted)
                || PatientDataProtector.IsCurrentCiphertext(item.EmergencyContactPhoneEncrypted)));
            await MigrateLegacyCiphertextAsync(context, cancellationToken);
            ready = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    private async Task MigrateLegacyCiphertextAsync(CaptureDbContext context, CancellationToken cancellationToken)
    {
        var entities = await context.Patients.ToListAsync(cancellationToken);
        var changed = false;
        foreach (var entity in entities)
        {
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
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.Info("旧版患者敏感字段已迁移到自动密钥加密格式。");
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

    private static async Task SaveCurrentPatientCodeAsync(CaptureDbContext context, string patientCode, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var state = await context.AppStates.FirstOrDefaultAsync(item => item.Key == CurrentPatientKey, cancellationToken);
        if (state is null)
        {
            context.AppStates.Add(new AppStateEntity { Key = CurrentPatientKey, Value = patientCode, UpdatedAtUnixMs = now });
        }
        else
        {
            state.Value = patientCode;
            state.UpdatedAtUnixMs = now;
        }
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
