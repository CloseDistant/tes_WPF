namespace RuinaoTesHardware;

using System.ComponentModel;

/// <summary>把Windows/驱动错误转换为稳定、不会受系统本地化误导的USB通信描述。</summary>
internal static class UsbTransportErrorFormatter
{
    internal const int ErrorSemTimeout = 121;

    public static string Format(string operation, int error)
    {
        if (error == ErrorSemTimeout)
        {
            var stage = operation.Contains("WritePipe", StringComparison.OrdinalIgnoreCase)
                ? "USB写入"
                : operation.Contains("ReadPipe", StringComparison.OrdinalIgnoreCase)
                    ? "USB读取"
                    : "USB通信";
            return $"{operation}失败：{stage}超时（Windows错误121，信号量超时）。";
        }

        return $"{operation}失败：{new Win32Exception(error).Message}（{error}）。";
    }
}
