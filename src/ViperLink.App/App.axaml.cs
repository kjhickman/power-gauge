using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using ViperLink.App.Application;
using ViperLink.App.Services;
using ViperLink.App.Tray;

namespace ViperLink.App;

public partial class App : Avalonia.Application
{
    private readonly PowerPollingController _pollingController = new(new WindowsViperUltimateReader());
    private TrayPresenter? _trayPresenter;
    private DispatcherTimer? _refreshTimer;

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

        _trayPresenter = new TrayPresenter(RefreshTrayState, Quit);
        Avalonia.Controls.TrayIcon.SetIcons(this, [_trayPresenter.TrayIcon]);
        StartRefreshLoop();
        RefreshTrayState();

        base.OnFrameworkInitializationCompleted();
    }

    private void StartRefreshLoop()
    {
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };

        _refreshTimer.Tick += (_, _) => RefreshTrayState();
        _refreshTimer.Start();
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
}
