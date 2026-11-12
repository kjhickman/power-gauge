using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PowerGauge.Devices;
using PowerGauge.Diagnostics;
using PowerGauge.Domain;

namespace PowerGauge.Application;

public sealed class CompositeDevicePowerReader : IDevicePowerReader
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
            if (snapshots.All(snapshot => snapshot.IsSuccessful))
            {
                return (PowerFailureKind.None, "success");
            }

            if (snapshots.Any(snapshot => snapshot.IsSuccessful))
            {
                return (PowerFailureKind.None, "partial");
            }

            var topSnapshotFailure = snapshots
                .OrderBy(snapshot => PowerFailureClassifier.GetPrecedence(snapshot.FailureKind))
                .First();

            return (topSnapshotFailure.FailureKind, topSnapshotFailure.ResultDetail);
        }

        if (providerResults.Count == 0)
        {
            return (PowerFailureKind.DeviceUnavailable, "no providers");
        }

        var topProviderFailure = providerResults
            .OrderBy(result => PowerFailureClassifier.GetPrecedence(result.FailureKind))
            .First();

        return (topProviderFailure.FailureKind, topProviderFailure.ResultDetail);
    }
}
