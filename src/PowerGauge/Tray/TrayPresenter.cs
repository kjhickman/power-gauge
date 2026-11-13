using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private readonly bool _renderIcons;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu;
    private readonly NativeMenu _moreMenu;
    private readonly List<DeviceMenuItems> _deviceMenuItems = [];
    private readonly NativeMenuItem _rootUpdateMenuItem;
    private readonly NativeMenuItem _moreUpdateMenuItem;
    private readonly NativeMenuItem _resultMenuItem;
    private readonly NativeMenuItem _versionMenuItem;
    private readonly NativeMenuItem _diagnosticsMenuItem;
    private readonly NativeMenuItem _openLogMenuItem;
    private int _dynamicItemCount;
    private string? _logFilePath;
    private string _baseToolTipText = AppIdentity.ProductName;
    private string? _updateTooltipNotice;
    private int? _currentBatteryPercent;
    private IntPtr _win32MessageWindow;

    public TrayPresenter(Action refreshNow, Action updateAction, Action quit, PlatformColorValues? initialColors = null)
        : this(refreshNow, updateAction, quit, initialColors, renderIcons: true)
    {
    }

    internal TrayPresenter(
        Action refreshNow,
        Action updateAction,
        Action quit,
        PlatformColorValues? initialColors,
        bool renderIcons)
    {
        _renderIcons = renderIcons;
        if (initialColors is not null)
        {
            _trayIconRenderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(initialColors));
        }

        _resultMenuItem = CreateReadOnlyItem("Last checked: waiting to start");
        _versionMenuItem = CreateReadOnlyItem($"Version: {AppIdentity.DisplayVersion}");
        _diagnosticsMenuItem = CreateReadOnlyItem(string.Empty);
        _diagnosticsMenuItem.IsVisible = false;

        var refreshMenuItem = new NativeMenuItem("Refresh");
        refreshMenuItem.Click += (_, _) => refreshNow();

        _moreUpdateMenuItem = new NativeMenuItem("Check for updates");
        _moreUpdateMenuItem.Click += (_, _) => updateAction();

        _openLogMenuItem = new NativeMenuItem("Open diagnostics log")
        {
            IsEnabled = false,
        };
        _openLogMenuItem.Click += (_, _) => OpenDiagnosticsLog();

        _moreMenu = new NativeMenu();
        _moreMenu.Add(_resultMenuItem);
        _moreMenu.Add(_versionMenuItem);
        _moreMenu.Add(_diagnosticsMenuItem);
        _moreMenu.Add(new NativeMenuItemSeparator());
        _moreMenu.Add(refreshMenuItem);
        _moreMenu.Add(_moreUpdateMenuItem);
        _moreMenu.Add(_openLogMenuItem);

        _rootUpdateMenuItem = new NativeMenuItem("Install update")
        {
            IsVisible = false,
        };
        _rootUpdateMenuItem.Click += (_, _) => updateAction();

        var moreMenuItem = new NativeMenuItem("More")
        {
            Menu = _moreMenu,
        };

        var quitMenuItem = new NativeMenuItem("Quit");
        quitMenuItem.Click += (_, _) => quit();

        _menu = new NativeMenu();
        _menu.Add(_rootUpdateMenuItem);
        _menu.Add(moreMenuItem);
        _menu.Add(quitMenuItem);
        SyncDeviceSections([new DeviceBatteryDisplay("Scanning wireless devices...")]);

        _trayIcon = new TrayIcon
        {
            Icon = _renderIcons ? _trayIconRenderer.Render(null) : null,
            ToolTipText = AppIdentity.ProductName,
            Menu = _menu,
            IsVisible = true,
        };

        if (OperatingSystem.IsWindows())
        {
            _trayIcon.Clicked += (_, _) => OpenWindowsTrayMenu();
        }
    }

    public TrayIcon TrayIcon => _trayIcon;

    internal NativeMenu RootMenu => _menu;

    internal NativeMenu MoreMenu => _moreMenu;

    public void Apply(BatteryProbeResult result)
    {
        SyncDeviceSections(result.DeviceSections);
        _resultMenuItem.Header = result.ResultHeader;
        _diagnosticsMenuItem.Header = result.DiagnosticsHeader;
        _diagnosticsMenuItem.IsVisible = result.ShowDiagnostics;
        _logFilePath = result.LogFilePath;
        _openLogMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(_logFilePath);
        _currentBatteryPercent = result.IconBatteryPercent;
        if (_renderIcons)
        {
            _trayIcon.Icon = _trayIconRenderer.Render(_currentBatteryPercent);
        }
        _baseToolTipText = result.ToolTipText;
        UpdateToolTipText();
    }

    public void ApplyTheme(PlatformColorValues colorValues)
    {
        if (_trayIconRenderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(colorValues)) && _renderIcons)
        {
            _trayIcon.Icon = _trayIconRenderer.Render(_currentBatteryPercent);
        }
    }

    public void ApplyUpdateStatus(TrayUpdateStatus updateStatus)
    {
        _versionMenuItem.Header = updateStatus.VersionHeader;

        _rootUpdateMenuItem.Header = updateStatus.ActionHeader;
        _rootUpdateMenuItem.IsEnabled = updateStatus.IsActionEnabled;
        _rootUpdateMenuItem.IsVisible = updateStatus.ShowActionInRootMenu;

        _moreUpdateMenuItem.Header = updateStatus.ActionHeader;
        _moreUpdateMenuItem.IsEnabled = updateStatus.IsActionEnabled;
        _moreUpdateMenuItem.IsVisible = !updateStatus.ShowActionInRootMenu;

        _updateTooltipNotice = updateStatus.TooltipNotice;
        UpdateToolTipText();
    }

    private void OpenDiagnosticsLog()
    {
        if (string.IsNullOrWhiteSpace(_logFilePath) || !File.Exists(_logFilePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _logFilePath,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
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
        if (!HasSameSectionShape(sections))
        {
            for (var i = _dynamicItemCount - 1; i >= 0; i--)
            {
                _menu.Items.RemoveAt(i);
            }

            _deviceMenuItems.Clear();

            var insertIndex = 0;
            foreach (var section in sections)
            {
                var deviceItem = CreateReadOnlyItem(section.DeviceHeader);
                _menu.Items.Insert(insertIndex++, deviceItem);

                NativeMenuItem? batteryItem = null;
                if (!string.IsNullOrWhiteSpace(section.BatteryStatusHeader))
                {
                    batteryItem = CreateReadOnlyItem(section.BatteryStatusHeader);
                    _menu.Items.Insert(insertIndex++, batteryItem);
                }

                _menu.Items.Insert(insertIndex++, new NativeMenuItemSeparator());
                _deviceMenuItems.Add(new DeviceMenuItems(deviceItem, batteryItem));
            }

            _dynamicItemCount = insertIndex;
            return;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            _deviceMenuItems[i].DeviceItem.Header = sections[i].DeviceHeader;
            if (_deviceMenuItems[i].BatteryStatusItem is { } batteryItem)
            {
                batteryItem.Header = sections[i].BatteryStatusHeader!;
            }
        }
    }

    private bool HasSameSectionShape(IReadOnlyList<DeviceBatteryDisplay> sections)
    {
        if (_deviceMenuItems.Count != sections.Count)
        {
            return false;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var hasBatteryLine = !string.IsNullOrWhiteSpace(sections[i].BatteryStatusHeader);
            if ((_deviceMenuItems[i].BatteryStatusItem is not null) != hasBatteryLine)
            {
                return false;
            }
        }

        return true;
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

    private sealed record DeviceMenuItems(NativeMenuItem DeviceItem, NativeMenuItem? BatteryStatusItem);
}
