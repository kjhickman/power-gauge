using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PowerGauge.Devices;
using PowerGauge.Diagnostics;
using PowerGauge.Domain;

namespace PowerGauge.Application;

public sealed class CompositeDevicePowerReader
{
    private readonly IReadOnlyList<IDevicePowerProvider> _providers;
    private readonly ProbeLogWriter _probeLogWriter;

    public CompositeDevicePowerReader(
        IReadOnlyList<IDevicePowerProvider> providers,
        ProbeLogWriter probeLogWriter)
    {
        _providers = providers;
        _probeLogWriter = probeLogWriter;
    }

    public PowerProbeReport Probe()
    {
        var timestamp = DateTimeOffset.Now;
        if (_providers.Count == 0)
        {
            var unsupportedReport = new PowerProbeReport(
                timestamp,
                [],
                PowerFailureKind.UnsupportedResponse,
                "platform unsupported",
                $"{AppIdentity.ProductName} does not support this operating system yet.");

            return unsupportedReport with { LogFilePath = _probeLogWriter.Write(unsupportedReport) };
        }

        var snapshots = new List<DevicePowerSnapshot>();
        var providerResults = new List<ProviderProbeResult>(_providers.Count);
        var diagnostics = new StringBuilder();

        foreach (var provider in _providers)
        {
            var result = provider.Probe(timestamp);
            providerResults.Add(result);
            snapshots.AddRange(result.Devices);

            if (!string.IsNullOrEmpty(result.Diagnostics))
            {
                diagnostics.Append(result.Diagnostics);
            }
        }

        var (failureKind, resultDetail) = SummarizeOutcome(snapshots, providerResults);
        var report = new PowerProbeReport(
            timestamp,
            snapshots,
            failureKind,
            resultDetail,
            diagnostics.ToString());

        return report with { LogFilePath = _probeLogWriter.Write(report) };
    }

    private static (PowerFailureKind FailureKind, string ResultDetail) SummarizeOutcome(
        IReadOnlyList<DevicePowerSnapshot> snapshots,
        IReadOnlyList<ProviderProbeResult> providerResults)
    {
        if (snapshots.Count > 0)
        {
            return PowerFailureClassifier.Summarize(snapshots);
        }

        var topProviderFailure = providerResults
            .OrderBy(result => PowerFailureClassifier.GetPrecedence(result.FailureKind))
            .First();

        return (topProviderFailure.FailureKind, topProviderFailure.ResultDetail);
    }
}
