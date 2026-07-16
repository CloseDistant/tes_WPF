using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using RuinaoTesProtocol.V13;

namespace RuinaoTesHardware;

/// <summary>
/// 直接调用Windows WinUSB API，不依赖厂商SDK。
/// 驱动安装后自动发现第一个Bulk OUT和Bulk IN端点。
/// </summary>
public sealed class WinUsbBackplaneTransport : IBackplaneTransport
{
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private SafeFileHandle? deviceHandle;
    private IntPtr winUsbHandle;
    private byte bulkInPipe;
    private byte bulkOutPipe;
    private uint timeoutMilliseconds;

    public bool IsOpen => deviceHandle is { IsInvalid: false, IsClosed: false } && winUsbHandle != IntPtr.Zero;

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

        await Task.Run(() => OpenCore(timeout), cancellationToken);
    }

    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            throw new BackplaneConnectionException("WinUSB链路尚未打开。");
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
        var devicePath = FindDeviceInterfacePath();
        var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new BackplaneConnectionException($"打开tES WinUSB设备失败：{new Win32Exception(error).Message}（{error}）。");
        }

        if (!WinUsbInitialize(handle, out var interfaceHandle))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new BackplaneConnectionException($"WinUsb_Initialize失败：{new Win32Exception(error).Message}（{error}）。");
        }

        try
        {
            if (!WinUsbQueryInterfaceSettings(interfaceHandle, 0, out var descriptor))
            {
                ThrowLastWin32("WinUsb_QueryInterfaceSettings");
            }

            byte foundIn = 0;
            byte foundOut = 0;
            for (byte index = 0; index < descriptor.NumberOfEndpoints; index++)
            {
                if (!WinUsbQueryPipe(interfaceHandle, 0, index, out var pipe))
                {
                    ThrowLastWin32("WinUsb_QueryPipe");
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
                    $"设备未暴露可用的Bulk IN/OUT端点（端点数={descriptor.NumberOfEndpoints}）。请硬件方确认USB固件描述符。");
            }

            timeoutMilliseconds = checked((uint)Math.Clamp(timeout.TotalMilliseconds, 100, 60_000));
            SetPipeTimeout(interfaceHandle, foundIn, timeoutMilliseconds);
            SetPipeTimeout(interfaceHandle, foundOut, timeoutMilliseconds);

            deviceHandle = handle;
            winUsbHandle = interfaceHandle;
            bulkInPipe = foundIn;
            bulkOutPipe = foundOut;
        }
        catch
        {
            WinUsbFree(interfaceHandle);
            handle.Dispose();
            throw;
        }
    }

    private byte[] ExchangeCore(byte[] request)
    {
        if (!WinUsbWritePipe(winUsbHandle, bulkOutPipe, request, (uint)request.Length, out var written, IntPtr.Zero))
        {
            ThrowLastWin32("WinUsb_WritePipe");
        }

        if (written != request.Length)
        {
            throw new BackplaneConnectionException($"USB写入不完整：expected={request.Length}, actual={written}。");
        }

        using var response = new MemoryStream();
        var buffer = new byte[4096];
        int? expectedLength = null;

        while (!expectedLength.HasValue || response.Length < expectedLength.Value)
        {
            if (!WinUsbReadPipe(winUsbHandle, bulkInPipe, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorSemTimeout)
                {
                    throw new TimeoutException($"等待硬件回复超时（{timeoutMilliseconds}ms）。");
                }

                throw new BackplaneConnectionException(
                    $"WinUsb_ReadPipe失败：{new Win32Exception(error).Message}（{error}）。");
            }

            if (read == 0)
            {
                throw new BackplaneConnectionException("硬件返回了0字节数据。");
            }

            response.Write(buffer, 0, checked((int)read));
            if (!expectedLength.HasValue && response.Length >= TesV13ProtocolConstants.HeaderLength)
            {
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

        return bytes;
    }

    private void CloseCore()
    {
        if (winUsbHandle != IntPtr.Zero)
        {
            WinUsbFree(winUsbHandle);
            winUsbHandle = IntPtr.Zero;
        }

        deviceHandle?.Dispose();
        deviceHandle = null;
        bulkInPipe = 0;
        bulkOutPipe = 0;
    }

    private static string FindDeviceInterfacePath()
    {
        var interfaceGuid = TesUsbIdentity.LegacyWinUsbInterfaceGuid;
        var set = SetupDiGetClassDevs(ref interfaceGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandleValue)
        {
            ThrowLastWin32("SetupDiGetClassDevs(DeviceInterface)");
        }

        try
        {
            var interfaceData = new SpDeviceInterfaceData
            {
                CbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>(),
            };
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref interfaceGuid, 0, ref interfaceData))
            {
                throw new BackplaneConnectionException(
                    "未找到tES WinUSB设备接口。请确认驱动INF中的DeviceInterfaceGUID与软件一致，并重新插拔设备。");
            }

            _ = SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
            var detailBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
            try
            {
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                {
                    ThrowLastWin32("SetupDiGetDeviceInterfaceDetail");
                }

                return Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4))
                    ?? throw new BackplaneConnectionException("无法读取tES设备接口路径。");
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static void SetPipeTimeout(IntPtr interfaceHandle, byte pipeId, uint timeout)
    {
        if (!WinUsbSetPipePolicy(interfaceHandle, pipeId, PipeTransferTimeout, sizeof(uint), ref timeout))
        {
            ThrowLastWin32("WinUsb_SetPipePolicy(PIPE_TRANSFER_TIMEOUT)");
        }
    }

    private static void ThrowLastWin32(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        throw new BackplaneConnectionException($"{operation}失败：{new Win32Exception(error).Message}（{error}）。");
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const byte UsbEndpointDirectionIn = 0x80;
    private const uint PipeTransferTimeout = 3;
    private const int ErrorSemTimeout = 121;

    private enum UsbdPipeType
    {
        Control,
        Isochronous,
        Bulk,
        Interrupt,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public ushort BcdUsb;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    private static bool WinUsbInitialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle)
        => WinUsb_Initialize(deviceHandle, out interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    private static bool WinUsbFree(IntPtr interfaceHandle) => WinUsb_Free(interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    private static bool WinUsbQueryInterfaceSettings(IntPtr handle, byte alternate, out UsbInterfaceDescriptor descriptor)
        => WinUsb_QueryInterfaceSettings(handle, alternate, out descriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    private static bool WinUsbQueryPipe(IntPtr handle, byte alternate, byte index, out WinUsbPipeInformation pipe)
        => WinUsb_QueryPipe(handle, alternate, index, out pipe);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);

    private static bool WinUsbSetPipePolicy(IntPtr handle, byte pipe, uint policy, uint length, ref uint value)
        => WinUsb_SetPipePolicy(handle, pipe, policy, length, ref value);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    private static bool WinUsbWritePipe(IntPtr handle, byte pipe, byte[] buffer, uint length, out uint transferred, IntPtr overlapped)
        => WinUsb_WritePipe(handle, pipe, buffer, length, out transferred, overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    private static bool WinUsbReadPipe(IntPtr handle, byte pipe, byte[] buffer, uint length, out uint transferred, IntPtr overlapped)
        => WinUsb_ReadPipe(handle, pipe, buffer, length, out transferred, overlapped);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
