namespace RuinaoSoftwareWpf;

/// <summary>
/// 患者服务接口。
/// </summary>
public interface IPatientService
{
    event EventHandler? CurrentPatientChanged;

    PatientRecord? CurrentPatient { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default);

    Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default);

    Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default);

    Task<PageResult<PatientRecord>> GetPatientsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);

    Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default);

    Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default);
}
