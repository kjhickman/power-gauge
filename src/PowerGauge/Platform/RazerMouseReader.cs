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

    internal static IReadOnlyList<IRazerMouseDriver> CreateDefaultDrivers() =>
    [
        new ViperUltimateDriver(),
        new NagaV2HyperSpeedDriver(),
    ];

    public PowerProbeReport Probe()
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
            return FinalizeReport(new PowerProbeReport(
                timestamp,
                [],
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
            return FinalizeReport(new PowerProbeReport(
                timestamp,
                [],
                PowerFailureKind.DeviceUnavailable,
                "no devices",
                diagnostics.ToString()));
        }

        var driverGroups = GroupDevicesByDriver(razerDevices);
        if (driverGroups.Count == 0)
        {
            diagnostics.AppendLine("No supported Razer mouse device found.");
            return FinalizeReport(new PowerProbeReport(
                timestamp,
                [],
                PowerFailureKind.DeviceUnavailable,
                "no supported device",
                diagnostics.ToString()));
        }

        diagnostics.AppendLine($"Matched {driverGroups.Count} supported Razer mouse model(s).");
        var deviceSnapshots = new List<MousePowerSnapshot>(driverGroups.Count);

        foreach (var group in driverGroups)
        {
            var deviceDiagnostics = new StringBuilder();
            MousePowerSnapshot? resolvedSnapshot = null;

            foreach (var device in group.Devices)
            {
                deviceDiagnostics.AppendLine($"Trying {DescribeDevice(device)}");

                if (group.Driver.TryProbe(device, _featureTransport, deviceDiagnostics, out var batteryPercent, out var isCharging))
                {
                    resolvedSnapshot = new MousePowerSnapshot(
                        group.Driver.DriverId,
                        timestamp,
                        group.Driver.GetDisplayName(device),
                        batteryPercent,
                        isCharging,
                        true,
                        PowerFailureKind.None,
                        "success",
                        deviceDiagnostics.ToString());
                    break;
                }
            }

            if (resolvedSnapshot is null)
            {
                var (failureKind, resultDetail) = ClassifyProbeFailure(deviceDiagnostics.ToString());
                resolvedSnapshot = new MousePowerSnapshot(
                    group.Driver.DriverId,
                    timestamp,
                    group.Driver.GetDisplayName(group.Devices[0]),
                    null,
                    null,
                    false,
                    failureKind,
                    resultDetail,
                    deviceDiagnostics.ToString());
            }

            deviceSnapshots.Add(resolvedSnapshot);
            diagnostics.Append(deviceDiagnostics);
        }

        var (reportFailureKind, reportResultDetail) = SummarizeOutcome(deviceSnapshots);

        return FinalizeReport(new PowerProbeReport(
            timestamp,
            deviceSnapshots,
            reportFailureKind,
            reportResultDetail,
            diagnostics.ToString()));
    }

    private IReadOnlyList<DriverDeviceGroup> GroupDevicesByDriver(IReadOnlyList<HidDeviceInfo> devices)
    {
        var groups = new List<DriverDeviceGroup>();

        foreach (var driver in _mouseDrivers)
        {
            var matchedDevices = devices
                .Where(device => ReferenceEquals(GetBestDriver(device), driver))
                .OrderBy(driver.GetPriority)
                .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
                .ToArray();

            if (matchedDevices.Length > 0)
            {
                groups.Add(new DriverDeviceGroup(driver, matchedDevices));
            }
        }

        return groups;
    }

    private static (PowerFailureKind FailureKind, string ResultDetail) SummarizeOutcome(IReadOnlyList<MousePowerSnapshot> snapshots)
    {
        if (snapshots.All(snapshot => snapshot.IsSuccessful))
        {
            return (PowerFailureKind.None, "success");
        }

        if (snapshots.Any(snapshot => snapshot.IsSuccessful))
        {
            return (PowerFailureKind.None, "partial");
        }

        return (snapshots[0].FailureKind, snapshots[0].ResultDetail);
    }

    private static string DescribeDevice(HidDeviceInfo device)
    {
        var name = device.ProductName;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({device.VendorId:x4}:{device.ProductId:x4}, feature={device.FeatureReportLength}, path={device.DevicePath})");
    }

    private PowerProbeReport FinalizeReport(PowerProbeReport report)
    {
        return report with { LogFilePath = _probeLogWriter.Write(report) };
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
            || diagnostics.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("concurrent device traffic", StringComparison.OrdinalIgnoreCase))
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

    private sealed record DriverDeviceGroup(IRazerMouseDriver Driver, IReadOnlyList<HidDeviceInfo> Devices);
}
