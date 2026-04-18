using System.Text;
using ViperLink.App.Platform.Abstractions;

namespace ViperLink.App.Razer.Devices;

internal interface IRazerMouseDriver
{
    bool Supports(HidDeviceInfo device);

    int GetPriority(HidDeviceInfo device);

    string GetDisplayName(HidDeviceInfo device);

    bool TryProbe(HidDeviceInfo device, IHidFeatureTransport featureTransport, StringBuilder diagnostics, out int batteryPercent, out bool? isCharging);
}
