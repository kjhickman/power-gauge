using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PowerGauge.Domain;

namespace PowerGauge.Tray;

public sealed record DeviceBatteryDisplay(
    string DeviceHeader,
    string BatteryStatusHeader);

public sealed record BatteryProbeResult(
    IReadOnlyList<DeviceBatteryDisplay> DeviceSections,
    string ResultHeader,
    string DiagnosticsHeader,
    bool ShowDiagnostics,
    string ToolTipText,
    string? LogFilePath = null,
    int? IconBatteryPercent = null)
{
    private const int MaxTooltipLength = 127;

    public static BatteryProbeResult FromReport(
        PowerProbeReport report,
        IReadOnlyDictionary<string, DevicePowerSuccess> lastSuccessfulByDevice)
    {
        if (report.Devices.Count == 0)
        {
            return BuildEmptyResult(report);
        }

        var resolvedDevices = report.Devices
            .Select(snapshot => ResolveDevice(snapshot, lastSuccessfulByDevice))
            .OrderBy(device => device.EffectiveBatteryPercent is null ? 1 : 0)
            .ThenBy(device => device.EffectiveBatteryPercent ?? int.MaxValue)
            .ThenBy(device => device.EffectiveIsCharging == true ? 1 : 0)
            .ThenBy(device => device.DisplayName, StringComparer.Ordinal)
            .ToArray();

        var iconBatteryPercent = resolvedDevices
            .Where(device => device.EffectiveBatteryPercent is not null)
            .Select(device => device.EffectiveBatteryPercent)
            .FirstOrDefault();

        var sections = resolvedDevices
            .Select(device => new DeviceBatteryDisplay(device.DisplayName, device.MenuBatteryStatusLine))
            .ToArray();

        var resultHeader = BuildResultHeader(report, resolvedDevices);
        var toolTipText = BuildMultiDeviceTooltip(resolvedDevices);
        var diagnosticSource = resolvedDevices.FirstOrDefault(device => device.HasUnexpectedFailure)
            ?? (iconBatteryPercent is null ? resolvedDevices.FirstOrDefault(device => device.Snapshot is DevicePowerFailureSnapshot) : null);
        var showDiagnostics = diagnosticSource is not null;
        var diagnosticsHeader = diagnosticSource is null
            ? string.Empty
            : TruncateHeader($"Diagnostics: {LastDiagnosticLine(diagnosticSource.Snapshot.Diagnostics)}");

        return new BatteryProbeResult(
            sections,
            resultHeader,
            diagnosticsHeader,
            showDiagnostics,
            toolTipText,
            report.LogFilePath,
            iconBatteryPercent);
    }

    private static BatteryProbeResult BuildEmptyResult(PowerProbeReport report)
    {
        var deviceDescription = report.FailureKind switch
        {
            PowerFailureKind.EnumerationFailed => "HID enumeration failed",
            _ when string.Equals(report.ResultDetail, "platform unsupported", StringComparison.Ordinal) => "Unsupported platform",
            _ => "No supported wireless device detected",
        };
        var statusLabel = FormatFailureLabel(report.FailureKind, fallbackTimestamp: null);
        var diagnosticsHeader = TruncateHeader($"Diagnostics: {LastDiagnosticLine(report.Diagnostics)}");
        var toolTipText = ClampTooltip($"{AppIdentity.ProductName}\n{deviceDescription}\nStatus: {statusLabel}");

        return new BatteryProbeResult(
            [new DeviceBatteryDisplay(deviceDescription, $"Battery: unavailable · {statusLabel}")],
            $"Last attempt: {report.Timestamp:HH:mm:ss}",
            diagnosticsHeader,
            true,
            toolTipText,
            report.LogFilePath,
            null);
    }

    private static ResolvedDeviceState ResolveDevice(
        DevicePowerSnapshot snapshot,
        IReadOnlyDictionary<string, DevicePowerSuccess> lastSuccessfulByDevice)
    {
        return snapshot switch
        {
            DevicePowerSuccess success => ResolveSuccessfulDevice(success),
            DevicePowerFailureSnapshot failure => ResolveFailedDevice(failure, lastSuccessfulByDevice),
        };
    }

    private static ResolvedDeviceState ResolveSuccessfulDevice(DevicePowerSuccess success)
    {
        var activeStatus = FormatActiveStatusLabel(success.IsCharging);
        return new ResolvedDeviceState(
            success,
            success.DeviceDisplayName,
            success.BatteryPercent,
            success.IsCharging,
            success.Timestamp,
            $"Battery: {success.BatteryPercent}% · {activeStatus}",
            activeStatus,
            HasUnexpectedFailure: false);
    }

    private static ResolvedDeviceState ResolveFailedDevice(
        DevicePowerFailureSnapshot failure,
        IReadOnlyDictionary<string, DevicePowerSuccess> lastSuccessfulByDevice)
    {
        var hasFallback = lastSuccessfulByDevice.TryGetValue(failure.DeviceKey, out var fallback);
        var displayName = fallback?.DeviceDisplayName ?? failure.DeviceDisplayName;
        int? effectiveBattery = fallback?.BatteryPercent;
        var effectiveIsCharging = fallback?.IsCharging;
        var statusLabel = FormatFailureLabel(failure.FailureKind, fallback?.Timestamp);
        var tooltipStatusLabel = FormatShortFailureLabel(failure.FailureKind);

        var menuBatteryStatusLine = effectiveBattery is int percent
            ? $"Battery: {percent}% · {statusLabel}"
            : $"Battery: unavailable · {statusLabel}";

        var hasUnexpectedFailure = !(failure.FailureKind == PowerFailureKind.DeviceSleeping && hasFallback);

        return new ResolvedDeviceState(
            failure,
            displayName,
            effectiveBattery,
            effectiveIsCharging,
            fallback?.Timestamp ?? failure.Timestamp,
            menuBatteryStatusLine,
            tooltipStatusLabel,
            hasUnexpectedFailure);
    }

    private static string BuildResultHeader(PowerProbeReport report, IReadOnlyList<ResolvedDeviceState> devices)
    {
        if (devices.Any(device => device.Snapshot is DevicePowerSuccess))
        {
            return $"Last updated: {report.Timestamp:HH:mm:ss}";
        }

        var latestCached = devices
            .Where(device => device.EffectiveBatteryPercent is not null)
            .Select(device => (DateTimeOffset?)device.EffectiveTimestamp)
            .Max();

        if (latestCached is DateTimeOffset cachedTimestamp)
        {
            return $"Last updated: {cachedTimestamp:HH:mm:ss} (checked {report.Timestamp:HH:mm:ss})";
        }

        return $"Last attempt: {report.Timestamp:HH:mm:ss}";
    }

    private static string BuildMultiDeviceTooltip(IReadOnlyList<ResolvedDeviceState> devices)
    {
        var builder = new StringBuilder(AppIdentity.ProductName);

        foreach (var device in devices)
        {
            var shortName = GetTooltipDeviceName(device.DisplayName);
            builder.Append('\n');
            if (device.EffectiveBatteryPercent is int percent)
            {
                builder.Append($"{shortName}: {percent}% ({device.TooltipStatusLabel})");
            }
            else
            {
                builder.Append($"{shortName}: {device.TooltipStatusLabel}");
            }
        }

        return ClampTooltip(builder.ToString());
    }

    private static string FormatActiveStatusLabel(bool? isCharging)
    {
        return isCharging switch
        {
            true => "Charging",
            false => "On battery",
            _ => "Unavailable",
        };
    }

    private static string FormatFailureLabel(PowerFailureKind failureKind, DateTimeOffset? fallbackTimestamp)
    {
        var baseLabel = FormatShortFailureLabel(failureKind);
        return fallbackTimestamp is DateTimeOffset timestamp
            ? $"{baseLabel} (last known {timestamp:HH:mm:ss})"
            : baseLabel;
    }

    private static string FormatShortFailureLabel(PowerFailureKind failureKind)
    {
        return failureKind switch
        {
            PowerFailureKind.DeviceSleeping => "Sleeping",
            PowerFailureKind.DeviceUnavailable => "Device unavailable",
            PowerFailureKind.ProtocolTimeout => "Timed out",
            PowerFailureKind.PermissionRequired => "Input Monitoring required",
            PowerFailureKind.UnsupportedResponse => "Unable to refresh",
            PowerFailureKind.EnumerationFailed => "HID error",
            _ => "Waiting for update",
        };
    }

    private static string LastDiagnosticLine(string diagnostics)
    {
        var lines = diagnostics
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? "no details";
    }

    private static string TruncateHeader(string value)
    {
        return value.Length <= 80 ? value : string.Concat(value.AsSpan(0, 77), "...");
    }

    private static string ClampTooltip(string value)
    {
        return value.Length <= MaxTooltipLength
            ? value
            : string.Concat(value.AsSpan(0, MaxTooltipLength - 3), "...");
    }

    private static string GetTooltipDeviceName(string deviceDisplayName)
    {
        var detailsStart = deviceDisplayName.IndexOf(" (", StringComparison.Ordinal);
        return detailsStart > 0 ? deviceDisplayName[..detailsStart] : deviceDisplayName;
    }

    private sealed record ResolvedDeviceState(
        DevicePowerSnapshot Snapshot,
        string DisplayName,
        int? EffectiveBatteryPercent,
        bool? EffectiveIsCharging,
        DateTimeOffset EffectiveTimestamp,
        string MenuBatteryStatusLine,
        string TooltipStatusLabel,
        bool HasUnexpectedFailure);
}
