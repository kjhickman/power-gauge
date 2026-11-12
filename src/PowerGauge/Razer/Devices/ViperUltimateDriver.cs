using System;
using System.Text;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Razer.Protocol;

namespace PowerGauge.Razer.Devices;

internal sealed class ViperUltimateDriver : IRazerMouseDriver
{
    private const int WiredProductId = 0x007a;
    private const int WirelessProductId = 0x007b;
    private static readonly byte[] CandidateTransactionIds = [0x1f, 0xff, 0x3f, 0x00];

    public string DriverId => "viper-ultimate";

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

        if (!RazerProtocol.TryReadPowerResponse(device, featureTransport, CandidateTransactionIds, diagnostics, RazerProtocol.GetBatteryCommandId, "Battery", out var batteryResponse))
        {
            return false;
        }

        batteryPercent = RazerProtocol.ParseBatteryPercent(batteryResponse);
        diagnostics.AppendLine($"Battery byte {batteryResponse[9]} parsed as {batteryPercent}%.");

        if (RazerProtocol.TryReadPowerResponse(device, featureTransport, CandidateTransactionIds, diagnostics, RazerProtocol.GetChargingStatusCommandId, "Charging", out var chargingResponse))
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

    private static bool ShouldDiscardBatteryReading(HidDeviceInfo device, int batteryPercent, bool? isCharging)
    {
        return device.ProductId == WirelessProductId
            && batteryPercent == 0
            && isCharging is not true;
    }
}
