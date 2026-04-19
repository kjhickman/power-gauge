using PowerGauge.Domain;

namespace PowerGauge.Application;

public interface IMousePowerReader
{
    MousePowerSnapshot Probe();
}
