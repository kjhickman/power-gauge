using System;
using System.Threading.Tasks;
using ViperLink.App.Services;

namespace ViperLink.App.Application;

public sealed class PowerPollingController
{
    private readonly IMousePowerReader _powerReader;
    private MousePowerSnapshot? _lastSuccessfulSnapshot;

    public PowerPollingController(IMousePowerReader powerReader)
    {
        _powerReader = powerReader;
    }

    public BatteryProbeResult CreateInitialResult()
    {
        return new BatteryProbeResult(
            "Battery: probing...",
            "Status: probing...",
            "Device: scanning Razer HID devices...",
            $"Last updated: {DateTimeOffset.Now:HH:mm:ss}",
            string.Empty,
            false,
            $"{AppIdentity.ProductName}\nRefreshing battery status...");
    }

    public bool HasSuccessfulSnapshot => _lastSuccessfulSnapshot is not null;

    public async Task<BatteryProbeResult> RefreshAsync()
    {
        var snapshot = await Task.Run(_powerReader.Probe);
        if (snapshot.IsSuccessful)
        {
            _lastSuccessfulSnapshot = snapshot;
        }

        return BatteryProbeResult.FromSnapshot(snapshot, _lastSuccessfulSnapshot);
    }
}
