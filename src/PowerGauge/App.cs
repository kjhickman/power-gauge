using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PowerGauge.Application;
using PowerGauge.Devices;
using PowerGauge.Devices.Hid;
using PowerGauge.Diagnostics;
using PowerGauge.Platform.Abstractions;
using PowerGauge.Platform.MacOS.Hid;
using PowerGauge.Platform.Windows.Hid;
using PowerGauge.Tray;
using PowerGauge.Updates;

namespace PowerGauge;

public sealed class App : Avalonia.Application
{
    private readonly PowerPollingController _pollingController = new(CreatePowerReader());
    private readonly UpdateController _updateController = new();
    private TrayPresenter? _trayPresenter;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _updateTimer;

    public override void Initialize()
    {
        Name = AppIdentity.ProductName;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        _trayPresenter = new TrayPresenter(RefreshTrayState, HandleUpdateMenuAction, Quit);
        _updateController.StatusChanged += status => Dispatcher.UIThread.Post(() => _trayPresenter?.ApplyUpdateStatus(status));
        _trayPresenter.ApplyUpdateStatus(_updateController.CurrentStatus);

        TrayIcon.SetIcons(this, [_trayPresenter.TrayIcon]);
        StartRefreshLoop();
        StartUpdateCheckLoop();
        RefreshTrayState();

        base.OnFrameworkInitializationCompleted();
    }

    private void StartRefreshLoop()
    {
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2),
        };

        _refreshTimer.Tick += (_, _) => RefreshTrayState();
        _refreshTimer.Start();
    }

    private void StartUpdateCheckLoop()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(6),
        };

        _updateTimer.Tick += async (_, _) => await _updateController.CheckForUpdatesAsync(isManualCheck: false);
        _updateTimer.Start();

        _ = CheckForUpdatesAfterStartupAsync();
    }

    private async Task CheckForUpdatesAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        await _updateController.CheckForUpdatesAsync(isManualCheck: false);
    }

    private async void HandleUpdateMenuAction()
    {
        await _updateController.HandleMenuActionAsync(Quit);
    }

    private async void RefreshTrayState()
    {
        if (_trayPresenter is null)
        {
            return;
        }

        if (!_pollingController.HasSuccessfulSnapshot)
        {
            _trayPresenter.Apply(_pollingController.CreateInitialResult());
        }

        _trayPresenter.Apply(await _pollingController.RefreshAsync());
    }

    private void Quit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static CompositeDevicePowerReader CreatePowerReader()
    {
        IReadOnlyList<IDevicePowerProvider> providers = OperatingSystem.IsWindows()
            ? [CreateHidProvider(new WindowsHidDeviceEnumerator(), new WindowsHidTransport())]
            : OperatingSystem.IsMacOS()
                ? [CreateHidProvider(new MacOsHidDeviceEnumerator(), new MacOsHidTransport())]
                : [];

        return new CompositeDevicePowerReader(providers, new ProbeLogWriter());
    }

    private static HidDevicePowerProvider CreateHidProvider(IHidDeviceEnumerator deviceEnumerator, IHidTransport transport)
    {
        return new HidDevicePowerProvider(
            deviceEnumerator,
            transport,
            HidDevicePowerProvider.CreateDefaultDrivers());
    }
}
