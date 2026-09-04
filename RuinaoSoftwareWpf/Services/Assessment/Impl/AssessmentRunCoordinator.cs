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
        IReadOnlyList<AssessmentFlowModuleDefinition> moduleFlow,
        CancellationToken cancellationToken = default)
    {
        ValidateModuleFlow(moduleFlow);
        var patientCode = GetCurrentPatientCode();
        return store.GetActiveRunAsync(patientCode, moduleFlow, cancellationToken);
    }

    public Task<AssessmentRunContext> CreateRunAsync(
        IReadOnlyList<AssessmentFlowModuleDefinition> moduleFlow,
        CancellationToken cancellationToken = default)
    {
        ValidateModuleFlow(moduleFlow);
        var patientCode = GetCurrentPatientCode();
        return store.CreateRunAsync(
            patientCode,
            moduleFlow,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<AssessmentRunContext> ResumeRunAsync(
        long runId,
        IReadOnlyList<AssessmentFlowModuleDefinition> moduleFlow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runId);
        ValidateModuleFlow(moduleFlow);
        var patientCode = GetCurrentPatientCode();
        return store.ResumeRunAsync(
            runId,
            patientCode,
            moduleFlow,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void ValidateModuleFlow(IReadOnlyList<AssessmentFlowModuleDefinition> moduleFlow)
    {
        ArgumentNullException.ThrowIfNull(moduleFlow);
        if (moduleFlow.Count == 0)
        {
            throw new ArgumentException("正式评估流程至少需要一个模块。", nameof(moduleFlow));
        }

        if (moduleFlow.Any(static module => module.ModuleTypeId <= 0
                || string.IsNullOrWhiteSpace(module.ModuleCode))
            || moduleFlow.Select(static module => module.ModuleTypeId).Distinct().Count() != moduleFlow.Count
            || moduleFlow.Select(static module => module.ModuleCode).Distinct(StringComparer.Ordinal).Count() != moduleFlow.Count)
        {
            throw new ArgumentException("正式评估流程包含无效或重复的模块身份。", nameof(moduleFlow));
        }
    }

    private string GetCurrentPatientCode()
    {
        return patientService.CurrentPatient?.PatientCode
            ?? throw new InvalidOperationException("请先新增或选择患者，再开始数字表型采集。");
    }
}
