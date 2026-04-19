using System;
using PowerGauge.Diagnostics;
using PowerGauge.Platform;
using PowerGauge.Platform.Windows.Hid;
using PowerGauge.Razer.Devices;

namespace PowerGauge.Platform.Windows;

internal sealed class WindowsRazerMouseReader : RazerMouseReader
{
    public WindowsRazerMouseReader()
        : this(new WindowsHidDeviceEnumerator(), new WindowsHidFeatureTransport(), [new ViperUltimateDriver()], new ProbeLogWriter())
    {
    }

    internal WindowsRazerMouseReader(Platform.Abstractions.IHidDeviceEnumerator deviceEnumerator, Platform.Abstractions.IHidFeatureTransport featureTransport, System.Collections.Generic.IReadOnlyList<IRazerMouseDriver> mouseDrivers, ProbeLogWriter probeLogWriter)
        : base(deviceEnumerator, featureTransport, mouseDrivers, probeLogWriter)
    {
    }
}
