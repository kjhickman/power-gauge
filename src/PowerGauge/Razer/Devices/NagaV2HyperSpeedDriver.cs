using System;
using System.Text;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Razer.Protocol;

namespace PowerGauge.Razer.Devices;

internal sealed class NagaV2HyperSpeedDriver : IRazerMouseDriver
{
    private const int ReceiverProductId = 0x00b4;
    private static readonly byte[] CandidateTransactionIds = [0x1f, 0x00, 0x3f];

    public string DriverId => "naga-v2-hyperspeed";

    public bool Supports(HidDeviceInfo device)
    {
        return device.ProductId == ReceiverProductId
            && device.FeatureReportLength >= RazerProtocol.ReportLength
            && !device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }

    public int GetPriority(HidDeviceInfo device) => 0;

    public string GetDisplayName(HidDeviceInfo device) => "Razer Naga V2 HyperSpeed (Wireless)";

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

        if (batteryPercent == 0)
        {
            diagnostics.AppendLine("Discarding zero battery reading from wireless device while not charging.");
            return false;
        }

        isCharging = false;
        diagnostics.AppendLine("Charging status skipped (AA battery device; reporting on battery).");
        return true;
    }
}
