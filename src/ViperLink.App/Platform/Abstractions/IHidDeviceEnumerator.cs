using System.Collections.Generic;

namespace ViperLink.App.Platform.Abstractions;

public interface IHidDeviceEnumerator
{
    IReadOnlyList<HidDeviceInfo> Enumerate();
}
