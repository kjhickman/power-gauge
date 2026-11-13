using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PowerGauge.Domain;

namespace PowerGauge.Tray;

public sealed record DeviceBatteryDisplay(
    string DeviceHeader,
    string? BatteryStatusHeader = null);

public sealed record BatteryProbeResult(
    IReadOnlyList<DeviceBatteryDisplay> DeviceSections,
    string ResultHeader,
    string DiagnosticsHeader,
    bool ShowDiagnostics,
    string ToolTipText,
    string? LogFilePath = null,
    int? IconBatteryPercent = null)
{
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
        var diagnosticSource = resolvedDevices.FirstOrDefault(device => device.HasUnexpectedFailure);
        var showDiagnostics = diagnosticSource is not null;
        var diagnosticsHeader = diagnosticSource is null
            ? string.Empty
            : Truncate($"Diagnostics: {LastDiagnosticLine(diagnosticSource.Snapshot.Diagnostics)}", 80);

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
        var isUnsupportedPlatform = string.Equals(report.ResultDetail, "platform unsupported", StringComparison.Ordinal);
        var hasFailure = report.FailureKind != PowerFailureKind.None || isUnsupportedPlatform;

        var deviceDescription = report.FailureKind switch
        {
            PowerFailureKind.EnumerationFailed => "HID enumeration failed",
            _ when isUnsupportedPlatform => "Unsupported platform",
            _ => "No supported devices",
        };
        var diagnosticsHeader = hasFailure
            ? Truncate($"Diagnostics: {LastDiagnosticLine(report.Diagnostics)}", 80)
            : string.Empty;
        var toolTipText = hasFailure
            ? $"{AppIdentity.ProductName}\n{deviceDescription}\nStatus: {FormatFailureLabel(report.FailureKind, fallbackTimestamp: null)}"
            : $"{AppIdentity.ProductName}\n{deviceDescription}";

        return new BatteryProbeResult(
            [new DeviceBatteryDisplay(deviceDescription)],
            $"Last checked: {report.Timestamp:HH:mm:ss}",
            diagnosticsHeader,
            hasFailure,
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
        var menuStatus = success.IsCharging == true
            ? $"{success.BatteryPercent}% · Charging"
            : $"{success.BatteryPercent}%";

        var tooltipStatus = success.IsCharging == true ? "Charging" : null;

        return new ResolvedDeviceState(
            success,
            success.DeviceDisplayName,
            success.BatteryPercent,
            success.IsCharging,
            success.Timestamp,
            menuStatus,
            tooltipStatus,
            HasUnexpectedFailure: false);
    }

    private static ResolvedDeviceState ResolveFailedDevice(
        DevicePowerFailureSnapshot failure,
        IReadOnlyDictionary<string, DevicePowerSuccess> lastSuccessfulByDevice)
    {
        lastSuccessfulByDevice.TryGetValue(failure.DeviceKey, out var fallback);
        var displayName = fallback?.DeviceDisplayName ?? failure.DeviceDisplayName;
        int? effectiveBattery = fallback?.BatteryPercent;
        var effectiveIsCharging = fallback?.IsCharging;
        var statusLabel = FormatFailureLabel(failure.FailureKind, fallback?.Timestamp);
        var tooltipStatusLabel = FormatShortFailureLabel(failure.FailureKind);

        var menuBatteryStatusLine = effectiveBattery is int percent
            ? $"{percent}% · {statusLabel}"
            : statusLabel;

        return new ResolvedDeviceState(
            failure,
            displayName,
            effectiveBattery,
            effectiveIsCharging,
            fallback?.Timestamp ?? failure.Timestamp,
            menuBatteryStatusLine,
            tooltipStatusLabel,
            HasUnexpectedFailure: failure.FailureKind != PowerFailureKind.DeviceSleeping);
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
                builder.Append(device.TooltipStatusLabel is { } status
                    ? $"{shortName}: {percent}% ({status})"
                    : $"{shortName}: {percent}%");
            }
            else
            {
                builder.Append($"{shortName}: {device.TooltipStatusLabel}");
            }
        }

        return builder.ToString();
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

    internal static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength - 3), "...");
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
        string? TooltipStatusLabel,
        bool HasUnexpectedFailure);
}
