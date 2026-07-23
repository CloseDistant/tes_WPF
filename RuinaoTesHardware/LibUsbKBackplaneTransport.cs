using System.ComponentModel;
using System.Runtime.InteropServices;
using RuinaoTesProtocol.V13;

namespace RuinaoTesHardware;

/// <summary>
/// 通过libusbK.dll访问已绑定libusbK.sys的tES设备。
/// 按VID/PID打开设备，并自动选择第一个Bulk OUT和Bulk IN端点。
/// </summary>
public sealed class LibUsbKBackplaneTransport : IBackplaneTransport, IBackplaneTransferDiagnostics
{
    // 保证同一个USB句柄同一时间只进行一次“写请求+读回复”，避免两条命令的回复互相串帧。
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private IntPtr usbHandle;
    private byte bulkInPipe;
    private byte bulkOutPipe;
    private uint timeoutMilliseconds;
    private byte[]? lastWrittenFrame;

    public bool IsOpen => usbHandle != IntPtr.Zero;

    // 返回副本，避免上层修改内部保存的原始发送帧。
    public byte[]? LastWrittenFrame => lastWrittenFrame?.ToArray();

    public event EventHandler<UsbWriteCompletedEventArgs>? WriteCompleted;
    public event EventHandler<UsbFrameReceivedEventArgs>? FrameReceived;

    public UsbTransportDiagnosticSnapshot GetSnapshot() => new(
        IsOpen,
        IsOpen,
        bulkOutPipe,
        bulkInPipe,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        0,
        null,
        null);

    public async Task OpenAsync(
        UsbBackplaneDevice device,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
        {
            return;
        }

        if (!device.DriverReady)
        {
            throw new BackplaneConnectionException(device.DriverStatus);
        }

        if (!string.Equals(device.DriverService, TesUsbIdentity.DriverService, StringComparison.OrdinalIgnoreCase))
        {
            throw new BackplaneConnectionException(
                $"tES当前绑定的驱动服务是{device.DriverService ?? "未知"}，需要绑定{TesUsbIdentity.DriverService}驱动。");
        }

        try
        {
            await Task.Run(() => OpenCore(timeout), cancellationToken);
        }
        catch (DllNotFoundException exception)
        {
            throw new BackplaneConnectionException(
                "未找到libusbK.dll。请安装tES libusbK驱动包，或将匹配程序位数的libusbK.dll放到程序目录。",
                exception);
        }
        catch (BadImageFormatException exception)
        {
            throw new BackplaneConnectionException(
                "libusbK.dll位数与工程师软件不匹配；当前软件需要64位libusbK.dll。",
                exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new BackplaneConnectionException("libusbK.dll版本不兼容，缺少必要的USB接口函数。", exception);
        }
    }

    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            throw new BackplaneConnectionException("libusbK链路尚未打开。");
        }

        await exchangeGate.WaitAsync(cancellationToken);
        try
        {
            var requestBytes = request.ToArray();
            return await Task.Run(() => ExchangeCore(requestBytes), cancellationToken);
        }
        finally
        {
            exchangeGate.Release();
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseCore();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        exchangeGate.Dispose();
    }

    private void OpenCore(TimeSpan timeout)
    {
        // libusbK先创建当前USB设备列表，再按固定VID/PID 04B4:00F1查找tES背板。
        if (!LstKInit(out var deviceList, 0))
        {
            ThrowLastWin32("LstK_Init");
        }

        IntPtr openedHandle = IntPtr.Zero;
        try
        {
            if (!LstKFindByVidPid(deviceList, TesUsbIdentity.VendorId, TesUsbIdentity.ProductId, out var deviceInfo))
            {
                var error = Marshal.GetLastWin32Error();
                throw new BackplaneConnectionException(
                    error == ErrorNoMoreItems
                        ? $"libusbK未发现tES设备（VID_{TesUsbIdentity.VendorId:X4}&PID_{TesUsbIdentity.ProductId:X4}）。"
                        : $"LstK_FindByVidPid失败：{new Win32Exception(error).Message}（{error}）。");
            }

            if (!UsbKInit(out openedHandle, deviceInfo))
            {
                ThrowLastWin32("UsbK_Init");
            }
        }
        finally
        {
            LstKFree(deviceList);
        }

        try
        {
            if (!UsbKQueryInterfaceSettings(openedHandle, 0, out var descriptor))
            {
                ThrowLastWin32("UsbK_QueryInterfaceSettings");
            }

            // 枚举接口0的所有端点，自动寻找第一个Bulk IN和第一个Bulk OUT。
            // 当前实物枚举结果是OUT=0x01、IN=0x81，但代码不把端点号写死。
            byte foundIn = 0;
            byte foundOut = 0;
            for (byte index = 0; index < descriptor.NumberOfEndpoints; index++)
            {
                if (!UsbKQueryPipe(openedHandle, descriptor.AlternateSetting, index, out var pipe))
                {
                    ThrowLastWin32("UsbK_QueryPipe");
                }

                if (pipe.PipeType != UsbdPipeType.Bulk)
                {
                    continue;
                }

                if ((pipe.PipeId & UsbEndpointDirectionIn) != 0 && foundIn == 0)
                {
                    foundIn = pipe.PipeId;
                }
                else if ((pipe.PipeId & UsbEndpointDirectionIn) == 0 && foundOut == 0)
                {
                    foundOut = pipe.PipeId;
                }
            }

            if (foundIn == 0 || foundOut == 0)
            {
                throw new BackplaneConnectionException(
                    $"tES设备未暴露可用的Bulk IN/OUT端点（端点数={descriptor.NumberOfEndpoints}）。请硬件方确认USB固件描述符。");
            }

            timeoutMilliseconds = checked((uint)Math.Clamp(timeout.TotalMilliseconds, 100, 60_000));
            SetPipeTimeout(openedHandle, foundIn, timeoutMilliseconds);
            SetPipeTimeout(openedHandle, foundOut, timeoutMilliseconds);

            usbHandle = openedHandle;
            bulkInPipe = foundIn;
            bulkOutPipe = foundOut;
        }
        catch
        {
            UsbKFree(openedHandle);
            throw;
        }
    }

