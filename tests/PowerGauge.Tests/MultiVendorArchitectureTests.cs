using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PowerGauge.Application;
using PowerGauge.Devices;
using PowerGauge.Devices.Hid;
using PowerGauge.Devices.Razer;
using PowerGauge.Diagnostics;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Tests.Fakes;
using Xunit;

namespace PowerGauge.Tests;

public sealed class MultiVendorArchitectureTests
{
    [Fact]
    public void HidDevicePowerProvider_EnumeratesOnce_AcrossMultipleVendorsAndDeviceKinds()
    {
        const int headsetVendorId = 0x1038;
        const int headsetProductId = 0x2202;
        const int vendorUsagePage = 0xffc0;

        var razerMouse = new HidDeviceInfo(
            "path-razer-mouse",
            RazerProtocol.VendorId,
            0x00b4,
            90,
            "Razer Naga V2 HyperSpeed",
            HidUsagePages.GenericDesktop,
            HidUsagePages.MouseUsage);

        var unrelatedSystemHid = new HidDeviceInfo(
            "path-acpi-button",
            0x045e,
            0x0001,
            0,
            "System Control");

        var wirelessHeadset = new HidDeviceInfo(
            "path-headset-vendor",
            headsetVendorId,
            headsetProductId,
            8,
            "Test Wireless Headset",
            vendorUsagePage,
            0x0001);

        var enumerator = new FakeHidDeviceEnumerator([razerMouse, unrelatedSystemHid, wirelessHeadset]);
        var transport = new FakeHidTransport();

        transport.RegisterSession(razerMouse.DevicePath, new FakeHidDeviceSession((request, _) =>
        {
            var response = new byte[90];
            response[0] = 0x02;
            response[1] = request[1];
            response[6] = request[6];
            response[7] = request[7];
            response[9] = 204; // 80%
            return (true, response, PowerFailureKind.None, string.Empty);
        }));

        transport.RegisterSession(wirelessHeadset.DevicePath, new FakeHidDeviceSession((_, _) =>
        {
            // Simulated 8-byte vendor report completely unrelated to Razer's 90-byte layout
            return (true, [0x06, 0x18, 0x23, 0x01, 0x00, 0x00, 0x00, 0x00], PowerFailureKind.None, string.Empty);
        }));

        var headsetDriver = new FakeVendorHeadsetDriver(headsetVendorId, headsetProductId, vendorUsagePage);
        var provider = new HidDevicePowerProvider(
            enumerator,
            transport,
            [new NagaV2HyperSpeedDriver(), headsetDriver]);

        var reader = new CompositeDevicePowerReader([provider], ProbeLogWriter.Disabled);
        var report = reader.Probe();

        Assert.Equal(1, enumerator.EnumerateCallCount);
        Assert.Equal(PowerFailureKind.None, report.FailureKind);
        Assert.Equal(2, report.Devices.Count);

        Assert.Collection(
            report.Devices,
            mouse =>
            {
                Assert.Equal("naga-v2-hyperspeed", mouse.DeviceKey);
                Assert.Equal(DeviceKind.Mouse, mouse.DeviceKind);
                Assert.Equal(80, mouse.BatteryPercent);
            },
            headset =>
            {
                Assert.Equal("test-vendor:headset", headset.DeviceKey);
                Assert.Equal(DeviceKind.Headset, headset.DeviceKind);
                Assert.Equal(35, headset.BatteryPercent);
                Assert.Equal(true, headset.IsCharging);
            });
    }

    [Fact]
    public async Task CompositeReader_CombinesHidAndNonHidProviders_AndPicksLowestBatteryForTrayIcon()
    {
        var hidProvider = new StubProvider(ts => new ProviderProbeResult(
            [
                new DevicePowerSnapshot(
                    "razer:mouse",
                    DeviceKind.Mouse,
                    ts,
                    "Razer Viper Ultimate (Wireless)",
                    72,
                    false,
                    true,
                    PowerFailureKind.None,
                    "success",
                    "ok")
            ],
            PowerFailureKind.None,
            "success",
            "HID ok\n"));

        var bleKeyboardProvider = new StubProvider(ts => new ProviderProbeResult(
            [
                new DevicePowerSnapshot(
                    "ble:keychron-k2",
                    DeviceKind.Keyboard,
                    ts,
                    "Keychron K2 Pro (Bluetooth)",
                    28,
                    false,
                    true,
                    PowerFailureKind.None,
                    "success",
                    "ok")
            ],
            PowerFailureKind.None,
            "success",
            "BLE ok\n"));

        var reader = new CompositeDevicePowerReader([hidProvider, bleKeyboardProvider], ProbeLogWriter.Disabled);
        var controller = new PowerPollingController(reader);

        var uiResult = await controller.RefreshAsync();

        Assert.Equal(28, uiResult.IconBatteryPercent);
        Assert.Equal(2, uiResult.DeviceSections.Count);
        Assert.Equal("Keychron K2 Pro (Bluetooth)", uiResult.DeviceSections[0].DeviceHeader);
        Assert.Equal("Razer Viper Ultimate (Wireless)", uiResult.DeviceSections[1].DeviceHeader);
        Assert.False(uiResult.ShowDiagnostics);
    }

