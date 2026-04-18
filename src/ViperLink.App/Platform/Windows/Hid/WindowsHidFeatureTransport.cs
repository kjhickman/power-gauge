using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using ViperLink.App.Platform.Abstractions;

namespace ViperLink.App.Platform.Windows.Hid;

public sealed class WindowsHidFeatureTransport : IHidFeatureTransport
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    public bool TryExchangeFeatureReport(string devicePath, byte[] request, byte[] response, out string error)
    {
        error = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            error = "Windows HID transport unavailable on this platform.";
            return false;
        }

        using var handle = CreateFile(
            devicePath,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            error = $"CreateFile failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        if (!HidD_SetFeature(handle, request, request.Length))
        {
            error = $"HidD_SetFeature failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        var delays = new[] { 35, 50, 70 };
        foreach (var delayMs in delays)
        {
            Thread.Sleep(delayMs);

            Array.Clear(response);
            response[0] = request[0];
            if (!HidD_GetFeature(handle, response, response.Length))
            {
                error = $"HidD_GetFeature failed after {delayMs}ms: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                continue;
            }

            if (!LooksLikePlaceholderResponse(request, response))
            {
                return true;
            }
        }

        error = "Only placeholder response received.";
        return false;
    }

    private static bool LooksLikePlaceholderResponse(byte[] request, byte[] response)
    {
        if (request.Length != response.Length || response.Length < 10)
        {
            return false;
        }

        if (response[1] != request[1])
        {
            return false;
        }

        return response[5] == request[5]
            && response[6] == request[6]
            && response[7] == request[7]
            && response[8] == 0x00
            && response[9] == 0x00;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);
}
