using Avalonia.Controls;

namespace PowerGauge.Tray;

public sealed class TrayPresenter
{
    private readonly TrayIconRenderer _trayIconRenderer = new();
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _batteryMenuItem;
    private readonly NativeMenuItem _statusMenuItem;
    private readonly NativeMenuItem _deviceMenuItem;
    private readonly NativeMenuItem _resultMenuItem;
    private readonly NativeMenuItem _versionMenuItem;
    private readonly NativeMenuItem _diagnosticsMenuItem;
    private readonly NativeMenuItem _logMenuItem;

    public TrayPresenter(System.Action refreshNow, System.Action quit)
    {
        _batteryMenuItem = CreateReadOnlyItem("Battery: probing...");
        _statusMenuItem = CreateReadOnlyItem("Status: probing...");
        _deviceMenuItem = CreateReadOnlyItem("Device: probing...");
        _resultMenuItem = CreateReadOnlyItem("Last updated: waiting to start");
        _versionMenuItem = CreateReadOnlyItem($"Version: {AppIdentity.DisplayVersion}");
        _diagnosticsMenuItem = CreateReadOnlyItem(string.Empty);
        _logMenuItem = CreateReadOnlyItem(string.Empty);

        var menu = new NativeMenu();
        menu.Add(_batteryMenuItem);
        menu.Add(_statusMenuItem);
        menu.Add(_deviceMenuItem);
        menu.Add(_resultMenuItem);
        menu.Add(_versionMenuItem);
        menu.Add(_diagnosticsMenuItem);
        menu.Add(_logMenuItem);
        menu.Add(new NativeMenuItemSeparator());

        var refreshMenuItem = new NativeMenuItem("Refresh now");
        refreshMenuItem.Click += (_, _) => refreshNow();
        menu.Add(refreshMenuItem);

        var quitMenuItem = new NativeMenuItem("Quit");
        quitMenuItem.Click += (_, _) => quit();
        menu.Add(quitMenuItem);

        _trayIcon = new TrayIcon
        {
            Icon = _trayIconRenderer.Render(null),
            ToolTipText = AppIdentity.ProductName,
            Menu = menu,
            IsVisible = true,
        };

        _diagnosticsMenuItem.IsVisible = false;
        _logMenuItem.IsVisible = false;
    }

    public TrayIcon TrayIcon => _trayIcon;

    public void Apply(BatteryProbeResult result)
    {
        _batteryMenuItem.Header = result.BatteryHeader;
        _statusMenuItem.Header = result.StatusHeader;
        _deviceMenuItem.Header = result.DeviceHeader;
        _resultMenuItem.Header = result.ResultHeader;
        _diagnosticsMenuItem.Header = result.DiagnosticsHeader;
        _diagnosticsMenuItem.IsVisible = result.ShowDiagnostics;
        _logMenuItem.Header = result.LogFilePath is null ? "Log: unavailable" : $"Log: {result.LogFilePath}";
        _logMenuItem.IsVisible = result.ShowDiagnostics;
        _trayIcon.Icon = _trayIconRenderer.Render(result.IconBatteryPercent);
        _trayIcon.ToolTipText = result.ToolTipText;
    }

    private static NativeMenuItem CreateReadOnlyItem(string header)
    {
        return new NativeMenuItem(header)
        {
            IsEnabled = false,
        };
    }
}
