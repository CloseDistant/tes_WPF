namespace RuinaoTesHardware;

public static class TesUsbIdentity
{
    public const ushort VendorId = 0x04B4;
    public const ushort ProductId = 0x00F1;
    public const string HardwareIdFragment = "VID_04B4&PID_00F1";
    public const string DriverService = "libusbK";

    // 仅供保留的旧WinUSB传输实现使用；当前工程师软件使用libusbK并按VID/PID枚举。
    public static readonly Guid LegacyWinUsbInterfaceGuid = new("7DA6A8D1-7A5E-4AB8-92AF-2E166A0D31C1");
}
