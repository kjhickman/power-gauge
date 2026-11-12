using System;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.MacOS.Hid;

public sealed partial class MacOsHidTransport : IHidTransport
{
    public HidOpenResult Open(HidDeviceInfo device)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new HidOpenFailure(
                PowerFailureKind.UnsupportedResponse,
                "macOS HID transport unavailable on this platform.");
        }

        if (!MacOsHidInterop.TryParseRegistryEntryId(device.DevicePath, out var registryEntryId))
        {
            return new HidOpenFailure(
                PowerFailureKind.DeviceUnavailable,
                $"Unsupported macOS HID path: {device.DevicePath}");
        }

        var accessStatus = MacOsHidInterop.GetListenAccessStatus();
        if (accessStatus != MacOsHidInterop.HidAccessStatus.Granted && !MacOsHidInterop.RequestListenAccess())
        {
            return new HidOpenFailure(
                PowerFailureKind.PermissionRequired,
                $"Input Monitoring permission is required to open wireless HID devices on macOS. Current status: {accessStatus}. Allow {AppIdentity.ProductName} in System Settings > Privacy & Security > Input Monitoring, then relaunch the app.");
        }

        if (!TryFindDevice(registryEntryId, out var manager, out var deviceSet, out var hidDevice, out var error))
        {
            MacOsHidInterop.Release(deviceSet);
            MacOsHidInterop.Release(manager);
            return new HidOpenFailure(PowerFailureKind.DeviceUnavailable, error);
        }

        if (!MacOsHidInterop.TryOpenDevice(hidDevice, out error))
        {
            MacOsHidInterop.Release(deviceSet);
            MacOsHidInterop.Release(manager);

            if (error.Contains("0xe00002e2", StringComparison.OrdinalIgnoreCase))
            {
                return new HidOpenFailure(
                    PowerFailureKind.PermissionRequired,
                    $"{error} Input Monitoring permission is required to open wireless HID devices on macOS. Allow {AppIdentity.ProductName} in System Settings > Privacy & Security > Input Monitoring, then relaunch the app.");
            }

            return new HidOpenFailure(PowerFailureKind.DeviceUnavailable, error);
        }

        return new MacOsHidDeviceSession(manager, deviceSet, hidDevice);
    }

    private static bool TryFindDevice(ulong registryEntryId, out IntPtr manager, out IntPtr deviceSet, out IntPtr matchedDevice, out string error)
    {
        matchedDevice = IntPtr.Zero;
        if (!MacOsHidInterop.TryCopyDevices(out manager, out deviceSet, out var devices, out error))
        {
            return false;
        }

        foreach (var device in devices)
        {
            if (MacOsHidInterop.GetRegistryEntryId(device) == registryEntryId)
            {
                matchedDevice = device;
                return true;
            }
        }

        error = "Device unavailable.";
        return false;
    }

    private sealed class MacOsHidDeviceSession : IHidDeviceSession
    {
        private readonly IntPtr _manager;
        private readonly IntPtr _deviceSet;
        private readonly IntPtr _device;
        private bool _disposed;

        public MacOsHidDeviceSession(IntPtr manager, IntPtr deviceSet, IntPtr device)
        {
            _manager = manager;
            _deviceSet = deviceSet;
            _device = device;
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

            if (request.IsEmpty)
            {
                failureKind = PowerFailureKind.UnsupportedResponse;
                error = "IOHIDDeviceSetReport failed: empty request.";
                return false;
            }

            var classify = responseClassifier ?? AcceptAnyResponse;
            var sawPendingResponse = false;
            var sawUnrelatedResponse = false;
            var reportId = request[0];

            ReadOnlySpan<int> offsets = request.Length > 1 && response.Length > 1 && reportId == 0x00
                ? [0, 1]
                : [0];
            ReadOnlySpan<int> delays = [35, 50, 70, 100, 150, 225];

            foreach (var offset in offsets)
            {
                var layoutDescription = offset == 0 ? "full payload" : "stripped zero report id";
                var requestSlice = request[offset..];

                foreach (var delayMs in delays)
                {
                    response.Clear();
                    if (!response.IsEmpty)
                    {
                        response[0] = reportId;
                    }

                    if (!TrySetSliceFeature(_device, reportId, requestSlice, layoutDescription, out error))
                    {
                        continue;
                    }

                    Thread.Sleep(delayMs);

                    if (!TryGetSliceFeature(_device, reportId, response, offset, layoutDescription, out error))
                    {
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
                        sawUnrelatedResponse = true;
                        continue;
                    }

                    error = string.Empty;
                    return true;
                }
            }

            if (sawPendingResponse)
            {
                failureKind = PowerFailureKind.DeviceSleeping;
                error = "Only placeholder response received.";
            }
            else if (sawUnrelatedResponse)
            {
                failureKind = PowerFailureKind.ProtocolTimeout;
                error = "Feature report was overwritten by concurrent device traffic.";
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            MacOsHidInterop.CloseDevice(_device);
            MacOsHidInterop.Release(_deviceSet);
            MacOsHidInterop.Release(_manager);
        }

        private static bool TrySetSliceFeature(IntPtr device, byte reportId, ReadOnlySpan<byte> payload, string description, out string error)
        {
            if (payload.IsEmpty)
            {
                error = $"IOHIDDeviceSetReport failed for {description}: empty request.";
                return false;
            }

            var result = IOHIDDeviceSetReport(device, MacOsHidInterop.FeatureReportType, reportId, payload, payload.Length);
            if (result == 0)
            {
                error = string.Empty;
                return true;
            }

            error = $"IOHIDDeviceSetReport failed for {description}: 0x{result:x8}";
            return false;
        }

        private static bool TryGetSliceFeature(IntPtr device, byte reportId, Span<byte> response, int responseOffset, string description, out string error)
        {
            var payload = response[responseOffset..];
            if (payload.IsEmpty)
            {
                error = $"IOHIDDeviceGetReport failed for {description}: empty response buffer.";
                return false;
            }

            response.Clear();
            response[0] = reportId;
            nint payloadSize = payload.Length;
            var result = IOHIDDeviceGetReport(device, MacOsHidInterop.FeatureReportType, reportId, payload, ref payloadSize);
            if (result != 0)
            {
                error = $"IOHIDDeviceGetReport failed for {description}: 0x{result:x8}";
                return false;
            }

            if (payloadSize < payload.Length)
            {
                payload[(int)payloadSize..].Clear();
            }

            error = string.Empty;
            return true;
        }

        private static HidFeatureResponseStatus AcceptAnyResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        {
            return HidFeatureResponseStatus.Accepted;
        }
    }

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static partial int IOHIDDeviceSetReport(IntPtr device, int reportType, nint reportId, ReadOnlySpan<byte> report, nint reportLength);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static partial int IOHIDDeviceGetReport(IntPtr device, int reportType, nint reportId, Span<byte> report, ref nint reportLength);
}
