using System.Text;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Razer.Devices;

internal interface IRazerMouseDriver
{
    string DriverId { get; }

    bool Supports(HidDeviceInfo device);

    int GetPriority(HidDeviceInfo device);

    string GetDisplayName(HidDeviceInfo device);

    bool TryProbe(HidDeviceInfo device, IHidFeatureTransport featureTransport, StringBuilder diagnostics, out int batteryPercent, out bool? isCharging);
}
