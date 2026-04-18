using System;
using System.Linq;

namespace ViperLink.App.Services;

public sealed record BatteryProbeResult(
    string BatteryHeader,
    string StatusHeader,
    string DeviceHeader,
    string ResultHeader,
    string DiagnosticsHeader,
    bool ShowDiagnostics,
    string ToolTipText,
    string? LogFilePath = null)
{
    public static BatteryProbeResult FromSnapshot(MousePowerSnapshot snapshot, MousePowerSnapshot? fallbackSnapshot = null)
    {
        var displaySnapshot = snapshot.IsSuccessful ? snapshot : fallbackSnapshot ?? snapshot;
        var batteryHeader = displaySnapshot.BatteryPercent is int batteryPercent
            ? $"Battery: {batteryPercent}%"
            : "Battery: unavailable";
        var statusHeader = BuildStatusHeader(snapshot, displaySnapshot, fallbackSnapshot is not null && !snapshot.IsSuccessful);
        var resultHeader = BuildResultHeader(snapshot, displaySnapshot, fallbackSnapshot is not null && !snapshot.IsSuccessful);
        var tooltipDeviceName = GetTooltipDeviceName(displaySnapshot.DeviceDisplayName);
        var toolTipText = displaySnapshot.BatteryPercent is int percent
            ? BuildTooltip(tooltipDeviceName, percent, statusHeader)
            : BuildUnavailableTooltip(statusHeader);
        var diagnosticsHeader = snapshot.IsSuccessful
            ? string.Empty
            : TruncateHeader($"Diagnostics: {LastDiagnosticLine(snapshot.Diagnostics)}");

        return new BatteryProbeResult(
            batteryHeader,
            statusHeader,
            $"Device: {displaySnapshot.DeviceDisplayName}",
            resultHeader,
            diagnosticsHeader,
            !snapshot.IsSuccessful,
            toolTipText,
            snapshot.LogFilePath);
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

    private static string GetTooltipDeviceName(string deviceDisplayName)
    {
        var detailsStart = deviceDisplayName.IndexOf(" (", StringComparison.Ordinal);
        return detailsStart > 0 ? deviceDisplayName[..detailsStart] : deviceDisplayName;
    }

    private static string BuildTooltip(string deviceName, int batteryPercent, string statusHeader)
    {
        var tooltip = $"ViperLink\n{deviceName}\nBattery: {batteryPercent}%";
        return $"{tooltip}\n{statusHeader}";
    }

    private static string BuildUnavailableTooltip(string statusHeader)
    {
        return $"ViperLink\nBattery unavailable\n{statusHeader}";
    }

    private static string BuildStatusHeader(MousePowerSnapshot snapshot, MousePowerSnapshot displaySnapshot, bool isUsingFallback)
    {
        if (snapshot.IsSuccessful)
        {
            return displaySnapshot.IsCharging switch
            {
                true => "Status: Charging",
                false => "Status: On battery",
                _ => "Status: unavailable",
            };
        }

        return snapshot.FailureKind switch
        {
            PowerFailureKind.DeviceSleeping => isUsingFallback
                ? "Status: Sleeping (showing last known reading)"
                : "Status: Sleeping",
            PowerFailureKind.DeviceUnavailable => isUsingFallback
                ? "Status: Device unavailable (showing last known reading)"
                : "Status: Device unavailable",
            PowerFailureKind.ProtocolTimeout => isUsingFallback
                ? "Status: Timed out (showing last known reading)"
                : "Status: Timed out",
            PowerFailureKind.UnsupportedResponse => isUsingFallback
                ? "Status: Unable to refresh (showing last known reading)"
                : "Status: Unable to refresh",
            PowerFailureKind.EnumerationFailed => isUsingFallback
                ? "Status: HID error (showing last known reading)"
                : "Status: HID error",
            _ => isUsingFallback
                ? "Status: Waiting for update (showing last known reading)"
                : "Status: Waiting for update",
        };
    }

    private static string BuildResultHeader(MousePowerSnapshot snapshot, MousePowerSnapshot displaySnapshot, bool isUsingFallback)
    {
        if (snapshot.IsSuccessful)
        {
            return $"Last updated: {snapshot.Timestamp:HH:mm:ss}";
        }

        if (isUsingFallback)
        {
            return $"Last updated: {displaySnapshot.Timestamp:HH:mm:ss} (refresh failed at {snapshot.Timestamp:HH:mm:ss})";
        }

        return $"Last attempt: {snapshot.Timestamp:HH:mm:ss}";
    }
}
