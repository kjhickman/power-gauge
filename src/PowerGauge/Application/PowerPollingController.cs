using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PowerGauge.Domain;
using PowerGauge.Tray;

namespace PowerGauge.Application;

public sealed class PowerPollingController
{
    private readonly IDevicePowerReader _powerReader;
    private readonly Dictionary<string, DevicePowerSuccess> _lastSuccessfulByDevice = new(StringComparer.Ordinal);

    public PowerPollingController(IDevicePowerReader powerReader)
    {
        _powerReader = powerReader;
    }

    public BatteryProbeResult CreateInitialResult()
    {
        return new BatteryProbeResult(
            [new DeviceBatteryDisplay("Scanning wireless devices...", "Battery: probing...")],
            $"Last updated: {DateTimeOffset.Now:HH:mm:ss}",
            string.Empty,
            false,
            $"{AppIdentity.ProductName}\nRefreshing battery status...");
    }

    public bool HasSuccessfulSnapshot => _lastSuccessfulByDevice.Count > 0;

    public async Task<BatteryProbeResult> RefreshAsync()
    {
        var report = await Task.Run(_powerReader.Probe);

        if (report.FailureKind != PowerFailureKind.EnumerationFailed)
        {
            var detectedKeys = report.Devices
                .Select(device => device.DeviceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var cachedKey in _lastSuccessfulByDevice.Keys.Where(key => !detectedKeys.Contains(key)).ToArray())
            {
                _lastSuccessfulByDevice.Remove(cachedKey);
            }
        }

        foreach (var device in report.Devices)
        {
            if (device is DevicePowerSuccess success)
            {
                _lastSuccessfulByDevice[device.DeviceKey] = success;
            }
        }

        return BatteryProbeResult.FromReport(report, _lastSuccessfulByDevice);
    }
}
