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
    public static BatteryProbeResult FromSnapshot(MousePowerSnapshot snapshot)
    {
        var batteryHeader = snapshot.BatteryPercent is int batteryPercent
            ? $"Battery: {batteryPercent}%"
            : "Battery: unavailable";
        var statusHeader = snapshot.IsCharging switch
        {
            true => "Status: Charging",
            false => "Status: On battery",
            _ => "Status: unavailable",
        };
        var resultHeader = $"Last updated: {snapshot.Timestamp:HH:mm:ss}";
        var tooltipDeviceName = GetTooltipDeviceName(snapshot.DeviceDisplayName);
        var toolTipText = snapshot.BatteryPercent is int percent
            ? BuildTooltip(tooltipDeviceName, percent, snapshot.IsCharging)
            : "ViperLink\nBattery unavailable";
        var diagnosticsHeader = snapshot.IsSuccessful
            ? string.Empty
            : TruncateHeader($"Diagnostics: {LastDiagnosticLine(snapshot.Diagnostics)}");

        return new BatteryProbeResult(
            batteryHeader,
            statusHeader,
            $"Device: {snapshot.DeviceDisplayName}",
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

    private static string BuildTooltip(string deviceName, int batteryPercent, bool? isCharging)
    {
        var tooltip = $"ViperLink\n{deviceName}\nBattery: {batteryPercent}%";
        return isCharging switch
        {
            true => $"{tooltip}\nStatus: Charging",
            false => $"{tooltip}\nStatus: On battery",
            _ => tooltip,
        };
    }
}
