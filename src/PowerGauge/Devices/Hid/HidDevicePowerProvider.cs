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
        if (targetGroups.Length == 0)
        {
            diagnostics.AppendLine("No supported wireless HID device found.");
            return new ProviderProbeResult(
                [],
                PowerFailureKind.DeviceUnavailable,
                "no supported device",
                diagnostics.ToString());
        }

        diagnostics.AppendLine($"Matched {targetGroups.Length} supported wireless HID device(s).");
        var snapshots = new List<DevicePowerSnapshot>(targetGroups.Length);

        foreach (var group in targetGroups)
        {
            var deviceDiagnostics = new StringBuilder();
            DevicePowerSnapshot? resolvedSnapshot = null;
            (PowerFailureKind Kind, string Detail)? bestFailure = null;

            foreach (var device in group.Devices)
            {
                deviceDiagnostics.AppendLine($"Trying {DescribeDevice(device)}");

                var openResult = _transport.Open(device);
                if (openResult is HidOpenFailure openFailure)
                {
                    deviceDiagnostics.AppendLine($"Open failed: {openFailure.Error}");
                    bestFailure = SelectHigherPrecedenceFailure(
                        bestFailure,
                        openFailure.Kind,
                        PowerFailureClassifier.GetDefaultDetail(openFailure.Kind));
                    continue;
                }

                if (openResult is not IHidDeviceSession session)
                {
                    continue;
                }

                using (session)
                {
                    var outcome = group.Driver.Probe(device, session, deviceDiagnostics);
                    switch (outcome)
                    {
                        case DeviceReading reading:
                            resolvedSnapshot = new DevicePowerSuccess(
                                group.DeviceKey,
                                group.Driver.DeviceKind,
                                timestamp,
                                group.Driver.GetDisplayName(device),
                                reading.BatteryPercent,
                                reading.IsCharging,
                                deviceDiagnostics.ToString());
                            break;

                        case DeviceProbeFailure failure:
                            bestFailure = SelectHigherPrecedenceFailure(bestFailure, failure.Kind, failure.Detail);
                            break;
                    }

                    if (resolvedSnapshot is not null)
                    {
                        break;
                    }
                }
            }

            if (resolvedSnapshot is null)
            {
                var (failureKind, resultDetail) = bestFailure
                    ?? (PowerFailureKind.UnsupportedResponse, PowerFailureClassifier.GetDefaultDetail(PowerFailureKind.UnsupportedResponse));

                resolvedSnapshot = new DevicePowerFailureSnapshot(
                    group.DeviceKey,
                    group.Driver.DeviceKind,
                    timestamp,
                    group.Driver.GetDisplayName(group.Devices[0]),
                    failureKind,
                    resultDetail,
                    deviceDiagnostics.ToString());
            }

            snapshots.Add(resolvedSnapshot);
            diagnostics.Append(deviceDiagnostics);
        }

        var (summaryKind, summaryDetail) = PowerFailureClassifier.Summarize(snapshots);
        return new ProviderProbeResult(snapshots, summaryKind, summaryDetail, diagnostics.ToString());
    }

    private DeviceTargetGroup[] GroupDevicesByTarget(IReadOnlyList<HidDeviceInfo> devices)
    {
        return devices
            .Select(device => (Device: device, Driver: GetBestDriver(device)))
            .Where(match => match.Driver is not null)
            .OrderBy(match => IndexOfDriver(match.Driver!))
            .GroupBy(match => (Driver: match.Driver!, Key: match.Driver!.GetDeviceKey(match.Device)))
            .Select(group => new DeviceTargetGroup(
                group.Key.Key,
                group.Key.Driver,
                [.. group.Select(match => match.Device)
                    .OrderBy(group.Key.Driver.GetPriority)
                    .ThenBy(device => device.ProductId)
                    .ThenBy(device => device.DevicePath, StringComparer.Ordinal)]))
            .ToArray();
    }

    private int IndexOfDriver(IHidDeviceDriver target)
    {
        for (var i = 0; i < _drivers.Count; i++)
        {
            if (ReferenceEquals(_drivers[i], target))
            {
                return i;
            }
        }

        return int.MaxValue;
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

    private static string DescribeDevice(HidDeviceInfo device)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{device.ProductName} ({device.VendorId:x4}:{device.ProductId:x4}, usage={device.UsagePage:x4}:{device.Usage:x4}, in={device.InputReportLength}, out={device.OutputReportLength}, feature={device.FeatureReportLength}, path={device.DevicePath})");
    }

    private sealed record DeviceTargetGroup(string DeviceKey, IHidDeviceDriver Driver, IReadOnlyList<HidDeviceInfo> Devices);
}
