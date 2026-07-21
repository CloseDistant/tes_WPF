namespace RuinaoSoftwareWpf;

/// <summary>
/// 空实现：功能尚未开发时，先用这些类占坑，保证 DI 容器能正常组装。
///
/// 它们什么都不做，等后续模块开发时，再替换为真正的实现类。
/// </summary>

public sealed class NullDataProcessingService : IDataProcessingService
{
}

public sealed class NullPatientService : IPatientService
{
    public event EventHandler? CurrentPatientChanged
    {
        add { }
        remove { }
    }

    public PatientRecord? CurrentPatient => null;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"P{DateTime.Now:yyyyMMdd}0001");
    }

    public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<PageResult<PatientRecord>> GetPatientsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PageResult<PatientRecord>(Array.Empty<PatientRecord>(), false));
    }

    public Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("请先新增或选择患者，再开始采集。");
    }

}

public sealed class NullStimulationRecordService : IStimulationRecordService
{
    public Task RecordAsync(StimulationRecordRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PageResult<StimulationTreatmentRecord>(Array.Empty<StimulationTreatmentRecord>(), false));
}

public sealed class NullConfigService : IConfigService
{
}

public sealed class NullReportService : IReportService
{
}
