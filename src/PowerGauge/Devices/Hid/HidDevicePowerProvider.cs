using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PowerGauge.Devices.Razer;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Hid;

public sealed class HidDevicePowerProvider : IDevicePowerProvider
{
    private readonly IHidDeviceEnumerator _deviceEnumerator;
    private readonly IHidTransport _transport;
    private readonly IReadOnlyList<IHidDeviceDriver> _drivers;

    public HidDevicePowerProvider(
        IHidDeviceEnumerator deviceEnumerator,
        IHidTransport transport,
        IReadOnlyList<IHidDeviceDriver> drivers)
    {
        _deviceEnumerator = deviceEnumerator;
        _transport = transport;
        _drivers = drivers;
    }

    public static IReadOnlyList<IHidDeviceDriver> CreateDefaultDrivers() =>
    [
        new ViperUltimateDriver(),
        new NagaV2HyperSpeedDriver(),
    ];

    public ProviderProbeResult Probe(DateTimeOffset timestamp)
    {
        var diagnostics = new StringBuilder();

        IReadOnlyList<HidDeviceInfo> allDevices;
        try
        {
            allDevices = _deviceEnumerator.Enumerate();
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"HID enumeration failed: {ex.Message}");
            return new ProviderProbeResult(
                [],
                PowerFailureKind.EnumerationFailed,
                "enumeration failed",
                diagnostics.ToString());
        }

        var vendorDevices = allDevices
            .Where(device => _drivers.Any(driver => driver.MatchesVendor(device.VendorId)))
            .OrderBy(device => device.VendorId)
            .ThenBy(device => device.ProductId)
            .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
            .ToArray();

        diagnostics.AppendLine($"Enumerated {allDevices.Count} HID interface(s); {vendorDevices.Length} matching registered vendor(s).");
        foreach (var device in vendorDevices)
        {
            diagnostics.AppendLine($"Found {DescribeDevice(device)}");
        }

        if (vendorDevices.Length == 0)
        {
            diagnostics.AppendLine("No HID devices from supported vendors found.");
            return new ProviderProbeResult(
                [],
                PowerFailureKind.DeviceUnavailable,
                "no devices",
                diagnostics.ToString());
        }

        var targetGroups = GroupDevicesByTarget(vendorDevices);
        if (targetGroups.Count == 0)
        {
            diagnostics.AppendLine("No supported wireless HID device found.");
            return new ProviderProbeResult(
                [],
                PowerFailureKind.DeviceUnavailable,
                "no supported device",
                diagnostics.ToString());
        }

        diagnostics.AppendLine($"Matched {targetGroups.Count} supported wireless HID device(s).");
        var snapshots = new List<DevicePowerSnapshot>(targetGroups.Count);

        foreach (var group in targetGroups)
        {
            var deviceDiagnostics = new StringBuilder();
            DevicePowerSnapshot? resolvedSnapshot = null;
            (PowerFailureKind Kind, string Detail)? bestFailure = null;

            foreach (var device in group.Devices)
            {
                deviceDiagnostics.AppendLine($"Trying {DescribeDevice(device)}");

                if (!_transport.TryOpen(device, out var session, out var openFailureKind, out var openError) || session is null)
                {
                    deviceDiagnostics.AppendLine($"Open failed: {openError}");
                    bestFailure = SelectHigherPrecedenceFailure(
                        bestFailure,
                        openFailureKind,
                        PowerFailureClassifier.GetDefaultDetail(openFailureKind));
                    continue;
                }

                using (session)
                {
                    var outcome = group.Driver.Probe(device, session, deviceDiagnostics);
                    if (outcome.IsSuccessful)
                    {
                        resolvedSnapshot = new DevicePowerSnapshot(
                            group.DeviceKey,
                            group.Driver.DeviceKind,
                            timestamp,
                            group.Driver.GetDisplayName(device),
                            outcome.BatteryPercent,
                            outcome.IsCharging,
                            true,
                            PowerFailureKind.None,
                            "success",
                            deviceDiagnostics.ToString());
                        break;
                    }

                    bestFailure = SelectHigherPrecedenceFailure(bestFailure, outcome.FailureKind, outcome.ResultDetail);
                }
            }

            if (resolvedSnapshot is null)
            {
                var (failureKind, resultDetail) = bestFailure
                    ?? (PowerFailureKind.UnsupportedResponse, PowerFailureClassifier.GetDefaultDetail(PowerFailureKind.UnsupportedResponse));

                resolvedSnapshot = new DevicePowerSnapshot(
                    group.DeviceKey,
                    group.Driver.DeviceKind,
                    timestamp,
                    group.Driver.GetDisplayName(group.Devices[0]),
                    null,
                    null,
                    false,
                    failureKind,
                    resultDetail,
                    deviceDiagnostics.ToString());
            }

            snapshots.Add(resolvedSnapshot);
            diagnostics.Append(deviceDiagnostics);
        }

