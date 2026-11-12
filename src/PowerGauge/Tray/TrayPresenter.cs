using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using PowerGauge.Updates;

namespace PowerGauge.Tray;

public sealed partial class TrayPresenter
{
    private const int MaxTooltipLength = 127;
    private const uint WmTrayMouse = 0x0800; // WM_USER + 1024 in Avalonia.Win32.TrayIconImpl
    private const int WmRightButtonUp = 0x0205;
    private readonly TrayIconRenderer _trayIconRenderer = new();
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu;
    private readonly List<DeviceMenuItems> _deviceMenuItems = [];
    private readonly NativeMenuItem _resultMenuItem;
    private readonly NativeMenuItem _versionMenuItem;
    private readonly NativeMenuItem _diagnosticsMenuItem;
    private readonly NativeMenuItem _logMenuItem;
    private readonly NativeMenuItem _updateMenuItem;
    private string _baseToolTipText = AppIdentity.ProductName;
    private string? _updateTooltipNotice;
    private int? _currentBatteryPercent;
    private IntPtr _win32MessageWindow;

    public TrayPresenter(Action refreshNow, Action updateAction, Action quit, PlatformColorValues? initialColors = null)
    {
        if (initialColors is not null)
        {
            _trayIconRenderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(initialColors));
        }
        _resultMenuItem = CreateReadOnlyItem("Last updated: waiting to start");
        _versionMenuItem = CreateReadOnlyItem($"Version: {AppIdentity.DisplayVersion}");
        _diagnosticsMenuItem = CreateReadOnlyItem(string.Empty);
        _logMenuItem = CreateReadOnlyItem(string.Empty);

        _menu = new NativeMenu();
        _menu.Add(_resultMenuItem);
        _menu.Add(_versionMenuItem);
        _menu.Add(_diagnosticsMenuItem);
        _menu.Add(_logMenuItem);
        _menu.Add(new NativeMenuItemSeparator());
        SyncDeviceSections([new DeviceBatteryDisplay("Device: probing...", "Battery: probing...")]);

        var refreshMenuItem = new NativeMenuItem("Refresh now");
        refreshMenuItem.Click += (_, _) => refreshNow();
        _menu.Add(refreshMenuItem);

        _updateMenuItem = new NativeMenuItem("Check for updates");
        _updateMenuItem.Click += (_, _) => updateAction();
        _menu.Add(_updateMenuItem);

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

        if (OperatingSystem.IsWindows())
        {
            _trayIcon.Clicked += (_, _) => OpenWindowsTrayMenu();
        }

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
        _currentBatteryPercent = result.IconBatteryPercent;
        _trayIcon.Icon = _trayIconRenderer.Render(_currentBatteryPercent);
        _baseToolTipText = result.ToolTipText;
        UpdateToolTipText();
    }

    public void ApplyTheme(PlatformColorValues colorValues)
    {
        if (_trayIconRenderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(colorValues)))
        {
            _trayIcon.Icon = _trayIconRenderer.Render(_currentBatteryPercent);
        }
    }

    public void ApplyUpdateStatus(TrayUpdateStatus updateStatus)
    {
        _versionMenuItem.Header = updateStatus.VersionHeader;
        _updateMenuItem.Header = updateStatus.ActionHeader;
        _updateMenuItem.IsEnabled = updateStatus.IsActionEnabled;
        _updateTooltipNotice = updateStatus.TooltipNotice;
        UpdateToolTipText();
    }

    private void UpdateToolTipText()
    {
        var combined = string.IsNullOrWhiteSpace(_updateTooltipNotice)
            ? _baseToolTipText
            : $"{_baseToolTipText}\n{_updateTooltipNotice}";

        _trayIcon.ToolTipText = BatteryProbeResult.Truncate(combined, MaxTooltipLength);
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

    private void OpenWindowsTrayMenu()
    {
        if (_win32MessageWindow == IntPtr.Zero)
        {
            IntPtr found = IntPtr.Zero;
            unsafe
            {
                _ = EnumThreadWindows(GetCurrentThreadId(), &FindAvaloniaMessageWindow, (IntPtr)(&found));
            }

            _win32MessageWindow = found;
        }

        if (_win32MessageWindow != IntPtr.Zero)
        {
            _ = PostMessageW(_win32MessageWindow, WmTrayMouse, new IntPtr(1), new IntPtr(WmRightButtonUp));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int FindAvaloniaMessageWindow(IntPtr hWnd, IntPtr lParam)
    {
        Span<byte> rawBuffer = stackalloc byte[128];
        var charCount = GetClassNameW(hWnd, rawBuffer, rawBuffer.Length / sizeof(char));
        if (charCount > 0)
        {
            var className = MemoryMarshal.Cast<byte, char>(rawBuffer)[..charCount];
            if (className.StartsWith("AvaloniaMessageWindow ", StringComparison.Ordinal))
            {
                *(IntPtr*)lParam = hWnd;
                return 0;
            }
        }

        return 1;
    }

    private static NativeMenuItem CreateReadOnlyItem(string header)
    {
        return new NativeMenuItem(header)
        {
            IsEnabled = false,
        };
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumThreadWindows(
        uint dwThreadId,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> lpfn,
        IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetClassNameW(IntPtr hWnd, Span<byte> lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private sealed record DeviceMenuItems(NativeMenuItem DeviceItem, NativeMenuItem BatteryStatusItem);
}
