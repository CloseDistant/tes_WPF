using RuinaoTesHardware;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class UsbTransportErrorFormatterTests
{
    [Fact]
    public void Format_WritePipeSemaphoreTimeout_UsesUsbWriteDescription()
    {
        var message = UsbTransportErrorFormatter.Format(
            "libusbK WinUsb_WritePipe(0x01)",
            UsbTransportErrorFormatter.ErrorSemTimeout);

        Assert.Equal(
            "libusbK WinUsb_WritePipe(0x01)失败：USB写入超时（Windows错误121，信号量超时）。",
            message);
        Assert.DoesNotContain("信号灯", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReadPipeSemaphoreTimeout_UsesUsbReadDescription()
    {
        var message = UsbTransportErrorFormatter.Format(
            "WinUsb_ReadPipe",
            UsbTransportErrorFormatter.ErrorSemTimeout);

        Assert.Contains("USB读取超时", message, StringComparison.Ordinal);
        Assert.Contains("信号量超时", message, StringComparison.Ordinal);
    }
}
