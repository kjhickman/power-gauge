using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.Windows.Hid;

public sealed partial class WindowsHidTransport : IHidTransport
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    public HidOpenResult Open(HidDeviceInfo device)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HidOpenFailure(
                PowerFailureKind.UnsupportedResponse,
                "Windows HID transport unavailable on this platform.");
        }

        var handle = OpenDevice(device.DevicePath);
        if (handle.IsInvalid)
        {
            var win32Error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return new HidOpenFailure(
                PowerFailureKind.DeviceUnavailable,
                $"CreateFile failed: {new Win32Exception(win32Error).Message}");
        }

        return new WindowsHidDeviceSession(handle);
    }

    internal static SafeFileHandle OpenDevice(string devicePath)
    {
        return CreateFile(
            devicePath,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
    }

    private sealed class WindowsHidDeviceSession : IHidDeviceSession
    {
        private readonly SafeFileHandle _handle;

        public WindowsHidDeviceSession(SafeFileHandle handle)
        {
            _handle = handle;
        }

        private bool TrySetFeature(ReadOnlySpan<byte> request, out string error)
        {
            if (request.IsEmpty)
            {
                error = "HidD_SetFeature failed: empty request.";
                return false;
            }

            if (!HidD_SetFeature(_handle, request, request.Length))
            {
                error = $"HidD_SetFeature failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool TryGetFeature(Span<byte> response, out string error)
        {
            if (response.IsEmpty)
            {
                error = "HidD_GetFeature failed: empty response buffer.";
                return false;
            }

            if (!HidD_GetFeature(_handle, response, response.Length))
            {
                error = $"HidD_GetFeature failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryExchangeFeatureReport(
            ReadOnlySpan<byte> request,
            Span<byte> response,
            HidFeatureResponseClassifier? responseClassifier,
            out PowerFailureKind failureKind,
            out string error)
        {
            failureKind = PowerFailureKind.None;
            error = string.Empty;
            var classify = responseClassifier ?? AcceptAnyResponse;

            const int maxAttempts = 4;
            ReadOnlySpan<int> pollDelaysMs = [15, 20, 30, 45];

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (!TrySetFeature(request, out error))
                {
                    failureKind = PowerFailureKind.UnsupportedResponse;
                    return false;
                }

                var overwrittenByOtherCommand = false;
                var sawPendingResponse = false;

                foreach (var delayMs in pollDelaysMs)
                {
                    Thread.Sleep(delayMs);

                    response.Clear();
                    if (!request.IsEmpty && !response.IsEmpty)
                    {
                        response[0] = request[0];
                    }

                    if (!TryGetFeature(response, out var getError))
                    {
                        error = $"HidD_GetFeature failed after {delayMs}ms: {getError}";
                        continue;
                    }

                    var status = classify(request, response);
                    if (status == HidFeatureResponseStatus.Pending)
                    {
                        sawPendingResponse = true;
                        continue;
                    }

                    if (status == HidFeatureResponseStatus.UnrelatedCommand)
                    {
                        overwrittenByOtherCommand = true;
                        break;
                    }

                    error = string.Empty;
                    return true;
                }

                if (!overwrittenByOtherCommand)
                {
                    if (sawPendingResponse)
                    {
                        failureKind = PowerFailureKind.DeviceSleeping;
                        error = "Only placeholder response received.";
                    }
                    else
                    {
                        failureKind = PowerFailureKind.UnsupportedResponse;
                        if (string.IsNullOrEmpty(error))
                        {
                            error = "No feature report response received.";
                        }
                    }

                    return false;
                }
            }

            failureKind = PowerFailureKind.ProtocolTimeout;
            error = "Feature report was overwritten by concurrent device traffic.";
            return false;
        }

        public void Dispose()
        {
            _handle.Dispose();
        }

        private static HidFeatureResponseStatus AcceptAnyResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        {
            return HidFeatureResponseStatus.Accepted;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_SetFeature(SafeFileHandle hidDeviceObject, ReadOnlySpan<byte> reportBuffer, int reportBufferLength);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetFeature(SafeFileHandle hidDeviceObject, Span<byte> reportBuffer, int reportBufferLength);
}
