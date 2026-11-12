using System;
using System.Collections.Generic;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Tests.Fakes;

internal sealed class FakeHidDeviceEnumerator : IHidDeviceEnumerator
{
    private readonly Func<IReadOnlyList<HidDeviceInfo>> _enumerateFunc;

    public FakeHidDeviceEnumerator(IReadOnlyList<HidDeviceInfo> devices)
        : this(() => devices)
    {
    }

    public FakeHidDeviceEnumerator(Func<IReadOnlyList<HidDeviceInfo>> enumerateFunc)
    {
        _enumerateFunc = enumerateFunc;
    }

    public int EnumerateCallCount { get; private set; }

    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        EnumerateCallCount++;
        return _enumerateFunc();
    }
}

internal sealed class FakeHidTransport : IHidTransport
{
    private readonly Dictionary<string, Func<HidDeviceInfo, (IHidDeviceSession? Session, PowerFailureKind FailureKind, string Error)>> _handlers =
        new(StringComparer.Ordinal);

    public int OpenCallCount { get; private set; }

    public void RegisterSession(string devicePath, IHidDeviceSession session)
    {
        _handlers[devicePath] = _ => (session, PowerFailureKind.None, string.Empty);
    }

    public void RegisterOpenFailure(string devicePath, PowerFailureKind failureKind, string error)
    {
        _handlers[devicePath] = _ => (null, failureKind, error);
    }

    public bool TryOpen(
        HidDeviceInfo device,
        out IHidDeviceSession? session,
        out PowerFailureKind failureKind,
        out string error)
    {
        OpenCallCount++;

        if (_handlers.TryGetValue(device.DevicePath, out var handler))
        {
            (session, failureKind, error) = handler(device);
            return session is not null;
        }

        session = null;
        failureKind = PowerFailureKind.DeviceUnavailable;
        error = "No fake handler registered for path.";
        return false;
    }
}

internal sealed class FakeHidDeviceSession : IHidDeviceSession
{
    public delegate (bool Success, byte[] Response, PowerFailureKind FailureKind, string Error) ExchangeHandler(
        byte[] request,
        HidFeatureResponseClassifier? classifier);

    private readonly ExchangeHandler _exchangeHandler;

    public FakeHidDeviceSession(ExchangeHandler exchangeHandler)
    {
        _exchangeHandler = exchangeHandler;
    }

    public bool IsDisposed { get; private set; }

    public int ExchangeCallCount { get; private set; }

    public bool TrySetFeature(ReadOnlySpan<byte> request, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryGetFeature(Span<byte> response, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryExchangeFeatureReport(
        byte[] request,
        byte[] response,
        HidFeatureResponseClassifier? responseClassifier,
        out PowerFailureKind failureKind,
        out string error)
    {
        ExchangeCallCount++;
        var result = _exchangeHandler(request, responseClassifier);
        failureKind = result.FailureKind;
        error = result.Error;

        if (!result.Success)
        {
            return false;
        }

        Array.Clear(response);
        Array.Copy(result.Response, 0, response, 0, Math.Min(result.Response.Length, response.Length));

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
