using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ViperLink.App;

namespace ViperLink.App.Services;

public sealed class WindowsViperUltimateReader : IViperPowerReader
{
    private const int RazerVendorId = 0x1532;
    private const int WiredProductId = 0x007a;
    private const int WirelessProductId = 0x007b;
    private static readonly HashSet<int> SupportedProductIds = [WiredProductId, WirelessProductId];

    public MousePowerSnapshot Probe()
    {
        var timestamp = DateTimeOffset.Now;
        var diagnostics = new StringBuilder();

        IReadOnlyList<WindowsHidDeviceInfo> razerDevices;
        try
        {
            razerDevices = WindowsHidDeviceEnumerator
                .Enumerate()
                .Where(device => device.VendorId == RazerVendorId)
                .OrderBy(device => device.ProductId)
                .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"HID enumeration failed: {ex.Message}");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no compatible Razer HID device",
                null,
                null,
                false,
                PowerFailureKind.EnumerationFailed,
                "enumeration failed",
                diagnostics.ToString()));
        }

        diagnostics.AppendLine($"Detected {razerDevices.Count} Razer HID device(s).");
        foreach (var device in razerDevices)
        {
            diagnostics.AppendLine($"Found {DescribeDevice(device)}");
        }

        if (razerDevices.Count == 0)
        {
            diagnostics.AppendLine("No Razer HID devices found.");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no compatible Razer HID device",
                null,
                null,
                false,
                PowerFailureKind.DeviceUnavailable,
                "no devices",
                diagnostics.ToString()));
        }

        var candidateDevices = PrioritizeDevices(razerDevices);
        if (candidateDevices.Count == 0)
        {
            diagnostics.AppendLine("No supported Viper Ultimate device found.");
            return FinalizeSnapshot(new MousePowerSnapshot(
                timestamp,
                "no supported Viper Ultimate device",
                null,
                null,
                false,
                PowerFailureKind.DeviceUnavailable,
                "no supported device",
                diagnostics.ToString()));
        }

        diagnostics.AppendLine($"Prioritized {candidateDevices.Count} candidate device(s) for probing.");

        foreach (var device in candidateDevices)
        {
            diagnostics.AppendLine($"Trying {DescribeDevice(device)}");

            if (TryReadBattery(device, diagnostics, out var batteryPercent))
            {
                bool? isCharging = null;
                if (TryReadChargingStatus(device, diagnostics, out var charging))
                {
                    isCharging = charging;
                }

                if (ShouldDiscardBatteryReading(device, batteryPercent, isCharging))
                {
                    diagnostics.AppendLine("Discarding zero battery reading from wireless device while not charging.");
                    continue;
                }

                return FinalizeSnapshot(new MousePowerSnapshot(
                    timestamp,
                    GetDeviceDisplayName(device),
                    batteryPercent,
                    isCharging,
                    true,
                    PowerFailureKind.None,
                    "success",
                    diagnostics.ToString()));
            }
        }

        var (failureKind, resultDetail) = ClassifyProbeFailure(diagnostics.ToString());

        return FinalizeSnapshot(new MousePowerSnapshot(
            timestamp,
            $"tried {candidateDevices.Count}/{razerDevices.Count} Razer HID device(s)",
            null,
            null,
            false,
            failureKind,
            resultDetail,
            diagnostics.ToString()));
    }

    private static IReadOnlyList<WindowsHidDeviceInfo> PrioritizeDevices(IReadOnlyList<WindowsHidDeviceInfo> devices)
    {
        return devices
            .Where(IsLikelyWindowsTopLevelCollection)
            .OrderBy(GetDevicePriority)
            .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryReadBattery(WindowsHidDeviceInfo device, StringBuilder diagnostics, out int batteryPercent)
    {
        batteryPercent = 0;

        if (!TryReadPowerResponse(device, diagnostics, RazerProtocol.GetBatteryCommandId, "Battery", out var responsePayload))
        {
            return false;
        }

        batteryPercent = RazerProtocol.ParseBatteryPercent(responsePayload);
        diagnostics.AppendLine($"Battery byte {responsePayload[9]} parsed as {batteryPercent}%.");
        return true;
    }

    private static bool TryReadChargingStatus(WindowsHidDeviceInfo device, StringBuilder diagnostics, out bool isCharging)
    {
        isCharging = false;

        if (!TryReadPowerResponse(device, diagnostics, RazerProtocol.GetChargingStatusCommandId, "Charging", out var responsePayload))
        {
            return false;
        }

        isCharging = RazerProtocol.ParseChargingStatus(responsePayload);
        diagnostics.AppendLine($"Charging byte {responsePayload[11]} parsed as {(isCharging ? "charging" : "on battery")}.");
        return true;
    }

    private static bool TryReadPowerResponse(WindowsHidDeviceInfo device, StringBuilder diagnostics, byte commandId, string responseLabel, out byte[] responsePayload)
    {
        responsePayload = Array.Empty<byte>();

        var reportLength = Math.Max(RazerProtocol.ReportLength, device.FeatureReportLength);
        diagnostics.AppendLine($"Feature report length: {reportLength}");
        if (reportLength < RazerProtocol.ReportLength)
        {
            diagnostics.AppendLine("Skipped: feature report length is shorter than 90 bytes.");
            return false;
        }

        diagnostics.AppendLine("Using Windows native HID feature transport.");
        return TryReadPowerResponseViaWindowsHid(device, reportLength, diagnostics, commandId, responseLabel, out responsePayload);
    }

    private static bool TryReadPowerResponseViaWindowsHid(WindowsHidDeviceInfo device, int reportLength, StringBuilder diagnostics, byte commandId, string responseLabel, out byte[] responsePayload)
    {
        responsePayload = Array.Empty<byte>();
        var payloadOffset = reportLength == RazerProtocol.ReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;

        foreach (var transactionId in RazerProtocol.CandidateTransactionIds)
        {
            var requestPayload = RazerProtocol.BuildRequest(payloadLength, transactionId, RazerProtocol.PowerCommandClass, commandId);
            var request = new byte[reportLength];
            Array.Copy(requestPayload, 0, request, payloadOffset, requestPayload.Length);

            var response = new byte[reportLength];

            if (!WindowsHidFeatureTransport.TryExchangeFeatureReport(device.DevicePath, request, response, out var error))
            {
                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {error}");
                continue;
            }

            var payload = response.AsSpan(payloadOffset, payloadLength).ToArray();
            diagnostics.AppendLine($"{responseLabel} transaction 0x{transactionId:x2} response: {FormatReport(payload)}");

            if (!RazerProtocol.LooksLikeResponse(payload, transactionId, RazerProtocol.PowerCommandClass, commandId))
            {
                continue;
            }

            responsePayload = payload;
            return true;
        }

        return false;
    }

    private static string DescribeDevice(WindowsHidDeviceInfo device)
    {
        var name = device.ProductName;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({device.VendorId:x4}:{device.ProductId:x4}, feature={device.FeatureReportLength}, path={device.DevicePath})");
    }

    private static string FormatReport(IReadOnlyList<byte> report)
    {
        var bytesToShow = report.Take(RazerProtocol.ReportLength).Select(value => value.ToString("x2", CultureInfo.InvariantCulture));
        return string.Join(" ", bytesToShow);
    }

    private static MousePowerSnapshot FinalizeSnapshot(MousePowerSnapshot snapshot)
    {
        return snapshot with { LogFilePath = WriteDiagnosticsLog(snapshot) };
    }

    private static string? WriteDiagnosticsLog(MousePowerSnapshot snapshot)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppIdentity.LogDirectoryName);
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "probe.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {snapshot.Timestamp:O}");
            builder.AppendLine($"DeviceDisplayName: {snapshot.DeviceDisplayName}");
            builder.AppendLine($"BatteryPercent: {(snapshot.BatteryPercent is int battery ? battery : "n/a")}");
            builder.AppendLine($"IsCharging: {(snapshot.IsCharging is bool isCharging ? isCharging : "n/a")}");
            builder.AppendLine($"IsSuccessful: {snapshot.IsSuccessful}");
            builder.AppendLine($"FailureKind: {snapshot.FailureKind}");
            builder.AppendLine($"ResultDetail: {snapshot.ResultDetail}");

            builder.AppendLine("Diagnostics:");
            builder.AppendLine(snapshot.Diagnostics);
            File.WriteAllText(logPath, builder.ToString());
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    private static int GetDevicePriority(WindowsHidDeviceInfo device)
    {
        return device.ProductId switch
        {
            WiredProductId => 0,
            WirelessProductId => 1,
            _ => 2,
        };
    }

    private static string GetDeviceDisplayName(WindowsHidDeviceInfo device)
    {
        return device.ProductId switch
        {
            WiredProductId => "Razer Viper Ultimate (Wired)",
            WirelessProductId => "Razer Viper Ultimate (Wireless)",
            _ => device.ProductName,
        };
    }

    private static bool IsLikelyWindowsTopLevelCollection(WindowsHidDeviceInfo device)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (device.FeatureReportLength < RazerProtocol.ReportLength)
        {
            return false;
        }

        if (!SupportedProductIds.Contains(device.ProductId))
        {
            return false;
        }

        return !device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldDiscardBatteryReading(WindowsHidDeviceInfo device, int batteryPercent, bool? isCharging)
    {
        return device.ProductId == WirelessProductId
            && batteryPercent == 0
            && isCharging is not true;
    }

    private static (PowerFailureKind FailureKind, string ResultDetail) ClassifyProbeFailure(string diagnostics)
    {
        if (diagnostics.Contains("Discarding zero battery reading from wireless device while not charging.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceSleeping, "device sleeping");
        }

        if (diagnostics.Contains("Only placeholder response received.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceSleeping, "device sleeping");
        }

        if (diagnostics.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.ProtocolTimeout, "probe timed out");
        }

        if (diagnostics.Contains("Open failed.", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("CreateFile failed", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("No Razer HID devices found.", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerFailureKind.DeviceUnavailable, "device unavailable");
        }

        return (PowerFailureKind.UnsupportedResponse, "unsupported response");
    }

}
