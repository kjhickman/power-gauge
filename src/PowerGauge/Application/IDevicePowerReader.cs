using PowerGauge.Domain;

namespace PowerGauge.Application;

public interface IDevicePowerReader
{
    PowerProbeReport Probe();
}
