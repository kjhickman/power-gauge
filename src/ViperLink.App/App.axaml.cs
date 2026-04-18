using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using ViperLink.App.Services;

namespace ViperLink.App;

public partial class App : Application
{
    private readonly IViperPowerReader _powerReader = new WindowsViperUltimateReader();
    private MousePowerSnapshot? _lastSuccessfulSnapshot;
    private TrayIcon? _statusTrayIcon;
    private NativeMenuItem? _batteryMenuItem;
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _deviceMenuItem;
    private NativeMenuItem? _resultMenuItem;
    private NativeMenuItem? _diagnosticsMenuItem;
    private NativeMenuItem? _logMenuItem;
    private DispatcherTimer? _refreshTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        CreateTrayIcon();
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
        if (_lastSuccessfulSnapshot is null)
        {
            ApplyResult(new BatteryProbeResult(
                "Battery: probing...",
                "Status: probing...",
                "Device: scanning Razer HID devices...",
                $"Last updated: {DateTimeOffset.Now:HH:mm:ss}",
                string.Empty,
                false,
                "ViperLink\nRefreshing battery status..."));
        }

        var snapshot = await System.Threading.Tasks.Task.Run(_powerReader.Probe);
        if (snapshot.IsSuccessful)
        {
            _lastSuccessfulSnapshot = snapshot;
        }

        ApplyResult(BatteryProbeResult.FromSnapshot(snapshot, _lastSuccessfulSnapshot));
    }

    private void ApplyResult(BatteryProbeResult result)
    {
        if (_batteryMenuItem is null
            || _statusMenuItem is null
            || _deviceMenuItem is null
            || _resultMenuItem is null
            || _diagnosticsMenuItem is null
            || _logMenuItem is null
            || _statusTrayIcon is null)
        {
            return;
        }

        _batteryMenuItem.Header = result.BatteryHeader;
        _statusMenuItem.Header = result.StatusHeader;
        _deviceMenuItem.Header = result.DeviceHeader;
        _resultMenuItem.Header = result.ResultHeader;
        _diagnosticsMenuItem.Header = result.DiagnosticsHeader;
        _diagnosticsMenuItem.IsVisible = result.ShowDiagnostics;
        _logMenuItem.Header = result.LogFilePath is null ? "Log: unavailable" : $"Log: {result.LogFilePath}";
        _logMenuItem.IsVisible = result.ShowDiagnostics;
        _statusTrayIcon.ToolTipText = result.ToolTipText;
    }

    private void RefreshNowClicked(object? sender, EventArgs e)
    {
        RefreshTrayState();
    }

    private void QuitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void CreateTrayIcon()
    {
        _batteryMenuItem = CreateReadOnlyItem("Battery: probing...");
        _statusMenuItem = CreateReadOnlyItem("Status: probing...");
        _deviceMenuItem = CreateReadOnlyItem("Device: probing...");
        _resultMenuItem = CreateReadOnlyItem("Last updated: waiting to start");
        _diagnosticsMenuItem = CreateReadOnlyItem(string.Empty);
        _logMenuItem = CreateReadOnlyItem(string.Empty);

        var menu = new NativeMenu();
        menu.Add(_batteryMenuItem);
        menu.Add(_statusMenuItem);
        menu.Add(_deviceMenuItem);
        menu.Add(_resultMenuItem);
        menu.Add(_diagnosticsMenuItem);
        menu.Add(_logMenuItem);
        menu.Add(new NativeMenuItemSeparator());

        var refreshMenuItem = new NativeMenuItem("Refresh now");
        refreshMenuItem.Click += RefreshNowClicked;
        menu.Add(refreshMenuItem);

        var quitMenuItem = new NativeMenuItem("Quit");
        quitMenuItem.Click += QuitClicked;
        menu.Add(quitMenuItem);

        var iconUri = new Uri("avares://ViperLink.App/Assets/avalonia-logo.ico");
        _statusTrayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(iconUri)),
            ToolTipText = "ViperLink",
            Menu = menu,
            IsVisible = true,
        };

        TrayIcon.SetIcons(this, [_statusTrayIcon]);
        _diagnosticsMenuItem.IsVisible = false;
        _logMenuItem.IsVisible = false;
    }

    private static NativeMenuItem CreateReadOnlyItem(string header)
    {
        return new NativeMenuItem(header)
        {
            IsEnabled = false,
        };
    }
}
