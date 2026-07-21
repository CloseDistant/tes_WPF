namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

/// <summary>数据库持久化的公用处方服务，不绑定患者或登录账号。</summary>
public sealed class PrescriptionService : IPrescriptionService
{
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAuditTrailService auditTrail;
    private readonly IAccountService accountService;
    private readonly IAuthorizationService authorizationService;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public PrescriptionService(
        IAppDatabaseInitializer databaseInitializer,
        IAuditTrailService auditTrail,
        IAccountService accountService,
        IAuthorizationService authorizationService)
    {
        this.databaseInitializer = databaseInitializer;
        this.auditTrail = auditTrail;
        this.accountService = accountService;
        this.authorizationService = authorizationService;
    }

    public async Task<PageResult<PrescriptionDefinition>> GetPrescriptionsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        authorizationService.RequireSignedIn();
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var entities = await context.Prescriptions
            .AsNoTracking()
            .OrderByDescending(item => item.IsBuiltin)
            .ThenBy(item => item.CreatedAtUnixMs)
            .ThenBy(item => item.Id)
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = entities.Count > request.SafePageSize;
        if (hasMore)
        {
            entities.RemoveAt(entities.Count - 1);
        }

        return new PageResult<PrescriptionDefinition>(entities.Select(ToDefinition).ToList(), hasMore);
    }

    public Task<PrescriptionDefinition> CreateDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        authorizationService.RequireSignedIn();
        return Task.FromResult(new PrescriptionDefinition(
            $"PROT_{Guid.NewGuid():N}",
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            0,
            null,
            null,
            string.Empty,
            0,
            0,
            string.Empty,
            false));
    }

    public async Task SaveAsync(
        PrescriptionDefinition prescription,
        CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Prescriptions
                .FirstOrDefaultAsync(item => item.Id == prescription.Id, cancellationToken);
            var isNew = entity is null;
            var currentUser = authorizationService.RequireSignedIn();
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (entity is null)
            {
                entity = new PrescriptionEntity
                {
                    Id = prescription.Id,
                    CreatedByUserId = currentUser.UserId,
                    CreatedAtUnixMs = now
                };
                context.Prescriptions.Add(entity);
            }
            else
            {
            }

            Apply(entity, prescription);
            entity.UpdatedByUserId = currentUser.UserId;
            entity.UpdatedAtUnixMs = now;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException("处方保存失败：业务数据库写入失败。", exception);
            }

            await auditTrail.AppendAsync(
                new AuditEventInput(
                    AuditEventCategory.PrescriptionManagement,
                    isNew ? "CREATE_PRESCRIPTION" : "UPDATE_PRESCRIPTION",
                    AuditActor.From(accountService.CurrentUser),
                    "Prescription",
                    prescription.Id,
                    AuditEventResult.Success),
                cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<PrescriptionDefinition> CopyAsync(string prescriptionId, CancellationToken cancellationToken = default)
    {
        authorizationService.RequireSignedIn();
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var source = await context.Prescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == prescriptionId, cancellationToken)
                ?? throw new InvalidOperationException("找不到要复制的处方。");
            var currentUser = authorizationService.RequireSignedIn();
            var normalizedSourceName = PrescriptionDefinition.NormalizeName(source.Name, source.StimulationType);
            var baseName = Regex.Replace(normalizedSourceName.TrimEnd(), @"-\d+$", string.Empty);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = source.Name.Trim();
            var existingNames = await context.Prescriptions
                .AsNoTracking()
                .Where(item => item.StimulationType == source.StimulationType && item.Name.StartsWith(baseName))
                .Select(item => item.Name)
                .ToListAsync(cancellationToken);
            existingNames = existingNames
                .Select(item => PrescriptionDefinition.NormalizeName(item, source.StimulationType))
                .ToList();
            var suffix = 1;
            string copyName;
            do
            {
                copyName = $"{baseName}-{suffix++}";
            }
            while (existingNames.Contains(copyName, StringComparer.OrdinalIgnoreCase));

            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var copy = new PrescriptionEntity
            {
                Id = $"PROT_{Guid.NewGuid():N}",
                Name = copyName,
                Indication = source.Indication,
                StimulationType = source.StimulationType,
                CurrentMilliamp = source.CurrentMilliamp,
                DeliveryMode = source.DeliveryMode,
                TotalDurationMinutes = source.TotalDurationMinutes,
                IntervalMinutes = source.IntervalMinutes,
                SessionDurationMinutes = source.SessionDurationMinutes,
                Course = source.Course,
                RampUpSeconds = source.RampUpSeconds,
                RampDownSeconds = source.RampDownSeconds,
                EvidenceGrade = source.EvidenceGrade,
                IsBuiltin = false,
                CreatedByUserId = currentUser.UserId,
                UpdatedByUserId = currentUser.UserId,
                CreatedAtUnixMs = now,
                UpdatedAtUnixMs = now
            };
            context.Prescriptions.Add(copy);
            await context.SaveChangesAsync(cancellationToken);
            await auditTrail.AppendAsync(
                new AuditEventInput(
                    AuditEventCategory.PrescriptionManagement,
                    "COPY_PRESCRIPTION",
                    AuditActor.From(accountService.CurrentUser),
                    "Prescription",
                    copy.Id,
                    AuditEventResult.Success,
                    Reason: $"source={prescriptionId}"),
                cancellationToken);
            return ToDefinition(copy);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task DeleteAsync(string prescriptionId, CancellationToken cancellationToken = default)
    {
        authorizationService.Demand(AppPermission.DeletePrescription);
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Prescriptions
                .FirstOrDefaultAsync(item => item.Id == prescriptionId, cancellationToken)
                ?? throw new InvalidOperationException("找不到要删除的处方。");
            context.Prescriptions.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            await auditTrail.AppendAsync(
                new AuditEventInput(
                    AuditEventCategory.PrescriptionManagement,
                    "DELETE_PRESCRIPTION",
                    AuditActor.From(accountService.CurrentUser),
                    "Prescription",
                    prescriptionId,
                    AuditEventResult.Success),
                cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static PrescriptionDefinition ToDefinition(PrescriptionEntity entity) => new(
        entity.Id,
        PrescriptionDefinition.NormalizeName(entity.Name, entity.StimulationType),
        entity.Indication,
        entity.StimulationType,
        entity.CurrentMilliamp,
        entity.DeliveryMode,
        entity.TotalDurationMinutes,
        entity.IntervalMinutes,
        entity.SessionDurationMinutes,
        entity.Course,
        entity.RampUpSeconds,
        entity.RampDownSeconds,
        entity.EvidenceGrade,
        entity.IsBuiltin);

    private static void Apply(PrescriptionEntity entity, PrescriptionDefinition prescription)
    {
        entity.Name = PrescriptionDefinition.NormalizeName(prescription.Name, prescription.StimulationType);
        entity.Indication = prescription.Indication;
        entity.StimulationType = prescription.StimulationType;
        entity.CurrentMilliamp = prescription.CurrentMilliamp;
        entity.DeliveryMode = prescription.DeliveryMode;
        entity.TotalDurationMinutes = prescription.TotalDurationMinutes;
        entity.IntervalMinutes = prescription.IntervalMinutes;
        entity.SessionDurationMinutes = prescription.SessionDurationMinutes;
        entity.Course = prescription.Course;
        entity.RampUpSeconds = prescription.RampUpSeconds;
        entity.RampDownSeconds = prescription.RampDownSeconds;
        entity.EvidenceGrade = prescription.EvidenceGrade;
        entity.IsBuiltin = prescription.IsBuiltin;
    }
}
