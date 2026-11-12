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

public closed record DevicePowerSnapshot(
    string DeviceKey,
    DeviceKind DeviceKind,
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    string Diagnostics);

public sealed record DevicePowerSuccess(
    string DeviceKey,
    DeviceKind DeviceKind,
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    int BatteryPercent,
    bool? IsCharging,
    string Diagnostics)
    : DevicePowerSnapshot(DeviceKey, DeviceKind, Timestamp, DeviceDisplayName, Diagnostics);

public sealed record DevicePowerFailureSnapshot(
    string DeviceKey,
    DeviceKind DeviceKind,
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    PowerFailureKind FailureKind,
    string ResultDetail,
    string Diagnostics)
    : DevicePowerSnapshot(DeviceKey, DeviceKind, Timestamp, DeviceDisplayName, Diagnostics);

public sealed record PowerProbeReport(
    DateTimeOffset Timestamp,
    IReadOnlyList<DevicePowerSnapshot> Devices,
    PowerFailureKind FailureKind,
    string ResultDetail,
    string Diagnostics,
    string? LogFilePath = null);

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

    public static (PowerFailureKind FailureKind, string ResultDetail) Summarize(IReadOnlyList<DevicePowerSnapshot> snapshots)
    {
        if (snapshots.All(snapshot => snapshot is DevicePowerSuccess))
        {
            return (PowerFailureKind.None, "success");
        }

        if (snapshots.Any(snapshot => snapshot is DevicePowerSuccess))
        {
            return (PowerFailureKind.None, "partial");
        }

        var bestFailure = snapshots
            .OfType<DevicePowerFailureSnapshot>()
            .OrderBy(snapshot => GetPrecedence(snapshot.FailureKind))
            .First();

        return (bestFailure.FailureKind, bestFailure.ResultDetail);
    }
}
