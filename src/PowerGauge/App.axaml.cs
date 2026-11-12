using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using PowerGauge.Application;
using PowerGauge.Platform;
using PowerGauge.Platform.MacOS;
using PowerGauge.Platform.Windows;
using PowerGauge.Tray;
using PowerGauge.Updates;

namespace PowerGauge;

public partial class App : Avalonia.Application
{
    private readonly PowerPollingController _pollingController = new(CreatePowerReader());
    private readonly UpdateController _updateController = new();
    private TrayPresenter? _trayPresenter;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _updateTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        }

        _trayPresenter = new TrayPresenter(RefreshTrayState, HandleUpdateMenuAction, Quit);
        _updateController.StatusChanged += status => Dispatcher.UIThread.Post(() => _trayPresenter?.ApplyUpdateStatus(status));
        _trayPresenter.ApplyUpdateStatus(_updateController.CurrentStatus);

        Avalonia.Controls.TrayIcon.SetIcons(this, [_trayPresenter.TrayIcon]);
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

    private static IMousePowerReader CreatePowerReader()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRazerMouseReader();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsRazerMouseReader();
        }

        return new UnsupportedMousePowerReader();
    }
}
