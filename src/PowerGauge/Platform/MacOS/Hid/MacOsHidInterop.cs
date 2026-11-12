using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerGauge.Platform.MacOS.Hid;

internal static partial class MacOsHidInterop
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

        manager = IOHIDManagerCreate(IntPtr.Zero, 0);
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

        var count = (int)CFSetGetCount(deviceSet);
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

        return !CFNumberGetValue(property, KCFNumberSInt64Type, out var value)
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

        var requiredLength = checked((int)maxBufferSize + 1);
        Span<byte> buffer = requiredLength <= 256 ? stackalloc byte[requiredLength] : new byte[requiredLength];
        if (!CFStringGetCString(property, buffer, buffer.Length, KCFStringEncodingUtf8))
        {
            return string.Empty;
        }

        var terminatorIndex = buffer.IndexOf((byte)0);
        var utf8Bytes = terminatorIndex >= 0 ? buffer[..terminatorIndex] : buffer;
        return Encoding.UTF8.GetString(utf8Bytes);
    }

    public static bool TryOpenDevice(IntPtr device, out string error)
    {
        var result = IOHIDDeviceOpen(device, 0);
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
            _ = IOHIDDeviceClose(device, 0);
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

    [LibraryImport(IOKitLibrary)]
    private static partial IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);

    [LibraryImport(IOKitLibrary)]
    private static partial void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);

    [LibraryImport(IOKitLibrary)]
    private static partial IntPtr IOHIDManagerCopyDevices(IntPtr manager);

    [LibraryImport(IOKitLibrary)]
    private static partial IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);

    [LibraryImport(IOKitLibrary)]
    private static partial IntPtr IOHIDDeviceGetService(IntPtr device);

    [LibraryImport(IOKitLibrary)]
    private static partial int IORegistryEntryGetRegistryEntryID(IntPtr entry, out ulong entryId);

    [LibraryImport(IOKitLibrary)]
    private static partial int IOHIDDeviceOpen(IntPtr device, uint options);

    [LibraryImport(IOKitLibrary)]
    private static partial int IOHIDDeviceClose(IntPtr device, uint options);

    [LibraryImport(IOKitLibrary)]
    private static partial uint IOHIDCheckAccess(uint requestType);

    [LibraryImport(IOKitLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool IOHIDRequestAccess(uint requestType);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFSetGetCount(IntPtr set);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFSetGetValues(IntPtr set, Span<IntPtr> values);

    [LibraryImport(CoreFoundationLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int numberType, out long value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFStringGetLength(IntPtr handle);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [LibraryImport(CoreFoundationLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr handle, Span<byte> buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundationLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFRelease(IntPtr handle);

    internal readonly struct CfStringHandle : IDisposable
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
