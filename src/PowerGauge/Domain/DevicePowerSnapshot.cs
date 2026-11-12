using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerGauge.Domain;

public enum DeviceKind
{
    Mouse,
    Keyboard,
    Headset,
    Controller,
    Other,
}

public sealed record DevicePowerSnapshot(
    string DeviceKey,
    DeviceKind DeviceKind,
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
    IReadOnlyList<DevicePowerSnapshot> Devices,
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

public static class PowerFailureClassifier
{
    public static int GetPrecedence(PowerFailureKind failureKind)
    {
        return failureKind switch
        {
            PowerFailureKind.PermissionRequired => 0,
            PowerFailureKind.EnumerationFailed => 1,
            PowerFailureKind.DeviceSleeping => 2,
            PowerFailureKind.ProtocolTimeout => 3,
            PowerFailureKind.DeviceUnavailable => 4,
            PowerFailureKind.UnsupportedResponse => 5,
            _ => 6,
        };
    }

    public static string GetDefaultDetail(PowerFailureKind failureKind)
    {
        return failureKind switch
        {
            PowerFailureKind.None => "success",
            PowerFailureKind.PermissionRequired => "input monitoring required",
            PowerFailureKind.EnumerationFailed => "enumeration failed",
            PowerFailureKind.DeviceSleeping => "device sleeping",
            PowerFailureKind.ProtocolTimeout => "probe timed out",
            PowerFailureKind.DeviceUnavailable => "device unavailable",
            PowerFailureKind.UnsupportedResponse => "unsupported response",
            _ => "failed",
        };
    }
}
