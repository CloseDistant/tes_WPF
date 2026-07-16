namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

/// <summary>数据库持久化的公用处方服务，不绑定患者或登录账号。</summary>
public sealed class PrescriptionService : IPrescriptionService
{
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public PrescriptionService(IAppDatabaseInitializer databaseInitializer)
    {
        this.databaseInitializer = databaseInitializer;
    }

    public async Task<IReadOnlyList<PrescriptionDefinition>> GetPrescriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var entities = await context.Prescriptions
            .AsNoTracking()
            .OrderByDescending(item => item.IsBuiltin)
            .ThenBy(item => item.CreatedAtUnixMs)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDefinition).ToList();
    }

    public Task<PrescriptionDefinition> CreateDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    public async Task SaveAsync(PrescriptionDefinition prescription, CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var entity = await context.Prescriptions
                .FirstOrDefaultAsync(item => item.Id == prescription.Id, cancellationToken);
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (entity is null)
            {
                entity = new PrescriptionEntity
                {
                    Id = prescription.Id,
                    CreatedAtUnixMs = now
                };
                context.Prescriptions.Add(entity);
            }

            Apply(entity, prescription);
            entity.UpdatedAtUnixMs = now;
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<PrescriptionDefinition> CopyAsync(string prescriptionId, CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var source = await context.Prescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == prescriptionId, cancellationToken)
                ?? throw new InvalidOperationException("找不到要复制的处方。");
            var existingItems = await context.Prescriptions
                .AsNoTracking()
                .Select(item => new { item.Name, item.StimulationType })
                .ToListAsync(cancellationToken);
            var existingNames = existingItems
                .Select(item => PrescriptionDefinition.NormalizeName(item.Name, item.StimulationType))
                .ToList();
            var normalizedSourceName = PrescriptionDefinition.NormalizeName(source.Name, source.StimulationType);
            var baseName = Regex.Replace(normalizedSourceName.TrimEnd(), @"-\d+$", string.Empty);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = source.Name.Trim();
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
                CreatedAtUnixMs = now,
                UpdatedAtUnixMs = now
            };
            context.Prescriptions.Add(copy);
            await context.SaveChangesAsync(cancellationToken);
            return ToDefinition(copy);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task DeleteAsync(string prescriptionId, CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var deleted = await context.Prescriptions
                .Where(item => item.Id == prescriptionId)
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0) throw new InvalidOperationException("找不到要删除的处方。");
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
