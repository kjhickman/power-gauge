using System;
using PowerGauge.Application;
using PowerGauge.Domain;

namespace PowerGauge.Platform;

public sealed class UnsupportedMousePowerReader : IMousePowerReader
{
    public PowerProbeReport Probe()
    {
        return new PowerProbeReport(
            DateTimeOffset.Now,
            [],
            PowerFailureKind.UnsupportedResponse,
            "platform unsupported",
            $"{AppIdentity.ProductName} does not support this operating system yet.");
    }
}
