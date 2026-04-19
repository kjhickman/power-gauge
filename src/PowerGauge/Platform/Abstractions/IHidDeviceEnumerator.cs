using System.Collections.Generic;

namespace PowerGauge.Platform.Abstractions;

public interface IHidDeviceEnumerator
{
    IReadOnlyList<HidDeviceInfo> Enumerate();
}
