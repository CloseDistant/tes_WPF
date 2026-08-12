namespace RuinaoSoftwareWpf;

public sealed record HardwareConnectionChangedEventArgs(
    bool IsConnected,
    bool IsConnecting,
    HardwareConnectionChangeReason Reason,
    string Message);
