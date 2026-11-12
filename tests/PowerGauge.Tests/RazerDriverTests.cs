using System;
using PowerGauge.Devices.Hid;
using PowerGauge.Devices.Razer;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Tests.Fakes;
using Xunit;

namespace PowerGauge.Tests;

public sealed class RazerDriverTests
{
    [Fact]
    public void ViperUltimate_PrefersWiredOverWireless_AndReusesSingleSessionAcrossCommands()
    {
        var wirelessDevice = new HidDeviceInfo(
            "macos-entry-100",
            RazerProtocol.VendorId,
            0x007b,
            90,
            "Razer Viper Ultimate",
            HidUsagePages.GenericDesktop,
            HidUsagePages.MouseUsage);

        var wiredDevice = new HidDeviceInfo(
            "macos-entry-101",
            RazerProtocol.VendorId,
            0x007a,
            90,
            "Razer Viper Ultimate",
            HidUsagePages.GenericDesktop,
            HidUsagePages.MouseUsage);

        var wiredSession = CreateRazerSession(
            status: 0x02,
            transactionId: 0x1f,
            rawBatteryByte: 204, // 80%
            isCharging: true);

        var transport = new FakeHidTransport();
        transport.RegisterSession(wiredDevice.DevicePath, wiredSession);

        var provider = new HidDevicePowerProvider(
            new FakeHidDeviceEnumerator([wirelessDevice, wiredDevice]),
            transport,
            HidDevicePowerProvider.CreateDefaultDrivers());

        var result = provider.Probe(DateTimeOffset.UtcNow);

        var snapshot = Assert.Single(result.Devices);
        Assert.Equal("viper-ultimate", snapshot.DeviceKey);
        Assert.Equal(DeviceKind.Mouse, snapshot.DeviceKind);
        Assert.Equal("Razer Viper Ultimate (Wired)", snapshot.DeviceDisplayName);
        Assert.True(snapshot.IsSuccessful);
        Assert.Equal(80, snapshot.BatteryPercent);
        Assert.Equal(true, snapshot.IsCharging);
        Assert.Equal(1, transport.OpenCallCount);
        Assert.Equal(2, wiredSession.ExchangeCallCount);
        Assert.True(wiredSession.IsDisposed);
    }

    [Fact]
    public void ViperUltimate_FiltersKeyboardUsageCrossPlatform_WithoutNeedingWindowsPath()
    {
        var macOsKeyboardCollection = new HidDeviceInfo(
            "4294969201", // macOS numeric IORegistryEntryID (no "\kbd" substring)
            RazerProtocol.VendorId,
            0x007b,
            90,
            "Razer Viper Ultimate",
            HidUsagePages.GenericDesktop,
            HidUsagePages.KeyboardUsage);

        var driver = new ViperUltimateDriver();

        Assert.False(driver.Supports(macOsKeyboardCollection));
    }

    [Fact]
    public void ViperUltimate_WirelessZeroBatteryNotCharging_ClassifiesAsDeviceSleeping()
    {
        var wirelessDevice = new HidDeviceInfo(
            "\\\\?\\hid#vid_1532&pid_007b&mi_00",
            RazerProtocol.VendorId,
            0x007b,
            91,
            "Razer Viper Ultimate",
            HidUsagePages.GenericDesktop,
            HidUsagePages.MouseUsage);

        var session = CreateRazerSession(
            status: 0x02,
            transactionId: 0x1f,
            rawBatteryByte: 0,
            isCharging: false,
            prependReportId: true);

        var transport = new FakeHidTransport();
        transport.RegisterSession(wirelessDevice.DevicePath, session);

        var provider = new HidDevicePowerProvider(
            new FakeHidDeviceEnumerator([wirelessDevice]),
            transport,
            [new ViperUltimateDriver()]);

        var result = provider.Probe(DateTimeOffset.UtcNow);

        var snapshot = Assert.Single(result.Devices);
        Assert.False(snapshot.IsSuccessful);
        Assert.Equal(PowerFailureKind.DeviceSleeping, snapshot.FailureKind);
        Assert.Equal("device sleeping", snapshot.ResultDetail);
    }

