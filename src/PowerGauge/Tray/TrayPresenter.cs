using System.Collections.Generic;
using Avalonia.Controls;

namespace PowerGauge.Tray;

public sealed class TrayPresenter
{
    private readonly TrayIconRenderer _trayIconRenderer = new();
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu;
    private readonly List<DeviceMenuItems> _deviceMenuItems = [];
    private readonly NativeMenuItem _resultMenuItem;
    private readonly NativeMenuItem _versionMenuItem;
    private readonly NativeMenuItem _diagnosticsMenuItem;
    private readonly NativeMenuItem _logMenuItem;

    public TrayPresenter(System.Action refreshNow, System.Action quit)
    {
        _resultMenuItem = CreateReadOnlyItem("Last updated: waiting to start");
        _versionMenuItem = CreateReadOnlyItem($"Version: {AppIdentity.DisplayVersion}");
        _diagnosticsMenuItem = CreateReadOnlyItem(string.Empty);
        _logMenuItem = CreateReadOnlyItem(string.Empty);

        _menu = new NativeMenu();
        AddDeviceSectionItems("Device: probing...", "Battery: probing...");
        _menu.Add(_resultMenuItem);
        _menu.Add(_versionMenuItem);
        _menu.Add(_diagnosticsMenuItem);
        _menu.Add(_logMenuItem);
        _menu.Add(new NativeMenuItemSeparator());

        var refreshMenuItem = new NativeMenuItem("Refresh now");
        refreshMenuItem.Click += (_, _) => refreshNow();
        _menu.Add(refreshMenuItem);

        var quitMenuItem = new NativeMenuItem("Quit");
        quitMenuItem.Click += (_, _) => quit();
        _menu.Add(quitMenuItem);

        _trayIcon = new TrayIcon
        {
            Icon = _trayIconRenderer.Render(null),
            ToolTipText = AppIdentity.ProductName,
            Menu = _menu,
            IsVisible = true,
        };

        _diagnosticsMenuItem.IsVisible = false;
        _logMenuItem.IsVisible = false;
    }

    public TrayIcon TrayIcon => _trayIcon;

    public void Apply(BatteryProbeResult result)
    {
        SyncDeviceSections(result.DeviceSections);
        _resultMenuItem.Header = result.ResultHeader;
        _diagnosticsMenuItem.Header = result.DiagnosticsHeader;
        _diagnosticsMenuItem.IsVisible = result.ShowDiagnostics;
        _logMenuItem.Header = result.LogFilePath is null ? "Log: unavailable" : $"Log: {result.LogFilePath}";
        _logMenuItem.IsVisible = result.ShowDiagnostics;
        _trayIcon.Icon = _trayIconRenderer.Render(result.IconBatteryPercent);
        _trayIcon.ToolTipText = result.ToolTipText;
    }

    private void SyncDeviceSections(IReadOnlyList<DeviceBatteryDisplay> sections)
    {
        if (_deviceMenuItems.Count != sections.Count)
        {
            var dynamicItemCount = _deviceMenuItems.Count * 3;
            for (var i = dynamicItemCount - 1; i >= 0; i--)
            {
                _menu.Items.RemoveAt(i);
            }

            _deviceMenuItems.Clear();

            var insertIndex = 0;
            foreach (var section in sections)
            {
                var deviceItem = CreateReadOnlyItem(section.DeviceHeader);
                var batteryItem = CreateReadOnlyItem(section.BatteryStatusHeader);
                var separator = new NativeMenuItemSeparator();

                _menu.Items.Insert(insertIndex++, deviceItem);
                _menu.Items.Insert(insertIndex++, batteryItem);
                _menu.Items.Insert(insertIndex++, separator);

                _deviceMenuItems.Add(new DeviceMenuItems(deviceItem, batteryItem));
            }

            return;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            _deviceMenuItems[i].DeviceItem.Header = sections[i].DeviceHeader;
            _deviceMenuItems[i].BatteryStatusItem.Header = sections[i].BatteryStatusHeader;
        }
    }

    private void AddDeviceSectionItems(string deviceHeader, string batteryStatusHeader)
    {
        var deviceItem = CreateReadOnlyItem(deviceHeader);
        var batteryItem = CreateReadOnlyItem(batteryStatusHeader);
        _menu.Add(deviceItem);
        _menu.Add(batteryItem);
        _menu.Add(new NativeMenuItemSeparator());
        _deviceMenuItems.Add(new DeviceMenuItems(deviceItem, batteryItem));
    }

    private static NativeMenuItem CreateReadOnlyItem(string header)
    {
        return new NativeMenuItem(header)
        {
            IsEnabled = false,
        };
    }

    private sealed record DeviceMenuItems(NativeMenuItem DeviceItem, NativeMenuItem BatteryStatusItem);
}