    private byte[] ExchangeCore(byte[] request)
    {
        // 一次协议交换的顺序固定为：Bulk OUT完整写入请求 -> Bulk IN读取完整回复。
        if (!UsbKWritePipe(usbHandle, bulkOutPipe, request, (uint)request.Length, out var written, IntPtr.Zero))
        {
            ThrowLastWin32("UsbK_WritePipe");
        }

        if (written != request.Length)
        {
            throw new BackplaneConnectionException($"USB写入不完整：expected={request.Length}, actual={written}。");
        }

        // 只有UsbK_WritePipe返回成功且实际写入长度完全一致，才记录为TX_OK。
        // 保存byte[]副本，之后即使读取回复超时，工程师仍能看到硬件端点实际接收了哪一帧。
        lastWrittenFrame = request.ToArray();
        WriteCompleted?.Invoke(
            this,
            new UsbWriteCompletedEventArgs(DateTimeOffset.Now, lastWrittenFrame.ToArray(), checked((int)written)));

        using var response = new MemoryStream();
        var buffer = new byte[4096];
        int? expectedLength = null;

        // USB一次ReadPipe不保证返回完整协议帧，因此可能需要循环读取。
        while (!expectedLength.HasValue || response.Length < expectedLength.Value)
        {
            if (!UsbKReadPipe(usbHandle, bulkInPipe, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorSemTimeout)
                {
                    throw new TimeoutException($"等待硬件回复超时（{timeoutMilliseconds}ms）。");
                }

                throw new BackplaneConnectionException(
                    $"UsbK_ReadPipe失败：{new Win32Exception(error).Message}（{error}）。");
            }

            if (read == 0)
            {
                throw new BackplaneConnectionException("硬件返回了0字节数据。");
            }

            response.Write(buffer, 0, checked((int)read));
            if (!expectedLength.HasValue && response.Length >= TesV13ProtocolConstants.HeaderLength)
            {
                // 收满18字节帧头后才能知道数据体长度，从而计算整帧应有的字节数。
                expectedLength = TesV13ProtocolCodec.GetExpectedFrameLength(response.GetBuffer());
            }

            if (response.Length > ushort.MaxValue + TesV13ProtocolConstants.MinimumFrameLength)
            {
                throw new BackplaneConnectionException("硬件回复超过协议允许的最大帧长度。");
            }
        }

        var bytes = response.ToArray();
        if (bytes.Length != expectedLength)
        {
            throw new BackplaneConnectionException($"一次USB读取包含额外数据：expected={expectedLength}, actual={bytes.Length}。");
        }

        if (TesV13ProtocolCodec.TryParseFrame(bytes, out var frame, out _) && frame is not null)
        {
            FrameReceived?.Invoke(
                this,
                new UsbFrameReceivedEventArgs(
                    DateTimeOffset.Now,
                    bytes.ToArray(),
                    frame.SendSequence,
                    frame.AckSequence,
                    true));
        }

        return bytes;
    }

    private void CloseCore()
    {
        if (usbHandle != IntPtr.Zero)
        {
            UsbKFree(usbHandle);
            usbHandle = IntPtr.Zero;
        }

        bulkInPipe = 0;
        bulkOutPipe = 0;
    }

    private static void SetPipeTimeout(IntPtr handle, byte pipeId, uint timeout)
    {
        if (!UsbKSetPipePolicy(handle, pipeId, PipeTransferTimeout, sizeof(uint), ref timeout))
        {
            ThrowLastWin32("UsbK_SetPipePolicy(PIPE_TRANSFER_TIMEOUT)");
        }
    }

    private static void ThrowLastWin32(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        throw new BackplaneConnectionException($"{operation}失败：{new Win32Exception(error).Message}（{error}）。");
    }

    private const string LibUsbKLibrary = "libusbK.dll";
    private const int ErrorNoMoreItems = 259;
    private const int ErrorSemTimeout = 121;
    private const byte UsbEndpointDirectionIn = 0x80;
    private const uint PipeTransferTimeout = 3;

    private enum UsbdPipeType
    {
        Control,
        Isochronous,
        Bulk,
        Interrupt,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumberOfEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte InterfaceIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public UsbdPipeType PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [DllImport(LibUsbKLibrary, EntryPoint = "LstK_Init", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LstKInit(out IntPtr deviceList, int flags);

    [DllImport(LibUsbKLibrary, EntryPoint = "LstK_Free", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LstKFree(IntPtr deviceList);

    [DllImport(LibUsbKLibrary, EntryPoint = "LstK_FindByVidPid", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LstKFindByVidPid(IntPtr deviceList, int vendorId, int productId, out IntPtr deviceInfo);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_Init", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKInit(out IntPtr interfaceHandle, IntPtr deviceInfo);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_Free", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKFree(IntPtr interfaceHandle);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_QueryInterfaceSettings", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKQueryInterfaceSettings(
        IntPtr interfaceHandle,
        byte alternateSettingIndex,
        out UsbInterfaceDescriptor descriptor);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_QueryPipe", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKQueryPipe(
        IntPtr interfaceHandle,
        byte alternateSettingNumber,
        byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_SetPipePolicy", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKSetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_WritePipe", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKWritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport(LibUsbKLibrary, EntryPoint = "UsbK_ReadPipe", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UsbKReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);
}
