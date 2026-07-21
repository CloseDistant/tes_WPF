namespace RuinaoSoftwareWpf;

public sealed record DebugHardwareSimulationResult(bool Succeeded, string Message);

public interface IDebugHardwareSimulationService
{
    event EventHandler? ConnectionChanged;

    bool IsAvailable { get; }

    bool IsConnected { get; }

    DebugHardwareSimulationResult Connect(bool realHardwareConnected);
}
