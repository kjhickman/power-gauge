using System;

namespace PowerGauge.Platform.Abstractions;

public interface IDeviceChangeMonitor : IDisposable
{
    event Action? DevicesChanged;
}
