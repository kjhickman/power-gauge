using PowerGauge.Diagnostics;
using PowerGauge.Platform;
using PowerGauge.Platform.MacOS.Hid;
using PowerGauge.Razer.Devices;

namespace PowerGauge.Platform.MacOS;

internal sealed class MacOsRazerMouseReader : RazerMouseReader
{
    public MacOsRazerMouseReader()
        : base(new MacOsHidDeviceEnumerator(), new MacOsHidFeatureTransport(), [new ViperUltimateDriver()], new ProbeLogWriter())
    {
    }
}
