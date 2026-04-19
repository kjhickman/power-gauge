using System;
using PowerGauge.Application;
using PowerGauge.Domain;

namespace PowerGauge.Platform;

public sealed class UnsupportedMousePowerReader : IMousePowerReader
{
    public MousePowerSnapshot Probe()
    {
        return new MousePowerSnapshot(
            DateTimeOffset.Now,
            "unsupported platform",
            null,
            null,
            false,
            PowerFailureKind.UnsupportedResponse,
            "platform unsupported",
            $"{AppIdentity.ProductName} does not support this operating system yet.");
    }
}
