using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerGauge.Domain;

public sealed record MousePowerSnapshot(
    string DeviceKey,
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    int? BatteryPercent,
    bool? IsCharging,
    bool IsSuccessful,
    PowerFailureKind FailureKind,
    string ResultDetail,
    string Diagnostics);

public sealed record PowerProbeReport(
    DateTimeOffset Timestamp,
    IReadOnlyList<MousePowerSnapshot> Devices,
    PowerFailureKind FailureKind,
    string ResultDetail,
    string Diagnostics,
    string? LogFilePath = null)
{
    public bool HasAnySuccessfulDevice => Devices.Any(device => device.IsSuccessful);
}

public enum PowerFailureKind
{
    None,
    DeviceUnavailable,
    DeviceSleeping,
    ProtocolTimeout,
    UnsupportedResponse,
    EnumerationFailed,
    PermissionRequired,
}
