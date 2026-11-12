using System;
using System.Threading;
using System.Threading.Tasks;
using PowerGauge.Application;
using PowerGauge.Devices;
using PowerGauge.Diagnostics;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Platform.Windows.Hid;
using PowerGauge.Tray;
using Xunit;

namespace PowerGauge.Tests;

public sealed class DeviceChangeMonitorTests
{
    [Fact]
    public async Task PowerPollingController_CoalescesBurstOfDeviceChangeEvents_IntoSingleRefresh()
    {
        var probeCount = 0;
        var currentCharging = false;
        var resultPublished = new TaskCompletionSource<BatteryProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = new CallbackProvider(ts =>
        {
            Interlocked.Increment(ref probeCount);
            var snapshot = new DevicePowerSuccess(
                "viper-ultimate",
                DeviceKind.Mouse,
                ts,
                currentCharging ? "Razer Viper Ultimate (Wired)" : "Razer Viper Ultimate (Wireless)",
                85,
                currentCharging,
                "ok");

            return new ProviderProbeResult([snapshot], PowerFailureKind.None, "success", "ok");
        });

        using var fakeMonitor = new FakeDeviceChangeMonitor();
        using var controller = new PowerPollingController(
            new CompositeDevicePowerReader([provider], ProbeLogWriter.Disabled),
            fakeMonitor,
            debounceDelay: TimeSpan.FromMilliseconds(50));

        controller.ResultUpdated += result => resultPublished.TrySetResult(result);

        // Simulate plugging in a composite USB mouse that fires 10 rapid HID interface arrival events
        currentCharging = true;
        for (var i = 0; i < 10; i++)
        {
            fakeMonitor.Trigger();
        }

        var updated = await resultPublished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(80); // Ensure no trailing duplicate probes run

        Assert.Equal(1, Volatile.Read(ref probeCount));
        Assert.Equal("Razer Viper Ultimate (Wired)", updated.DeviceSections[0].DeviceHeader);
        Assert.Contains("Charging", updated.DeviceSections[0].BatteryStatusHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsHidDeviceChangeMonitor_RegistersWithCfgMgr32OnWindows_AndDisposesCleanly()
    {
        using var monitor = new WindowsHidDeviceChangeMonitor();
        if (OperatingSystem.IsWindows())
        {
            Assert.True(monitor.IsRegistered);
        }

        monitor.Dispose();
        Assert.False(monitor.IsRegistered);
    }

    private sealed class FakeDeviceChangeMonitor : IDeviceChangeMonitor
    {
        public event Action? DevicesChanged;

        public void Trigger() => DevicesChanged?.Invoke();

        public void Dispose()
        {
        }
    }

    private sealed class CallbackProvider(Func<DateTimeOffset, ProviderProbeResult> callback) : IDevicePowerProvider
    {
        public ProviderProbeResult Probe(DateTimeOffset timestamp) => callback(timestamp);
    }
}
