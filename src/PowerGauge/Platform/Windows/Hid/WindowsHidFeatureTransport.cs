using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.Windows.Hid;

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

        const int maxAttempts = 4;
        ReadOnlySpan<int> pollDelaysMs = [15, 20, 30, 45];

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!HidD_SetFeature(handle, request, request.Length))
            {
                error = $"HidD_SetFeature failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                return false;
            }

            var overwrittenByOtherCommand = false;
            foreach (var delayMs in pollDelaysMs)
            {
                Thread.Sleep(delayMs);

                Array.Clear(response);
                response[0] = request[0];
                if (!HidD_GetFeature(handle, response, response.Length))
                {
                    error = $"HidD_GetFeature failed after {delayMs}ms: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    continue;
                }

                if (IsPendingSameCommand(request, response))
                {
                    continue;
                }

                if (IsDifferentCommandResponse(request, response))
                {
                    overwrittenByOtherCommand = true;
                    break;
                }

                return true;
            }

            if (!overwrittenByOtherCommand)
            {
                error = "Only placeholder response received.";
                return false;
            }
        }

        error = "Feature report was overwritten by concurrent device traffic.";
        return false;
    }

    private static bool IsPendingSameCommand(byte[] request, byte[] response)
    {
        var offset = request.Length == 91 ? 1 : 0;
        if (request.Length != response.Length || response.Length < offset + 10)
        {
            return false;
        }

        if (response[offset + 1] != request[offset + 1]
            || response[offset + 6] != request[offset + 6]
            || response[offset + 7] != request[offset + 7])
        {
            return false;
        }

        var status = response[offset];
        if (status == 0x01)
        {
            return true;
        }

        return status == 0x00
            && response[offset + 8] == 0x00
            && response[offset + 9] == 0x00;
    }

    private static bool IsDifferentCommandResponse(byte[] request, byte[] response)
    {
        var offset = request.Length == 91 ? 1 : 0;
        if (request.Length != response.Length || response.Length < offset + 8)
        {
            return false;
        }

        return response[offset + 6] != request[offset + 6]
            || response[offset + 7] != request[offset + 7];
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
