namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 将患者级 Run 解析集中在评估入口，模块生命周期只接收明确 RunId。
/// </summary>
internal sealed class AssessmentRunCoordinator(
    IAssessmentRunStore store,
    IPatientService patientService,
    TimeProvider timeProvider) : IAssessmentRunCoordinator
{
    public Task<AssessmentRunContext?> GetActiveRunAsync(
        int totalModuleCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalModuleCount);
        var patientCode = GetCurrentPatientCode();
        return store.GetActiveRunAsync(patientCode, totalModuleCount, cancellationToken);
    }

    public Task<AssessmentRunContext> CreateRunAsync(
        int totalModuleCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalModuleCount);
        var patientCode = GetCurrentPatientCode();
        return store.CreateRunAsync(
            patientCode,
            totalModuleCount,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<AssessmentRunContext> ResumeRunAsync(
        long runId,
        int totalModuleCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalModuleCount);
        var patientCode = GetCurrentPatientCode();
        return store.ResumeRunAsync(
            runId,
            patientCode,
            totalModuleCount,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private string GetCurrentPatientCode()
    {
        return patientService.CurrentPatient?.PatientCode
            ?? throw new InvalidOperationException("请先新增或选择患者，再开始数字表型采集。");
    }
}
