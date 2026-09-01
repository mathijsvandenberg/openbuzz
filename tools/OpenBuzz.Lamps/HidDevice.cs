using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenBuzz.Lamps;

/// <summary>
/// Just enough of the Windows HID API to find a device by its ids and send it
/// an output report.
///
/// Godot cannot write HID output reports, so lamp control has to live outside
/// the engine. This is the whole of the native part.
/// </summary>
public sealed class HidDevice : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x03;
    private const uint OpenExisting = 3;

    private const int DigcfPresent = 0x02;
    private const int DigcfDeviceInterface = 0x10;

    private static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    private readonly SafeFileHandle _handle;

    public string Path { get; }
    public int OutputReportLength { get; }

    private HidDevice(SafeFileHandle handle, string path, int outputLength)
    {
        _handle = handle;
        Path = path;
        OutputReportLength = outputLength;
    }

    /// Opens the first present device with these ids, or null.
    public static HidDevice? Open(ushort vendorId, ushort productId)
    {
        foreach (var path in EnumeratePaths())
        {
            var handle = CreateFile(path, GenericRead | GenericWrite, FileShareReadWrite,
                                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;

            var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
            if (!HidD_GetAttributes(handle, ref attributes) ||
                attributes.VendorID != vendorId || attributes.ProductID != productId)
            {
                handle.Dispose();
                continue;
            }

            // The report length has to come from the device: padding a report
            // to the wrong size is rejected outright rather than truncated.
            int outputLength = 0;
            if (HidD_GetPreparsedData(handle, out var preparsed))
            {
                try
                {
                    if (HidP_GetCaps(preparsed, out var caps) == 0x00110000)
                        outputLength = caps.OutputReportByteLength;
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }

            return new HidDevice(handle, path, outputLength);
        }

        return null;
    }

    /// <summary>
    /// Sends an output report. Tries the HID call first and falls back to a
    /// plain write, because which of the two a device accepts varies.
    /// </summary>
    public bool Write(byte[] report)
    {
        if (HidD_SetOutputReport(_handle, report, report.Length)) return true;

        return WriteFile(_handle, report, report.Length, out int written, IntPtr.Zero) && written > 0;
    }

    private static IEnumerable<string> EnumeratePaths()
    {
        var guid = HidGuid;
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) yield break;

        try
        {
            var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                if (needed <= 0) continue;

                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    // cbSize is the size of the fixed part, not of the buffer.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, needed, out _, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(buffer + 4);
                    if (!string.IsNullOrEmpty(path)) yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid interfaceClassGuid,
                                                           uint index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data,
                                                               IntPtr detail, int detailSize, out int required,
                                                               IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                    uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int count,
                                         out int written, IntPtr overlapped);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsed, out HidpCaps caps);
}
