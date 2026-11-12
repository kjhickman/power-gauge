using PowerGauge.Domain;

namespace PowerGauge.Devices;

public readonly record struct DeviceProbeOutcome(
    bool IsSuccessful,
    int? BatteryPercent,
    bool? IsCharging,
    PowerFailureKind FailureKind,
    string ResultDetail)
{
    public static DeviceProbeOutcome Success(int batteryPercent, bool? isCharging) =>
        new(true, batteryPercent, isCharging, PowerFailureKind.None, "success");

    public static DeviceProbeOutcome Failure(PowerFailureKind failureKind, string? resultDetail = null) =>
        new(false, null, null, failureKind, resultDetail ?? PowerFailureClassifier.GetDefaultDetail(failureKind));
}
