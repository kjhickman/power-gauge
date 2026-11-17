using System;
using System.Collections.Generic;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Tests.Fakes;

internal sealed class FakeHidDeviceEnumerator(IReadOnlyList<HidDeviceInfo> devices) : IHidDeviceEnumerator
{
    public IReadOnlyList<HidDeviceInfo> Enumerate() => devices;
}

internal sealed class FakeHidTransport : IHidTransport
{
    private readonly Dictionary<string, Func<HidDeviceInfo, HidOpenResult>> _handlers =
        new(StringComparer.Ordinal);

    public void RegisterSession(string devicePath, IHidDeviceSession session)
    {
        _handlers[devicePath] = _ => new HidOpenResult(session);
    }

    public HidOpenResult Open(HidDeviceInfo device)
    {
        if (_handlers.TryGetValue(device.DevicePath, out var handler))
        {
            return handler(device);
        }

        return new HidOpenFailure(PowerFailureKind.DeviceUnavailable, "No fake handler registered for path.");
    }
}

internal sealed class FakeHidDeviceSession : IHidDeviceSession
{
    public delegate (bool Success, byte[] Response, PowerFailureKind FailureKind, string Error) ExchangeHandler(
        ReadOnlySpan<byte> request,
        HidFeatureResponseClassifier? classifier);

    private readonly ExchangeHandler _exchangeHandler;

    public FakeHidDeviceSession(ExchangeHandler exchangeHandler)
    {
        _exchangeHandler = exchangeHandler;
    }

    public bool IsDisposed { get; private set; }

    public bool TryExchangeFeatureReport(
        ReadOnlySpan<byte> request,
        Span<byte> response,
        HidFeatureResponseClassifier? responseClassifier,
        out PowerFailureKind failureKind,
        out string error)
    {
        var result = _exchangeHandler(request, responseClassifier);
        failureKind = result.FailureKind;
        error = result.Error;

        if (!result.Success)
        {
            return false;
        }

        response.Clear();
        result.Response.AsSpan(0, Math.Min(result.Response.Length, response.Length)).CopyTo(response);

        if (responseClassifier is not null && responseClassifier(request, response) != HidFeatureResponseStatus.Accepted)
        {
            failureKind = PowerFailureKind.DeviceSleeping;
            error = "Fake classifier rejected response.";
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
