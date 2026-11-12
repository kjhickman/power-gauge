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

public interface IHidTransport
{
    bool TryOpen(
        HidDeviceInfo device,
        out IHidDeviceSession? session,
        out PowerFailureKind failureKind,
        out string error);
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
