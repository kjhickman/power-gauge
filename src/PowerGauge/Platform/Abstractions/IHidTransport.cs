using System;
using PowerGauge.Domain;

namespace PowerGauge.Platform.Abstractions;

public enum HidFeatureResponseStatus
{
    Accepted,
    Pending,
    UnrelatedCommand,
}

public delegate HidFeatureResponseStatus HidFeatureResponseClassifier(
    ReadOnlySpan<byte> request,
    ReadOnlySpan<byte> response);

public sealed record HidOpenFailure(PowerFailureKind Kind, string Error);

public union HidOpenResult(IHidDeviceSession, HidOpenFailure);

public interface IHidTransport
{
    HidOpenResult Open(HidDeviceInfo device);
}

public interface IHidDeviceSession : IDisposable
{
    bool TrySetFeature(ReadOnlySpan<byte> request, out string error);

    bool TryGetFeature(Span<byte> response, out string error);

    bool TryExchangeFeatureReport(
        byte[] request,
        byte[] response,
        HidFeatureResponseClassifier? responseClassifier,
        out PowerFailureKind failureKind,
        out string error);
}
