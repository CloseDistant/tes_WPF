using System.Runtime.InteropServices;
using System.Text;

namespace RuinaoTesHardware;

/// <summary>
/// 使用Windows SetupAPI发现设备；即使驱动缺失，也能报告VID/PID和问题代码。
/// </summary>
public sealed class WindowsUsbBackplaneDiscovery : IUsbBackplaneDiscovery
{
    public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FindCore());
    }

    private static UsbBackplaneDevice? FindCore()
    {
        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new BackplaneConnectionException($"SetupDiGetClassDevs失败，Win32={Marshal.GetLastWin32Error()}。");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var deviceInfo = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        return null;
                    }

                    throw new BackplaneConnectionException($"SetupDiEnumDeviceInfo失败，Win32={error}。");
                }

                var instanceId = GetInstanceId(deviceInfoSet, ref deviceInfo);
                if (!instanceId.Contains(TesUsbIdentity.HardwareIdFragment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _ = CmGetDevNodeStatus(out _, out var problemCode, deviceInfo.DevInst, 0);
                return new UsbBackplaneDevice(
                    instanceId,
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpDeviceDesc) ?? "tES",
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpMfg),
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpService),
                    problemCode,
                    true);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string GetInstanceId(IntPtr set, ref SpDevinfoData data)
    {
        var buffer = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceId(set, ref data, buffer, buffer.Capacity, out _))
        {
            throw new BackplaneConnectionException($"读取USB InstanceId失败，Win32={Marshal.GetLastWin32Error()}。");
        }

        return buffer.ToString();
    }

    private static string? GetRegistryString(IntPtr set, ref SpDevinfoData data, uint property)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, buffer, (uint)buffer.Length, out _))
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(buffer);
        var terminator = value.IndexOf('\0');
        return terminator >= 0 ? value[..terminator] : value;
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const int ErrorNoMoreItems = 259;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpMfg = 0x0000000B;
    private const uint SpdrpService = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        uint flags);

    private static uint CmGetDevNodeStatus(out uint status, out uint problemNumber, uint devInst, uint flags)
        => CM_Get_DevNode_Status(out status, out problemNumber, devInst, flags);
}