    [Theory]
    [InlineData(0x007b, PowerFailureKind.DeviceSleeping)]
    [InlineData(0x007a, PowerFailureKind.ProtocolTimeout)]
    public void ViperUltimate_MapsFirmwareCommandTimeoutByConnectionKind(int productId, PowerFailureKind expectedFailureKind)
    {
        var device = new HidDeviceInfo(
            "device-1",
            RazerProtocol.VendorId,
            productId,
            90,
            "Razer Viper Ultimate");

        var session = new FakeHidDeviceSession((request, _) =>
        {
            var response = new byte[90];
            response[0] = 0x04; // Razer command timeout
            response[1] = request[1];
            response[6] = request[6];
            response[7] = request[7];
            return (true, response, PowerFailureKind.None, string.Empty);
        });

        var transport = new FakeHidTransport();
        transport.RegisterSession(device.DevicePath, session);

        var provider = new HidDevicePowerProvider(
            new FakeHidDeviceEnumerator([device]),
            transport,
            [new ViperUltimateDriver()]);

        var result = provider.Probe(DateTimeOffset.UtcNow);
        var snapshot = Assert.Single(result.Devices);

        Assert.False(snapshot.IsSuccessful);
        Assert.Equal(expectedFailureKind, snapshot.FailureKind);
    }

    [Fact]
    public void NagaV2HyperSpeed_ReportsBatteryAndOnBatteryChargingState()
    {
        var receiver = new HidDeviceInfo(
            "\\\\?\\hid#vid_1532&pid_00b4&mi_00",
            RazerProtocol.VendorId,
            0x00b4,
            91,
            "Razer Naga V2 HyperSpeed",
            HidUsagePages.GenericDesktop,
            HidUsagePages.MouseUsage);

        var session = CreateRazerSession(
            status: 0x02,
            transactionId: 0x1f,
            rawBatteryByte: 128, // 50%
            isCharging: false,
            prependReportId: true);

        var transport = new FakeHidTransport();
        transport.RegisterSession(receiver.DevicePath, session);

        var provider = new HidDevicePowerProvider(
            new FakeHidDeviceEnumerator([receiver]),
            transport,
            [new NagaV2HyperSpeedDriver()]);

        var result = provider.Probe(DateTimeOffset.UtcNow);

        var snapshot = Assert.Single(result.Devices);
        Assert.True(snapshot.IsSuccessful);
        Assert.Equal("naga-v2-hyperspeed", snapshot.DeviceKey);
        Assert.Equal(50, snapshot.BatteryPercent);
        Assert.Equal(false, snapshot.IsCharging);
        Assert.Equal(1, session.ExchangeCallCount); // Skips charging query on AA mouse
    }

    [Fact]
    public void RazerProtocol_ClassifyFeatureResponse_DistinguishesAcceptedPendingAndUnrelated()
    {
        var request = new byte[91];
        request[0] = 0x00;
        request[1 + 1] = 0x1f;
        request[1 + 6] = 0x07;
        request[1 + 7] = 0x80;

        var busyResponse = (byte[])request.Clone();
        busyResponse[1 + 0] = 0x01;
        Assert.Equal(HidFeatureResponseStatus.Pending, RazerProtocol.ClassifyFeatureResponse(request, busyResponse));

        var zeroPlaceholder = (byte[])request.Clone();
        zeroPlaceholder[1 + 0] = 0x00;
        Assert.Equal(HidFeatureResponseStatus.Pending, RazerProtocol.ClassifyFeatureResponse(request, zeroPlaceholder));

        var otherCommand = (byte[])request.Clone();
        otherCommand[1 + 7] = 0x84;
        Assert.Equal(HidFeatureResponseStatus.UnrelatedCommand, RazerProtocol.ClassifyFeatureResponse(request, otherCommand));

        var validResponse = (byte[])request.Clone();
        validResponse[1 + 0] = 0x02;
        validResponse[1 + 9] = 0x80;
        Assert.Equal(HidFeatureResponseStatus.Accepted, RazerProtocol.ClassifyFeatureResponse(request, validResponse));
    }

    private static FakeHidDeviceSession CreateRazerSession(
        byte status,
        byte transactionId,
        byte rawBatteryByte,
        bool isCharging,
        bool prependReportId = false)
    {
        var offset = prependReportId ? 1 : 0;
        var length = 90 + offset;

        return new FakeHidDeviceSession((request, _) =>
        {
            var reqTx = request[offset + 1];
            var commandClass = request[offset + 6];
            var commandId = request[offset + 7];

            var response = new byte[length];
            response[offset + 0] = reqTx == transactionId ? status : (byte)0x04;
            response[offset + 1] = reqTx;
            response[offset + 6] = commandClass;
            response[offset + 7] = commandId;

            if (commandId == RazerProtocol.GetBatteryCommandId)
            {
                response[offset + 9] = rawBatteryByte;
            }
            else if (commandId == RazerProtocol.GetChargingStatusCommandId)
            {
                response[offset + 11] = isCharging ? (byte)0x01 : (byte)0x00;
            }

            return (true, response, PowerFailureKind.None, string.Empty);
        });
    }
}
