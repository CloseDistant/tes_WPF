namespace RuinaoTesHardware;

public sealed record UsbBackplaneDevice(
    string InstanceId,
    string Description,
    string? Manufacturer,
    string? DriverService,
    uint ProblemCode,
    bool IsPresent)
{
    public bool DriverReady => IsPresent && ProblemCode == 0 && !string.IsNullOrWhiteSpace(DriverService);

    public string DriverStatus => DriverReady
        ? $"驱动已就绪（{DriverService}）"
        : ProblemCode == 28
            ? "驱动未安装（Windows问题代码28）"
            : $"驱动异常（Windows问题代码{ProblemCode}）";
}
