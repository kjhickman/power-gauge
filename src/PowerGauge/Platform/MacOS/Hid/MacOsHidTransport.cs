using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.MacOS.Hid;

public sealed class MacOsHidTransport : IHidTransport
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

        public bool TrySetFeature(ReadOnlySpan<byte> request, out string error)
        {
            if (request.IsEmpty)
            {
                error = "IOHIDDeviceSetReport failed: empty request.";
                return false;
            }

            var buffer = request.ToArray();
            var result = IOHIDDeviceSetReport(_device, MacOsHidInterop.FeatureReportType, buffer[0], buffer, (nint)buffer.Length);
            if (result == 0)
            {
                error = string.Empty;
                return true;
            }

            error = $"IOHIDDeviceSetReport failed: 0x{result:x8}";
            return false;
        }

        public bool TryGetFeature(Span<byte> response, out string error)
        {
            if (response.IsEmpty)
            {
                error = "IOHIDDeviceGetReport failed: empty response buffer.";
                return false;
            }

            var reportId = response[0];
            var buffer = new byte[response.Length];
            nint payloadSize = buffer.Length;
            var result = IOHIDDeviceGetReport(_device, MacOsHidInterop.FeatureReportType, reportId, buffer, ref payloadSize);
            if (result != 0)
            {
                error = $"IOHIDDeviceGetReport failed: 0x{result:x8}";
                return false;
            }

            response.Clear();
            buffer.AsSpan(0, Math.Min((int)payloadSize, response.Length)).CopyTo(response);
            error = string.Empty;
            return true;
        }

        public bool TryExchangeFeatureReport(
            byte[] request,
            byte[] response,
            HidFeatureResponseClassifier? responseClassifier,
            out PowerFailureKind failureKind,
            out string error)
        {
            failureKind = PowerFailureKind.None;
            error = string.Empty;
            var classify = responseClassifier ?? AcceptAnyResponse;
            var sawPendingResponse = false;
            var sawUnrelatedResponse = false;

            ReadOnlySpan<int> delays = [35, 50, 70, 100, 150, 225];
            foreach (var layout in GetLayouts(request, response))
            {
                foreach (var delayMs in delays)
                {
                    Array.Clear(response);
                    if (request.Length > 0 && response.Length > 0)
                    {
                        response[0] = request[0];
                    }

                    if (!TrySetLayoutFeature(_device, layout, out error))
                    {
                        continue;
                    }

                    Thread.Sleep(delayMs);

                    if (!TryGetLayoutFeature(_device, layout, response, out error))
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

        private static IEnumerable<ReportLayout> GetLayouts(byte[] request, byte[] response)
        {
            if (request.Length == 0)
            {
                yield break;
            }

            yield return new ReportLayout(request[0], request, 0, response, 0, "full payload");

            if (request.Length > 1 && response.Length > 1 && request[0] == 0x00)
            {
                yield return new ReportLayout(request[0], request, 1, response, 1, "stripped zero report id");
            }
        }

        private static bool TrySetLayoutFeature(IntPtr device, ReportLayout layout, out string error)
        {
            var payloadLength = layout.Request.Length - layout.RequestOffset;
            if (payloadLength <= 0)
            {
                error = $"IOHIDDeviceSetReport failed for {layout.Description}: empty request.";
                return false;
            }

            var payload = new byte[payloadLength];
            Array.Copy(layout.Request, layout.RequestOffset, payload, 0, payload.Length);

            var result = IOHIDDeviceSetReport(device, MacOsHidInterop.FeatureReportType, layout.ReportId, payload, (nint)payload.Length);
            if (result == 0)
            {
                error = string.Empty;
                return true;
            }

            error = $"IOHIDDeviceSetReport failed for {layout.Description}: 0x{result:x8}";
            return false;
        }

        private static bool TryGetLayoutFeature(IntPtr device, ReportLayout layout, byte[] response, out string error)
        {
            var payloadLength = layout.ResponseTemplate.Length - layout.ResponseOffset;
            if (payloadLength <= 0)
            {
                error = $"IOHIDDeviceGetReport failed for {layout.Description}: empty response buffer.";
                return false;
            }

            var payload = new byte[payloadLength];
            nint payloadSize = payload.Length;
            var result = IOHIDDeviceGetReport(device, MacOsHidInterop.FeatureReportType, layout.ReportId, payload, ref payloadSize);
            if (result != 0)
            {
                error = $"IOHIDDeviceGetReport failed for {layout.Description}: 0x{result:x8}";
                return false;
            }

            Array.Clear(response);
            response[0] = layout.ReportId;
            Array.Copy(payload, 0, response, layout.ResponseOffset, Math.Min((int)payloadSize, response.Length - layout.ResponseOffset));
            error = string.Empty;
            return true;
        }

        private static HidFeatureResponseStatus AcceptAnyResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
        {
            return HidFeatureResponseStatus.Accepted;
        }

        private sealed record ReportLayout(byte ReportId, byte[] Request, int RequestOffset, byte[] ResponseTemplate, int ResponseOffset, string Description);
    }

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOHIDDeviceSetReport(IntPtr device, int reportType, byte reportId, byte[] report, nint reportLength);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOHIDDeviceGetReport(IntPtr device, int reportType, byte reportId, byte[] report, ref nint reportLength);
}
