using System.Text;
using PowerGauge.Devices.Hid;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Razer;

public sealed class ViperUltimateDriver : IHidDeviceDriver
{
    private const int WiredProductId = 0x007a;
    private const int WirelessProductId = 0x007b;
    private static readonly byte[] CandidateTransactionIds = [0x1f, 0xff, 0x3f, 0x00];

    public string DriverId => "viper-ultimate";

    public DeviceKind DeviceKind => DeviceKind.Mouse;

    public bool MatchesVendor(int vendorId) => vendorId == RazerProtocol.VendorId;

    public bool Supports(HidDeviceInfo device)
    {
        return device.VendorId == RazerProtocol.VendorId
            && device.ProductId is WiredProductId or WirelessProductId
            && device.FeatureReportLength >= RazerProtocol.ReportLength
            && !HidUsagePages.IsKeyboard(device);
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

    public DeviceProbeOutcome Probe(HidDeviceInfo device, IHidDeviceSession session, StringBuilder diagnostics)
    {
        var isWireless = device.ProductId == WirelessProductId;
        var batteryStatus = RazerProtocol.ReadPowerResponse(
            device,
            session,
            CandidateTransactionIds,
            diagnostics,
            RazerProtocol.GetBatteryCommandId,
            "Battery",
            out var batteryResponse);

        if (batteryStatus != RazerReadStatus.Success)
        {
            return DeviceProbeOutcome.Failure(RazerProtocol.MapReadFailure(batteryStatus, isWireless));
        }

        var batteryPercent = RazerProtocol.ParseBatteryPercent(batteryResponse);
        diagnostics.AppendLine($"Battery byte {batteryResponse[9]} parsed as {batteryPercent}%.");

        bool? isCharging = null;
        var chargingStatus = RazerProtocol.ReadPowerResponse(
            device,
            session,
            CandidateTransactionIds,
            diagnostics,
            RazerProtocol.GetChargingStatusCommandId,
            "Charging",
            out var chargingResponse);

        if (chargingStatus == RazerReadStatus.Success)
        {
            isCharging = RazerProtocol.ParseChargingStatus(chargingResponse);
            diagnostics.AppendLine($"Charging byte {chargingResponse[11]} parsed as {(isCharging.Value ? "charging" : "on battery")}.");
        }

        if (isWireless && batteryPercent == 0 && isCharging is not true)
        {
            diagnostics.AppendLine("Discarding zero battery reading from wireless device while not charging.");
            return DeviceProbeOutcome.Failure(PowerFailureKind.DeviceSleeping);
        }

        return DeviceProbeOutcome.Success(batteryPercent, isCharging);
    }
}
