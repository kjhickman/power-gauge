using System;
using System.Collections.Generic;
using System.Threading;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.MacOS.Hid;

public sealed class MacOsHidFeatureTransport : IHidFeatureTransport
{
    public bool TryExchangeFeatureReport(string devicePath, byte[] request, byte[] response, out string error)
    {
        error = string.Empty;

        if (!OperatingSystem.IsMacOS())
        {
            error = "macOS HID transport unavailable on this platform.";
            return false;
        }

        if (!MacOsHidInterop.TryParseRegistryEntryId(devicePath, out var registryEntryId))
        {
            error = $"Unsupported macOS HID path: {devicePath}";
            return false;
        }

        if (!TryFindDevice(registryEntryId, out var manager, out var deviceSet, out var device, out error))
        {
            return false;
        }

        try
        {
            var accessStatus = MacOsHidInterop.GetListenAccessStatus();
            if (accessStatus != MacOsHidInterop.HidAccessStatus.Granted && !MacOsHidInterop.RequestListenAccess())
            {
                error = $"Input Monitoring permission is required to open mouse HID devices on macOS. Current status: {accessStatus}. Allow {AppIdentity.ProductName} in System Settings > Privacy & Security > Input Monitoring, then relaunch the app.";
                return false;
            }

            if (!MacOsHidInterop.TryOpenDevice(device, out error))
            {
                if (error.Contains("0xe00002e2", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"{error} Input Monitoring permission is required to open mouse HID devices on macOS. Allow {AppIdentity.ProductName} in System Settings > Privacy & Security > Input Monitoring, then relaunch the app.";
                }

                return false;
            }

            try
            {
                return TryExchangeFeatureReport(device, request, response, out error);
            }
            finally
            {
                MacOsHidInterop.CloseDevice(device);
            }
        }
        finally
        {
            MacOsHidInterop.Release(deviceSet);
            MacOsHidInterop.Release(manager);
        }
    }

    private static bool TryExchangeFeatureReport(IntPtr device, byte[] request, byte[] response, out string error)
    {
        var delays = new[] { 35, 50, 70, 100, 150, 225 };
        foreach (var layout in GetLayouts(request, response))
        {
            foreach (var delayMs in delays)
            {
                Array.Clear(response);
                response[0] = request[0];

                if (!TrySetFeature(device, layout, out error))
                {
                    continue;
                }

                Thread.Sleep(delayMs);

                if (!TryGetFeature(device, layout, response, out error))
                {
                    continue;
                }

                return true;
            }
        }

        error = "No feature report response received.";
        return false;
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

    private static IEnumerable<ReportLayout> GetLayouts(byte[] request, byte[] response)
    {
        yield return new ReportLayout(request[0], request, 0, response, 0, "full payload");

        if (request.Length > 1 && response.Length > 1 && request[0] == 0x00)
        {
            yield return new ReportLayout(request[0], request, 1, response, 1, "stripped zero report id");
        }
    }

    private static bool TrySetFeature(IntPtr device, ReportLayout layout, out string error)
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

    private static bool TryGetFeature(IntPtr device, ReportLayout layout, byte[] response, out string error)
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

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOHIDDeviceSetReport(IntPtr device, int reportType, byte reportId, byte[] report, nint reportLength);

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOHIDDeviceGetReport(IntPtr device, int reportType, byte reportId, byte[] report, ref nint reportLength);

    private sealed record ReportLayout(byte ReportId, byte[] Request, int RequestOffset, byte[] ResponseTemplate, int ResponseOffset, string Description);
}
