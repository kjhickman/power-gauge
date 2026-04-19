namespace PowerGauge.Platform.Abstractions;

public interface IHidFeatureTransport
{
    bool TryExchangeFeatureReport(string devicePath, byte[] request, byte[] response, out string error);
}
