using PowerGauge.Domain;

namespace PowerGauge.Devices;

public sealed record DeviceReading(int BatteryPercent, bool? IsCharging);

public sealed record DeviceProbeFailure(PowerFailureKind Kind, string Detail)
{
    public DeviceProbeFailure(PowerFailureKind kind)
        : this(kind, PowerFailureClassifier.GetDefaultDetail(kind))
    {
    }
}

public union DeviceProbeOutcome(DeviceReading, DeviceProbeFailure);
