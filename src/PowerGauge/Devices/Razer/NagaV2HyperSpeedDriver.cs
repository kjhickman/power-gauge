using System.Text;
using PowerGauge.Devices.Hid;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Razer;

public sealed class NagaV2HyperSpeedDriver : IHidDeviceDriver
{
    private const int ReceiverProductId = 0x00b4;
    private static readonly byte[] CandidateTransactionIds = [0x1f, 0x00, 0x3f];

    public string DriverId => "naga-v2-hyperspeed";

    public DeviceKind DeviceKind => DeviceKind.Mouse;

    public bool MatchesVendor(int vendorId) => vendorId == RazerProtocol.VendorId;

    public bool Supports(HidDeviceInfo device)
    {
        return device.VendorId == RazerProtocol.VendorId
            && device.ProductId == ReceiverProductId
            && device.FeatureReportLength >= RazerProtocol.ReportLength
            && !HidUsagePages.IsKeyboard(device);
    }

    public int GetPriority(HidDeviceInfo device) => 0;

    public string GetDisplayName(HidDeviceInfo device) => "Razer Naga V2 HyperSpeed (Wireless)";

    public DeviceProbeOutcome Probe(HidDeviceInfo device, IHidDeviceSession session, StringBuilder diagnostics)
    {
        var batteryResult = RazerProtocol.ReadPowerResponse(
            device,
            session,
            CandidateTransactionIds,
            diagnostics,
            RazerProtocol.GetBatteryCommandId,
            "Battery");

        if (batteryResult is not RazerPowerPayload(var batteryResponse))
        {
            return new DeviceProbeFailure(RazerProtocol.MapFailureKind(batteryResult, isWirelessDevice: true));
        }

        var batteryPercent = RazerProtocol.ParseBatteryPercent(batteryResponse);
        diagnostics.AppendLine($"Battery byte {batteryResponse[9]} parsed as {batteryPercent}%.");

        if (batteryPercent == 0)
        {
            diagnostics.AppendLine("Discarding zero battery reading from wireless device while not charging.");
            return new DeviceProbeFailure(PowerFailureKind.DeviceSleeping);
        }

        diagnostics.AppendLine("Charging status skipped (AA battery device; reporting on battery).");
        return new DeviceReading(batteryPercent, IsCharging: false);
    }
}
