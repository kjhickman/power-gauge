using System;
using System.Text;
using ViperLink.App.Platform.Abstractions;
using ViperLink.App.Services;

namespace ViperLink.App.Razer.Devices;

internal sealed class ViperUltimateDriver : IRazerMouseDriver
{
    private const int WiredProductId = 0x007a;
    private const int WirelessProductId = 0x007b;

    public bool Supports(HidDeviceInfo device)
    {
        return device.ProductId is WiredProductId or WirelessProductId
            && device.FeatureReportLength >= RazerProtocol.ReportLength
            && !device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }

    public int GetPriority(HidDeviceInfo device)
    {
        return device.ProductId switch
        {
            WiredProductId => 0,
            WirelessProductId => 1,
            _ => 2,
        };
    }

    public string GetDisplayName(HidDeviceInfo device)
    {
        return device.ProductId switch
        {
            WiredProductId => "Razer Viper Ultimate (Wired)",
            WirelessProductId => "Razer Viper Ultimate (Wireless)",
            _ => device.ProductName,
        };
    }

    public bool TryProbe(HidDeviceInfo device, IHidFeatureTransport featureTransport, StringBuilder diagnostics, out int batteryPercent, out bool? isCharging)
    {
        batteryPercent = 0;
        isCharging = null;

        if (!TryReadPowerResponse(device, featureTransport, diagnostics, RazerProtocol.GetBatteryCommandId, "Battery", out var batteryResponse))
        {
            return false;
        }

        batteryPercent = RazerProtocol.ParseBatteryPercent(batteryResponse);
        diagnostics.AppendLine($"Battery byte {batteryResponse[9]} parsed as {batteryPercent}%.");

        if (TryReadPowerResponse(device, featureTransport, diagnostics, RazerProtocol.GetChargingStatusCommandId, "Charging", out var chargingResponse))
        {
            isCharging = RazerProtocol.ParseChargingStatus(chargingResponse);
            diagnostics.AppendLine($"Charging byte {chargingResponse[11]} parsed as {(isCharging.Value ? "charging" : "on battery")}.");
        }

        if (ShouldDiscardBatteryReading(device, batteryPercent, isCharging))
        {
            diagnostics.AppendLine("Discarding zero battery reading from wireless device while not charging.");
            return false;
        }

        return true;
    }

    private static bool TryReadPowerResponse(HidDeviceInfo device, IHidFeatureTransport featureTransport, StringBuilder diagnostics, byte commandId, string responseLabel, out byte[] responsePayload)
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
        var payloadOffset = reportLength == RazerProtocol.ReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;

        foreach (var transactionId in RazerProtocol.CandidateTransactionIds)
        {
            var requestPayload = RazerProtocol.BuildRequest(payloadLength, transactionId, RazerProtocol.PowerCommandClass, commandId);
            var request = new byte[reportLength];
            Array.Copy(requestPayload, 0, request, payloadOffset, requestPayload.Length);

            var response = new byte[reportLength];
            if (!featureTransport.TryExchangeFeatureReport(device.DevicePath, request, response, out var error))
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

    private static bool ShouldDiscardBatteryReading(HidDeviceInfo device, int batteryPercent, bool? isCharging)
    {
        return device.ProductId == WirelessProductId
            && batteryPercent == 0
            && isCharging is not true;
    }

    private static string FormatReport(System.Collections.Generic.IReadOnlyList<byte> report)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < report.Count && i < RazerProtocol.ReportLength; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(report[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
