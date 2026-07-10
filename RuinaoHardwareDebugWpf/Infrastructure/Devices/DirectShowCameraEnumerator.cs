namespace RuinaoHardwareDebugWpf;

using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;
using System.Management;

internal static class DirectShowCameraEnumerator
{
    private static readonly Guid SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
    private static readonly Guid IPropertyBagId = new("55272A00-42CB-11CE-8135-00AA004BB851");

    public static IReadOnlyList<string> GetVideoInputDeviceNames()
    {
        var names = new List<string>();
        ICreateDevEnum? deviceEnum = null;
        IEnumMoniker? enumMoniker = null;

        try
        {
            var deviceEnumType = Type.GetTypeFromCLSID(SystemDeviceEnum);
            if (deviceEnumType is null)
            {
                return names;
            }

            deviceEnum = (ICreateDevEnum?)Activator.CreateInstance(deviceEnumType);
            if (deviceEnum is null)
            {
                return names;
            }

            var videoInputDeviceCategory = VideoInputDeviceCategory;
            deviceEnum.CreateClassEnumerator(ref videoInputDeviceCategory, out enumMoniker, 0);
            if (enumMoniker is null)
            {
                return names;
            }

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                try
                {
                    var propertyBagId = IPropertyBagId;
                    moniker.BindToStorage(null!, null, ref propertyBagId, out var bagObject);
                    try
                    {
                        if (bagObject is IPropertyBag propertyBag)
                        {
                            propertyBag.Read("FriendlyName", out var value, IntPtr.Zero);
                            if (value is string name && !string.IsNullOrWhiteSpace(name))
                            {
                                names.Add(name);
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(bagObject);
                    }
                }
                finally
                {
                    ReleaseComObject(moniker);
                }
            }
        }
        catch
        {
            names.Clear();
        }
        finally
        {
            ReleaseComObject(enumMoniker);
            ReleaseComObject(deviceEnum);
        }

        if (names.Count == 0)
        {
            names.AddRange(GetWmiCameraNames());
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static IReadOnlyList<string> GetWmiCameraNames()
    {
        var names = new List<string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");

            foreach (var device in searcher.Get().OfType<ManagementObject>())
            {
                if (device["Name"] is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            return names;
        }

        return names;
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid classType, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propertyName, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propertyName, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
