using System;

namespace ViperLink.App.Services;

public sealed record MousePowerSnapshot(
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    int? BatteryPercent,
    bool? IsCharging,
    bool IsSuccessful,
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
}