        var (summaryKind, summaryDetail) = SummarizeOutcome(snapshots);
        return new ProviderProbeResult(snapshots, summaryKind, summaryDetail, diagnostics.ToString());
    }

    private IReadOnlyList<DeviceTargetGroup> GroupDevicesByTarget(IReadOnlyList<HidDeviceInfo> devices)
    {
        var matchedByKey = new Dictionary<string, (IHidDeviceDriver Driver, List<HidDeviceInfo> Devices)>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();

        foreach (var driver in _drivers)
        {
            foreach (var device in devices)
            {
                if (!ReferenceEquals(GetBestDriver(device), driver))
                {
                    continue;
                }

                var deviceKey = driver.GetDeviceKey(device);
                if (!matchedByKey.TryGetValue(deviceKey, out var entry))
                {
                    entry = (driver, []);
                    matchedByKey[deviceKey] = entry;
                    orderedKeys.Add(deviceKey);
                }

                entry.Devices.Add(device);
            }
        }

        var groups = new List<DeviceTargetGroup>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            var (driver, candidateDevices) = matchedByKey[key];
            var sortedCandidates = candidateDevices
                .OrderBy(driver.GetPriority)
                .ThenBy(device => device.ProductId)
                .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
                .ToArray();

            groups.Add(new DeviceTargetGroup(key, driver, sortedCandidates));
        }

        return groups;
    }

    private IHidDeviceDriver? GetBestDriver(HidDeviceInfo device)
    {
        return _drivers
            .Where(driver => driver.Supports(device))
            .OrderBy(driver => driver.GetPriority(device))
            .FirstOrDefault();
    }

    private static (PowerFailureKind Kind, string Detail) SelectHigherPrecedenceFailure(
        (PowerFailureKind Kind, string Detail)? current,
        PowerFailureKind candidateKind,
        string candidateDetail)
    {
        if (current is not { } existing)
        {
            return (candidateKind, candidateDetail);
        }

        return PowerFailureClassifier.GetPrecedence(candidateKind) < PowerFailureClassifier.GetPrecedence(existing.Kind)
            ? (candidateKind, candidateDetail)
            : existing;
    }

    private static (PowerFailureKind FailureKind, string ResultDetail) SummarizeOutcome(IReadOnlyList<DevicePowerSnapshot> snapshots)
    {
        if (snapshots.All(snapshot => snapshot.IsSuccessful))
        {
            return (PowerFailureKind.None, "success");
        }

        if (snapshots.Any(snapshot => snapshot.IsSuccessful))
        {
            return (PowerFailureKind.None, "partial");
        }

        var bestFailure = snapshots
            .OrderBy(snapshot => PowerFailureClassifier.GetPrecedence(snapshot.FailureKind))
            .First();

        return (bestFailure.FailureKind, bestFailure.ResultDetail);
    }

    private static string DescribeDevice(HidDeviceInfo device)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{device.ProductName} ({device.VendorId:x4}:{device.ProductId:x4}, usage={device.UsagePage:x4}:{device.Usage:x4}, in={device.InputReportLength}, out={device.OutputReportLength}, feature={device.FeatureReportLength}, path={device.DevicePath})");
    }

    private sealed record DeviceTargetGroup(string DeviceKey, IHidDeviceDriver Driver, IReadOnlyList<HidDeviceInfo> Devices);
}
