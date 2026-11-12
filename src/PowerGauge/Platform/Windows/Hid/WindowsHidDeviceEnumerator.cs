using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.Windows.Hid;

public sealed partial class WindowsHidDeviceEnumerator : IHidDeviceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int HidpStatusSuccess = 0x00110000;
    private const string UnknownProductName = "Unknown HID device";
    private static readonly IntPtr InvalidDeviceInfoSet = new(-1);

    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(in hidGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidDeviceInfoSet)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetupDiGetClassDevs failed.");
        }

        try
        {
            var devices = new List<HidDeviceInfo>();
            Span<byte> stackDetailBuffer = stackalloc byte[512];

            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    cbSize = (uint)Unsafe.SizeOf<SpDeviceInterfaceData>(),
                };

                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, in hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed.");
                }

                _ = SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, Span<byte>.Empty, 0, out var requiredSize, IntPtr.Zero);
                var detailError = Marshal.GetLastPInvokeError();
                if (detailError != 0 && detailError != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception(detailError, "SetupDiGetDeviceInterfaceDetail size query failed.");
                }

                var detailBuffer = requiredSize <= stackDetailBuffer.Length
                    ? stackDetailBuffer[..(int)requiredSize]
                    : new byte[(int)requiredSize];

                detailBuffer.Clear();
                MemoryMarshal.Write(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetupDiGetDeviceInterfaceDetail failed.");
                }

                var pathChars = MemoryMarshal.Cast<byte, char>(detailBuffer[4..]);
                var terminatorIndex = pathChars.IndexOf('\0');
                if (terminatorIndex >= 0)
                {
                    pathChars = pathChars[..terminatorIndex];
                }

                if (pathChars.IsWhiteSpace())
                {
                    continue;
                }

                devices.Add(GetDeviceInfo(new string(pathChars)));
            }

            return devices;
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static HidDeviceInfo GetDeviceInfo(string devicePath)
    {
        using var handle = WindowsHidTransport.OpenDevice(devicePath);
        if (handle.IsInvalid)
        {
            return CreateFallbackDeviceInfo(devicePath);
        }

        var attributes = new HiddAttributes
        {
            Size = Unsafe.SizeOf<HiddAttributes>(),
        };

        if (!HidD_GetAttributes(handle, ref attributes))
        {
            return CreateFallbackDeviceInfo(devicePath);
        }

        var usagePage = 0;
        var usage = 0;
        var inputReportLength = 0;
        var outputReportLength = 0;
        var featureReportLength = 0;
        if (HidD_GetPreparsedData(handle, out var preparsedData))
        {
            try
            {
                if (HidP_GetCaps(preparsedData, out var caps) == HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inputReportLength = caps.InputReportByteLength;
                    outputReportLength = caps.OutputReportByteLength;
                    featureReportLength = caps.FeatureReportByteLength;
                }
            }
            finally
            {
                _ = HidD_FreePreparsedData(preparsedData);
            }
        }

        var productName = GetProductName(handle);
        return new HidDeviceInfo(
            devicePath,
            attributes.VendorID,
            attributes.ProductID,
            featureReportLength,
            productName ?? UnknownProductName,
            usagePage,
            usage,
            inputReportLength,
            outputReportLength);
    }

    private static HidDeviceInfo CreateFallbackDeviceInfo(string devicePath)
    {
        return new HidDeviceInfo(
            devicePath,
            ParseHexSegment(devicePath, "vid_"),
            ParseHexSegment(devicePath, "pid_"),
            0,
            UnknownProductName);
    }

    private static string? GetProductName(SafeFileHandle handle)
    {
        Span<byte> rawBuffer = stackalloc byte[512];
        if (!HidD_GetProductString(handle, rawBuffer, rawBuffer.Length))
        {
            return null;
        }

        var buffer = MemoryMarshal.Cast<byte, char>(rawBuffer);
        var nullIndex = buffer.IndexOf('\0');
        var chars = nullIndex >= 0 ? buffer[..nullIndex] : buffer;
        return chars.IsWhiteSpace() ? null : new string(chars);
    }

    private static int ParseHexSegment(string devicePath, string marker)
    {
        var index = devicePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length + 4 > devicePath.Length)
        {
            return 0;
        }

        var hexSpan = devicePath.AsSpan(index + marker.Length, 4);
        return int.TryParse(hexSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    [LibraryImport("hid.dll")]
    private static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr SetupDiGetClassDevs(in Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, in Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, Span<byte> deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetProductString(SafeFileHandle hidDeviceObject, Span<byte> buffer, int bufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    private static partial int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [InlineArray(17)]
    private struct ReservedUInt16Buffer
    {
        private ushort _element0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public ReservedUInt16Buffer Reserved;
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
}
