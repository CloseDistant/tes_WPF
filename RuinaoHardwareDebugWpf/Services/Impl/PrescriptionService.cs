namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 处方服务内存实现。
/// 当前用于 P1 框架占位，后续可替换为 SQLite/JSON 持久化实现。
/// </summary>
public sealed class PrescriptionService : IPrescriptionService
{
    private readonly List<PrescriptionSummary> prescriptions = new();

    public Task<IReadOnlyList<PrescriptionSummary>> GetPrescriptionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PrescriptionSummary>>(prescriptions.ToList());
    }

    public Task<PrescriptionSummary> CreateDraftAsync(string mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prescription = new PrescriptionSummary(
            $"PROT_{DateTime.Now:yyyyMMddHHmmss}",
            $"Protocol {DateTime.Now:yyyyMMdd}",
            mode,
            false);

        prescriptions.Add(prescription);
        return Task.FromResult(prescription);
    }

    public Task SaveAsync(PrescriptionSummary prescription, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var index = prescriptions.FindIndex(item => item.Id == prescription.Id);
        if (index >= 0)
        {
            prescriptions[index] = prescription;
        }
        else
        {
            prescriptions.Add(prescription);
        }

        return Task.CompletedTask;
    }
}
