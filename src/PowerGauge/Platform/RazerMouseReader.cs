using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PowerGauge.Application;
using PowerGauge.Diagnostics;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Razer.Devices;

namespace PowerGauge.Platform;

public class RazerMouseReader : IMousePowerReader
{
    private const int RazerVendorId = 0x1532;
    private readonly IHidDeviceEnumerator _deviceEnumerator;
    private readonly IHidFeatureTransport _featureTransport;
    private readonly IReadOnlyList<IRazerMouseDriver> _mouseDrivers;
    private readonly ProbeLogWriter _probeLogWriter;

    internal RazerMouseReader(IHidDeviceEnumerator deviceEnumerator, IHidFeatureTransport featureTransport, IReadOnlyList<IRazerMouseDriver> mouseDrivers, ProbeLogWriter probeLogWriter)
    {
        _deviceEnumerator = deviceEnumerator;
        _featureTransport = featureTransport;
        _mouseDrivers = mouseDrivers;
        _probeLogWriter = probeLogWriter;
    }

    public MousePowerSnapshot Probe()
    {
        var timestamp = DateTimeOffset.Now;
        var diagnostics = new StringBuilder();

        IReadOnlyList<HidDeviceInfo> razerDevices;
        try
        {
            razerDevices = _deviceEnumerator
                .Enumerate()
                .Where(device => device.VendorId == RazerVendorId)
                .OrderBy(device => device.ProductId)
                .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"HID enumeration failed: {ex.Message}");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no compatible Razer HID device",
                null,
                null,
                false,
                PowerFailureKind.EnumerationFailed,
                "enumeration failed",
                diagnostics.ToString()));
        }

        diagnostics.AppendLine($"Detected {razerDevices.Count} Razer HID device(s).");
        foreach (var device in razerDevices)
        {
            diagnostics.AppendLine($"Found {DescribeDevice(device)}");
        }

        if (razerDevices.Count == 0)
        {
            diagnostics.AppendLine("No Razer HID devices found.");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no compatible Razer HID device",
                null,
                null,
                false,
                PowerFailureKind.DeviceUnavailable,
                "no devices",
                diagnostics.ToString()));
        }

        var candidateDevices = PrioritizeDevices(razerDevices);
        if (candidateDevices.Count == 0)
        {
            diagnostics.AppendLine("No supported Razer mouse device found.");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no supported Razer mouse device",
                null,
                null,
                false,
                PowerFailureKind.DeviceUnavailable,
                "no supported device",
                diagnostics.ToString()));
        }

        diagnostics.AppendLine($"Prioritized {candidateDevices.Count} candidate device(s) for probing.");

        foreach (var candidate in candidateDevices)
        {
            var device = candidate.Device;
            var driver = candidate.Driver;
            diagnostics.AppendLine($"Trying {DescribeDevice(device)}");

            if (driver.TryProbe(device, _featureTransport, diagnostics, out var batteryPercent, out var isCharging))
            {
                return FinalizeSnapshot(new MousePowerSnapshot(
                    timestamp,
                    driver.GetDisplayName(device),
                    batteryPercent,
                    isCharging,
                    true,
                    PowerFailureKind.None,
                    "success",
                    diagnostics.ToString()));
            }
        }

        var (failureKind, resultDetail) = ClassifyProbeFailure(diagnostics.ToString());

        return FinalizeSnapshot(new MousePowerSnapshot(
            timestamp,
            $"tried {candidateDevices.Count}/{razerDevices.Count} Razer HID device(s)",
            null,
            null,
            false,
            failureKind,
            resultDetail,
            diagnostics.ToString()));
    }

    private IReadOnlyList<DriverCandidate> PrioritizeDevices(IReadOnlyList<HidDeviceInfo> devices)
    {
        var candidates = new List<DriverCandidate>();

        foreach (var device in devices)
        {
            var driver = GetBestDriver(device);
            if (driver is null)
            {
                continue;
            }

            candidates.Add(new DriverCandidate(device, driver));
        }

        return candidates
            .OrderBy(candidate => candidate.Driver.GetPriority(candidate.Device))
            .ThenBy(candidate => candidate.Device.DevicePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string DescribeDevice(HidDeviceInfo device)
    {
        var name = device.ProductName;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({device.VendorId:x4}:{device.ProductId:x4}, feature={device.FeatureReportLength}, path={device.DevicePath})");
    }

    private MousePowerSnapshot FinalizeSnapshot(MousePowerSnapshot snapshot)
    {
        return snapshot with { LogFilePath = _probeLogWriter.Write(snapshot) };
    }

    private static (PowerFailureKind FailureKind, string ResultDetail) ClassifyProbeFailure(string diagnostics)
    {
        if (diagnostics.Contains("Discarding zero battery reading from wireless device while not charging.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceSleeping, "device sleeping");
        }

        if (diagnostics.Contains("Only placeholder response received.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceSleeping, "device sleeping");
        }

        if (diagnostics.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.ProtocolTimeout, "probe timed out");
        }

        if (diagnostics.Contains("Input Monitoring permission is required", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.PermissionRequired, "input monitoring required");
        }

        if (diagnostics.Contains("Open failed.", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("CreateFile failed", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("No Razer HID devices found.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceUnavailable, "device unavailable");
        }

        return (PowerFailureKind.UnsupportedResponse, "unsupported response");
    }

    private IRazerMouseDriver? GetBestDriver(HidDeviceInfo device)
    {
        return _mouseDrivers
            .Where(driver => driver.Supports(device))
            .OrderBy(driver => driver.GetPriority(device))
            .FirstOrDefault();
    }

    private sealed record DriverCandidate(HidDeviceInfo Device, IRazerMouseDriver Driver);
}
