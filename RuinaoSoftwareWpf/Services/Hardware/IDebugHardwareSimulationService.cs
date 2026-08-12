namespace RuinaoSoftwareWpf;

public interface IDebugHardwareSimulationService
{
    event EventHandler? ConnectionChanged;

    bool IsAvailable { get; }

    bool IsConnected { get; }

    DebugHardwareSimulationResult Connect(bool realHardwareConnected);
}
