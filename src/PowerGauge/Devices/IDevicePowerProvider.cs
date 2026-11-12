using System;
using System.Collections.Generic;
using PowerGauge.Domain;

namespace PowerGauge.Devices;

public sealed record ProviderProbeResult(
    IReadOnlyList<DevicePowerSnapshot> Devices,
    PowerFailureKind FailureKind,
    string ResultDetail,
    string Diagnostics);

public interface IDevicePowerProvider
{
    ProviderProbeResult Probe(DateTimeOffset timestamp);
}
