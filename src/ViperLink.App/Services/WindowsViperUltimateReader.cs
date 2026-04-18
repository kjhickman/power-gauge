using HidSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ViperLink.App.Services;

public sealed class WindowsViperUltimateReader : IViperPowerReader
{
    private const int RazerVendorId = 0x1532;
    private static readonly HashSet<int> PreferredProductIds = [0x007a, 0x007b];

    public MousePowerSnapshot Probe()
    {
        var timestamp = DateTimeOffset.Now;
        var diagnostics = new StringBuilder();

        IReadOnlyList<HidDevice> razerDevices;
        try
        {
            razerDevices = DeviceList.Local
                .GetHidDevices()
                .Where(device => device.VendorID == RazerVendorId)
                .OrderBy(device => device.ProductID)
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
                "no devices",
                diagnostics.ToString()));
        }

        var candidateDevices = PrioritizeDevices(razerDevices);
        diagnostics.AppendLine($"Prioritized {candidateDevices.Count} candidate device(s) for probing.");

        foreach (var device in candidateDevices)
        {
            diagnostics.AppendLine($"Trying {DescribeDevice(device)}");

            if (TryReadBattery(device, diagnostics, out var batteryPercent))
            {
                return FinalizeSnapshot(new MousePowerSnapshot(
                    timestamp,
                    DescribeDevice(device),
                    batteryPercent,
                    null,
                    true,
                    "success",
                    diagnostics.ToString()));
            }
        }

        return FinalizeSnapshot(new MousePowerSnapshot(
            timestamp,
            $"tried {candidateDevices.Count}/{razerDevices.Count} Razer HID device(s)",
            null,
            null,
            false,
            "no battery response",
            diagnostics.ToString()));
    }

    private static IReadOnlyList<HidDevice> PrioritizeDevices(IReadOnlyList<HidDevice> devices)
    {
        if (OperatingSystem.IsWindows())
        {
            var topLevelCollections = devices
                .Where(device => IsLikelyWindowsTopLevelCollection(device))
                .ToArray();

            if (topLevelCollections.Length > 0)
            {
                return topLevelCollections;
            }
        }

        var preferred = devices
            .Where(device => PreferredProductIds.Contains(device.ProductID) || LooksLikeViperUltimate(device))
            .ToArray();

        if (preferred.Length > 0)
        {
            return preferred;
        }

        var reportSized = devices
            .Where(device => device.GetMaxFeatureReportLength() >= RazerProtocol.ReportLength)
            .ToArray();

        return reportSized.Length > 0 ? reportSized : devices;
    }

    private static bool TryReadBattery(HidDevice device, StringBuilder diagnostics, out int batteryPercent)
    {
        batteryPercent = 0;

        var reportLength = Math.Max(RazerProtocol.ReportLength, device.GetMaxFeatureReportLength());
        diagnostics.AppendLine($"Feature report length: {reportLength}");
        if (reportLength < RazerProtocol.ReportLength)
        {
            diagnostics.AppendLine("Skipped: feature report length is shorter than 90 bytes.");
            return false;
        }

        if (WindowsHidFeatureTransport.IsSupported && IsLikelyWindowsTopLevelCollection(device))
        {
            diagnostics.AppendLine("Using Windows native HID feature transport.");
            return TryReadBatteryViaWindowsHid(device, reportLength, diagnostics, out batteryPercent);
        }

        if (!device.TryOpen(out HidStream stream))
        {
            diagnostics.AppendLine("Open failed.");
            return false;
        }

        using (stream)
        {
            stream.ReadTimeout = 1000;
            stream.WriteTimeout = 1000;

            foreach (var transactionId in RazerProtocol.CandidateTransactionIds)
            {
                var request = RazerProtocol.BuildRequest(reportLength, transactionId, RazerProtocol.PowerCommandClass, RazerProtocol.GetBatteryCommandId);
                var response = new byte[reportLength];

                try
                {
                    stream.SetFeature(request);
                    stream.GetFeature(response);
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {ex.Message}");
                    continue;
                }

                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} response: {FormatReport(response)}");

                if (!RazerProtocol.LooksLikeResponse(response, transactionId, RazerProtocol.PowerCommandClass, RazerProtocol.GetBatteryCommandId))
                {
                    continue;
                }

                batteryPercent = RazerProtocol.ParseBatteryPercent(response);
                diagnostics.AppendLine($"Battery byte {response[9]} parsed as {batteryPercent}%.");
                return true;
            }
        }

        return false;
    }

    private static bool TryReadBatteryViaWindowsHid(HidDevice device, int reportLength, StringBuilder diagnostics, out int batteryPercent)
    {
        batteryPercent = 0;
        var payloadOffset = reportLength == RazerProtocol.ReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;

        foreach (var transactionId in RazerProtocol.CandidateTransactionIds)
        {
            var requestPayload = RazerProtocol.BuildRequest(payloadLength, transactionId, RazerProtocol.PowerCommandClass, RazerProtocol.GetBatteryCommandId);
            var request = new byte[reportLength];
            Array.Copy(requestPayload, 0, request, payloadOffset, requestPayload.Length);

            var response = new byte[reportLength];

            if (!WindowsHidFeatureTransport.TryExchangeFeatureReport(device.DevicePath, request, response, out var error))
            {
                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {error}");
                continue;
            }

            var responsePayload = response.AsSpan(payloadOffset, payloadLength).ToArray();
            diagnostics.AppendLine($"Transaction 0x{transactionId:x2} response: {FormatReport(responsePayload)}");

            if (!RazerProtocol.LooksLikeResponse(responsePayload, transactionId, RazerProtocol.PowerCommandClass, RazerProtocol.GetBatteryCommandId))
            {
                continue;
            }

            batteryPercent = RazerProtocol.ParseBatteryPercent(responsePayload);
            diagnostics.AppendLine($"Battery byte {responsePayload[9]} parsed as {batteryPercent}%.");
            return true;
        }

        return false;
    }

    private static string DescribeDevice(HidDevice device)
    {
        var name = device.GetFriendlyName();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({device.VendorID:x4}:{device.ProductID:x4}, feature={device.GetMaxFeatureReportLength()}, path={device.DevicePath})");
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
                "ViperLink");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "probe.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {snapshot.Timestamp:O}");
            builder.AppendLine($"DeviceDisplayName: {snapshot.DeviceDisplayName}");
            builder.AppendLine($"BatteryPercent: {(snapshot.BatteryPercent is int battery ? battery : "n/a")}");
            builder.AppendLine($"IsCharging: {(snapshot.IsCharging is bool isCharging ? isCharging : "n/a")}");
            builder.AppendLine($"IsSuccessful: {snapshot.IsSuccessful}");
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

    private static bool LooksLikeViperUltimate(HidDevice device)
    {
        var name = device.GetFriendlyName();
        return name.Contains("Viper Ultimate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Viper Ultimate Dongle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyWindowsTopLevelCollection(HidDevice device)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (device.GetMaxFeatureReportLength() < RazerProtocol.ReportLength)
        {
            return false;
        }

        if (!PreferredProductIds.Contains(device.ProductID) && !LooksLikeViperUltimate(device))
        {
            return false;
        }

        return !device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }

}

internal static class HidDeviceExtensions
{
    public static string GetFriendlyName(this HidDevice device)
    {
        var productName = device.GetProductName();
        if (!string.IsNullOrWhiteSpace(productName))
        {
            return productName;
        }

        return "Unknown Razer device";
    }
}
