using System.Text;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Hid;

public interface IHidDeviceDriver
{
    string DriverId { get; }

    DeviceKind DeviceKind { get; }

    bool MatchesVendor(int vendorId);

    bool Supports(HidDeviceInfo device);

    string GetDeviceKey(HidDeviceInfo device) => DriverId;

    int GetPriority(HidDeviceInfo device) => 0;

    string GetDisplayName(HidDeviceInfo device);

    DeviceProbeOutcome Probe(HidDeviceInfo device, IHidDeviceSession session, StringBuilder diagnostics);
}
