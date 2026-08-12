namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record StimulationCommandResult(
    StimulationCommandStatus Status,
    StimulationDeviceSnapshot Device,
    string? ErrorCode = null,
    string? Message = null)
{
    public bool Succeeded => Status is StimulationCommandStatus.Accepted or StimulationCommandStatus.Confirmed;
}
