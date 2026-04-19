using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerGauge.Platform.MacOS.Hid;

internal static class MacOsHidInterop
{
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const int KCFNumberSInt64Type = 4;
    private const uint KCFStringEncodingUtf8 = 0x08000100;
    private const int KIOReturnSuccess = 0;
    private const uint KIOHIDRequestTypeListenEvent = 1;
    internal const int FeatureReportType = 2;

    public static bool TryCopyDevices(out IntPtr manager, out IntPtr deviceSet, out IntPtr[] devices, out string error)
    {
        manager = IntPtr.Zero;
        deviceSet = IntPtr.Zero;
        devices = [];
        error = string.Empty;

        manager = IOHIDManagerCreate(IntPtr.Zero, IntPtr.Zero);
        if (manager == IntPtr.Zero)
        {
            error = "IOHIDManagerCreate failed.";
            return false;
        }

        IOHIDManagerSetDeviceMatching(manager, IntPtr.Zero);

        deviceSet = IOHIDManagerCopyDevices(manager);
        if (deviceSet == IntPtr.Zero)
        {
            devices = [];
            return true;
        }

        var count = CFSetGetCount(deviceSet).ToInt32();
        if (count <= 0)
        {
            devices = [];
            return true;
        }

        devices = new IntPtr[count];
        CFSetGetValues(deviceSet, devices);
        return true;
    }

    public static ulong GetRegistryEntryId(IntPtr device)
    {
        var service = IOHIDDeviceGetService(device);
        if (service == IntPtr.Zero)
        {
            return 0;
        }

        return IORegistryEntryGetRegistryEntryID(service, out var entryId) == KIOReturnSuccess
            ? entryId
            : 0;
    }

    public static int GetIntProperty(IntPtr device, string propertyName)
    {
        using var propertyNameHandle = new CfStringHandle(propertyName);
        var property = IOHIDDeviceGetProperty(device, propertyNameHandle.Handle);
        if (property == IntPtr.Zero)
        {
            return 0;
        }

        return !CFNumberGetValue(property, KCFNumberSInt64Type, out long value)
            ? 0
            : checked((int)value);
    }

    public static string GetStringProperty(IntPtr device, string propertyName)
    {
        using var propertyNameHandle = new CfStringHandle(propertyName);
        var property = IOHIDDeviceGetProperty(device, propertyNameHandle.Handle);
        if (property == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = CFStringGetLength(property);
        if (length == 0)
        {
            return string.Empty;
        }

        var maxBufferSize = CFStringGetMaximumSizeForEncoding(length, KCFStringEncodingUtf8);
        if (maxBufferSize <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[maxBufferSize + 1];
        if (!CFStringGetCString(property, buffer, buffer.Length, KCFStringEncodingUtf8))
        {
            return string.Empty;
        }

        var terminatorIndex = Array.IndexOf(buffer, (byte)0);
        var byteCount = terminatorIndex >= 0 ? terminatorIndex : buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, byteCount);
    }

    public static bool TryOpenDevice(IntPtr device, out string error)
    {
        var result = IOHIDDeviceOpen(device, IntPtr.Zero);
        if (result == KIOReturnSuccess)
        {
            error = string.Empty;
            return true;
        }

        error = $"Open failed: 0x{result:x8}";
        return false;
    }

    public static HidAccessStatus GetListenAccessStatus()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 15))
        {
            return HidAccessStatus.Granted;
        }

        return (HidAccessStatus)IOHIDCheckAccess(KIOHIDRequestTypeListenEvent);
    }

    public static bool RequestListenAccess()
    {
        return !OperatingSystem.IsMacOSVersionAtLeast(10, 15)
            || IOHIDRequestAccess(KIOHIDRequestTypeListenEvent);
    }

    public static void CloseDevice(IntPtr device)
    {
        if (device != IntPtr.Zero)
        {
            _ = IOHIDDeviceClose(device, IntPtr.Zero);
        }
    }

    public static string FormatRegistryEntryId(ulong entryId)
    {
        return entryId.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseRegistryEntryId(string value, out ulong entryId)
    {
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out entryId);
    }

    public static void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            CFRelease(handle);
        }
    }

    [DllImport(IOKitLibrary)]
    private static extern IntPtr IOHIDManagerCreate(IntPtr allocator, IntPtr options);

    [DllImport(IOKitLibrary)]
    private static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);

    [DllImport(IOKitLibrary)]
    private static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);

    [DllImport(IOKitLibrary)]
    private static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);

    [DllImport(IOKitLibrary)]
    private static extern IntPtr IOHIDDeviceGetService(IntPtr device);

    [DllImport(IOKitLibrary)]
    private static extern int IORegistryEntryGetRegistryEntryID(IntPtr entry, out ulong entryId);

    [DllImport(IOKitLibrary)]
    private static extern int IOHIDDeviceOpen(IntPtr device, IntPtr options);

    [DllImport(IOKitLibrary)]
    private static extern int IOHIDDeviceClose(IntPtr device, IntPtr options);

    [DllImport(IOKitLibrary)]
    private static extern uint IOHIDCheckAccess(uint requestType);

    [DllImport(IOKitLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool IOHIDRequestAccess(uint requestType);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFSetGetCount(IntPtr set);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFSetGetValues(IntPtr set, [Out] IntPtr[] values);

    [DllImport(CoreFoundationLibrary)]
    private static extern bool CFNumberGetValue(IntPtr number, int numberType, out long value);

    [DllImport(CoreFoundationLibrary)]
    private static extern long CFStringGetLength(IntPtr handle);

    [DllImport(CoreFoundationLibrary)]
    private static extern long CFStringGetMaximumSizeForEncoding(long length, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern bool CFStringGetCString(IntPtr handle, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr handle);

    internal sealed class CfStringHandle : IDisposable
    {
        public CfStringHandle(string value)
        {
            Handle = CFStringCreateWithCString(IntPtr.Zero, value, KCFStringEncodingUtf8);
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                CFRelease(Handle);
            }
        }
    }

    public enum HidAccessStatus : uint
    {
        Granted = 0,
        Denied = 1,
        Unknown = 2,
    }
}
