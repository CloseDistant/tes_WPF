namespace RuinaoSoftwareWpf;

public interface IStimulationRecordService
{
    Task<string> StartRunAsync(
        StimulationRunStartRequest request,
        CancellationToken cancellationToken = default);

    Task EndChannelsAsync(
        StimulationChannelsEndRequest request,
        CancellationToken cancellationToken = default);

    Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);
}