    [Fact]
    public async Task PollingController_PreservesLastKnownBatteryQuietly_WhenDeviceGoesToSleep()
    {
        var isSleeping = false;
        var provider = new StubProvider(ts =>
        {
            var snapshot = isSleeping
                ? new DevicePowerSnapshot(
                    "steelseries:arctis",
                    DeviceKind.Headset,
                    ts,
                    "SteelSeries Arctis Nova 7",
                    null,
                    null,
                    false,
                    PowerFailureKind.DeviceSleeping,
                    "device sleeping",
                    "Headset powered off / sleeping.")
                : new DevicePowerSnapshot(
                    "steelseries:arctis",
                    DeviceKind.Headset,
                    ts,
                    "SteelSeries Arctis Nova 7",
                    64,
                    false,
                    true,
                    PowerFailureKind.None,
                    "success",
                    "Headset awake.");

            return new ProviderProbeResult(
                [snapshot],
                snapshot.FailureKind,
                snapshot.ResultDetail,
                snapshot.Diagnostics);
        });

        var controller = new PowerPollingController(new CompositeDevicePowerReader([provider], ProbeLogWriter.Disabled));

        var awakeResult = await controller.RefreshAsync();
        Assert.Equal(64, awakeResult.IconBatteryPercent);
        Assert.False(awakeResult.ShowDiagnostics);

        isSleeping = true;
        var sleepingResult = await controller.RefreshAsync();
        Assert.Equal(64, sleepingResult.IconBatteryPercent);
        Assert.Contains("64%", sleepingResult.DeviceSections[0].BatteryStatusHeader, StringComparison.Ordinal);
        Assert.Contains("Sleeping", sleepingResult.DeviceSections[0].BatteryStatusHeader, StringComparison.Ordinal);
        Assert.False(sleepingResult.ShowDiagnostics);
    }

    [Fact]
    public void HidDevicePowerProvider_MultiInterfaceFailure_PrefersSleepingOrPermissionOverUnsupported()
    {
        var interface1 = new HidDeviceInfo("dev-col01", RazerProtocol.VendorId, 0x00b4, 90, "Naga");
        var interface2 = new HidDeviceInfo("dev-col02", RazerProtocol.VendorId, 0x00b4, 90, "Naga");

        var transport = new FakeHidTransport();
        // Interface 1 fails with generic unsupported response
        transport.RegisterSession(interface1.DevicePath, new FakeHidDeviceSession(
            (_, _) => (false, [], PowerFailureKind.UnsupportedResponse, "unsupported collection")));
        // Interface 2 reports placeholder sleeping response
        transport.RegisterSession(interface2.DevicePath, new FakeHidDeviceSession(
            (_, _) => (false, [], PowerFailureKind.DeviceSleeping, "Only placeholder response received.")));

        var provider = new HidDevicePowerProvider(
            new FakeHidDeviceEnumerator([interface1, interface2]),
            transport,
            [new NagaV2HyperSpeedDriver()]);

        var result = provider.Probe(DateTimeOffset.UtcNow);
        var snapshot = Assert.Single(result.Devices);

        Assert.Equal(PowerFailureKind.DeviceSleeping, snapshot.FailureKind);
        Assert.Equal("device sleeping", snapshot.ResultDetail);
    }

    private sealed class FakeVendorHeadsetDriver : IHidDeviceDriver
    {
        private readonly int _vendorId;
        private readonly int _productId;
        private readonly int _usagePage;

        public FakeVendorHeadsetDriver(int vendorId, int productId, int usagePage)
        {
            _vendorId = vendorId;
            _productId = productId;
            _usagePage = usagePage;
        }

        public string DriverId => "test-vendor:headset";

        public DeviceKind DeviceKind => DeviceKind.Headset;

        public bool MatchesVendor(int vendorId) => vendorId == _vendorId;

        public bool Supports(HidDeviceInfo device) =>
            device.VendorId == _vendorId
            && device.ProductId == _productId
            && device.UsagePage == _usagePage;

        public string GetDisplayName(HidDeviceInfo device) => "Test Wireless Headset";

        public DeviceProbeOutcome Probe(HidDeviceInfo device, IHidDeviceSession session, StringBuilder diagnostics)
        {
            var response = new byte[8];
            if (!session.TryExchangeFeatureReport([0x06, 0x18], response, null, out var failureKind, out var error))
            {
                diagnostics.AppendLine(error);
                return DeviceProbeOutcome.Failure(failureKind);
            }

            return DeviceProbeOutcome.Success(batteryPercent: response[2], isCharging: response[3] == 0x01);
        }
    }

    private sealed class StubProvider : IDevicePowerProvider
    {
        private readonly Func<DateTimeOffset, ProviderProbeResult> _probeFunc;

        public StubProvider(Func<DateTimeOffset, ProviderProbeResult> probeFunc)
        {
            _probeFunc = probeFunc;
        }

        public ProviderProbeResult Probe(DateTimeOffset timestamp) => _probeFunc(timestamp);
    }
}
