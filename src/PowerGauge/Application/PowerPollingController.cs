using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Tray;

namespace PowerGauge.Application;

public sealed class PowerPollingController : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(600);

    private readonly CompositeDevicePowerReader _powerReader;
    private readonly IDeviceChangeMonitor? _changeMonitor;
    private readonly TimeSpan _debounceDelay;
    private readonly Dictionary<string, DevicePowerSuccess> _lastSuccessfulByDevice = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public PowerPollingController(
        CompositeDevicePowerReader powerReader,
        IDeviceChangeMonitor? changeMonitor = null,
        TimeSpan? debounceDelay = null)
    {
        _powerReader = powerReader;
        _changeMonitor = changeMonitor;
        _debounceDelay = debounceDelay ?? DefaultDebounceDelay;
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (_changeMonitor is not null)
        {
            _changeMonitor.DevicesChanged += ScheduleDebouncedRefresh;
        }
    }

    public event Action<BatteryProbeResult>? ResultUpdated;

    public bool HasSuccessfulSnapshot => _lastSuccessfulByDevice.Count > 0;

    public BatteryProbeResult CreateInitialResult()
    {
        return new BatteryProbeResult(
            [new DeviceBatteryDisplay("Scanning wireless devices...", "Battery: probing...")],
            $"Last updated: {DateTimeOffset.Now:HH:mm:ss}",
            string.Empty,
            false,
            $"{AppIdentity.ProductName}\nRefreshing battery status...");
    }

    public async Task<BatteryProbeResult> RefreshAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var report = await Task.Run(_powerReader.Probe).ConfigureAwait(false);

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

            var result = BatteryProbeResult.FromReport(report, _lastSuccessfulByDevice);
            ResultUpdated?.Invoke(result);
            return result;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_changeMonitor is not null)
        {
            _changeMonitor.DevicesChanged -= ScheduleDebouncedRefresh;
            _changeMonitor.Dispose();
        }

        _debounceTimer.Dispose();
    }

    private void ScheduleDebouncedRefresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _debounceTimer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async void OnDebounceElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
