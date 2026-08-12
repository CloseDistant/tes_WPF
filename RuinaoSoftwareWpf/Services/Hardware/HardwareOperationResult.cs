namespace RuinaoSoftwareWpf;

/// <summary>
/// 硬件操作结果。
/// </summary>
public sealed record HardwareOperationResult(
    bool IsConnected,
    string FooterStatus,
    string? UserMessage = null);
