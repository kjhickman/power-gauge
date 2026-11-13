using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using PowerGauge.Tray;
using PowerGauge.Updates;
using Xunit;

namespace PowerGauge.Tests;

public sealed class TrayMenuThemeTests
{
    [Fact]
    public void MenuItemTheme_HighlightsOnHoverAndSelection_AndStaysTransparentWhenDisabled()
    {
        var resources = TrayMenuTheme.CreateResources();
        var menuItemTheme = Assert.IsType<ControlTheme>(resources[typeof(MenuItem)]);

        var host = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Resources = resources,
        };

        var enabledItem = new MenuItem { Header = "Refresh now", Theme = menuItemTheme };
        var disabledItem = new MenuItem { Header = "Battery: 100%", IsEnabled = false, Theme = menuItemTheme };
        var stack = new StackPanel();
        stack.Children.Add(enabledItem);
        stack.Children.Add(disabledItem);
        host.Child = stack;

        enabledItem.ApplyStyling();
        disabledItem.ApplyStyling();
        enabledItem.ApplyTemplate();
        disabledItem.ApplyTemplate();

        var enabledBorder = enabledItem.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");
        var disabledBorder = disabledItem.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");

        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(enabledBorder.Background).Color);

        ((IPseudoClasses)enabledItem.Classes).Set(":pointerover", true);
        var hoverColor = Assert.IsAssignableFrom<ISolidColorBrush>(enabledBorder.Background).Color;
        Assert.True(hoverColor.A > 0);

        ((IPseudoClasses)enabledItem.Classes).Set(":pointerover", false);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(enabledBorder.Background).Color);

        enabledItem.IsSelected = true;
        var selectedColor = Assert.IsAssignableFrom<ISolidColorBrush>(enabledBorder.Background).Color;
        Assert.True(selectedColor.A > 0);

        ((IPseudoClasses)disabledItem.Classes).Set(":pointerover", true);
        disabledItem.IsSelected = true;
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(disabledBorder.Background).Color);

        var enabledChevron = enabledItem.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Chevron");
        Assert.False(enabledChevron.IsVisible);

        var submenuParent = new MenuItem { Header = "More", Theme = menuItemTheme };
        submenuParent.Items.Add(new MenuItem { Header = "Refresh", Theme = menuItemTheme });
        stack.Children.Add(submenuParent);
        submenuParent.ApplyStyling();
        submenuParent.ApplyTemplate();

        var submenuChevron = submenuParent.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Chevron");
        Assert.True(submenuChevron.IsVisible);
    }

    [Fact]
    public void TrayIconRenderer_UpdatesPaletteFromPlatformColors_AndPicksReadableForegrounds()
    {
        var renderer = new TrayIconRenderer();

        var darkColors = new PlatformColorValues
        {
            ThemeVariant = PlatformThemeVariant.Dark,
            AccentColor1 = Color.Parse("#0078D4"),
        };
        Assert.True(renderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(darkColors)));
        Assert.False(renderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(darkColors)));

        Assert.Equal(Brushes.White, TrayIconRenderer.GetReadableForeground(renderer.GetBatteryBackgroundColor(15)));
        Assert.Equal(Brushes.Black, TrayIconRenderer.GetReadableForeground(renderer.GetBatteryBackgroundColor(35)));
        Assert.Equal(Brushes.White, TrayIconRenderer.GetReadableForeground(renderer.GetBatteryBackgroundColor(85)));
        var darkBorder = renderer.GetBorderColor();

        var lightColors = new PlatformColorValues
        {
            ThemeVariant = PlatformThemeVariant.Light,
            AccentColor1 = Color.Parse("#FFB900"),
        };
        Assert.True(renderer.UpdateTheme(TrayIconThemePalette.FromPlatformColors(lightColors)));
        Assert.NotEqual(darkBorder, renderer.GetBorderColor());
        Assert.Equal(0, renderer.CachedBatteryIconCount);
    }

    [Fact]
    public void UpdatePromptContent_AppliesButtonThemes_AndDispatchesDecisions()
    {
        var resources = TrayMenuTheme.CreateResources();
        var buttonTheme = Assert.IsType<ControlTheme>(resources[typeof(Button)]);

        bool? decision = null;
        var content = UpdatePromptWindow.BuildContent(
            new UpdatePromptInfo("v0.3.0", "v0.3.1", CanSelfUpdate: true),
            accepted => decision = accepted);

        var host = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Resources = resources,
            Child = content,
        };

        var buttons = content.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(2, buttons.Length);
        var (laterButton, confirmButton) = (buttons[0], buttons[1]);
        laterButton.Theme = buttonTheme;
        confirmButton.Theme = buttonTheme;

        laterButton.ApplyStyling();
        confirmButton.ApplyStyling();
        laterButton.ApplyTemplate();
        confirmButton.ApplyTemplate();

        Assert.Equal("Later", laterButton.Content);
        Assert.Equal("Install update v0.3.1", confirmButton.Content);

        var baseLaterBg = Assert.IsAssignableFrom<ISolidColorBrush>(laterButton.Presenter!.Background).Color;
        var baseConfirmBg = Assert.IsAssignableFrom<ISolidColorBrush>(confirmButton.Presenter!.Background).Color;
        Assert.NotEqual(baseLaterBg, baseConfirmBg);

        ((IPseudoClasses)confirmButton.Classes).Set(":pointerover", true);
        var hoverConfirmBg = Assert.IsAssignableFrom<ISolidColorBrush>(confirmButton.Presenter.Background).Color;
        Assert.NotEqual(baseConfirmBg, hoverConfirmBg);

        laterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(false, decision);

        confirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(true, decision);
    }

    [Fact]
    public void TrayPresenter_SyncsCompactRootMenu_AndTucksSecondaryOptionsIntoMoreSubmenu()
    {
        var presenter = new TrayPresenter(() => { }, () => { }, () => { }, initialColors: null, renderIcons: false);

        // Idle update state: Check for updates lives in More, hidden in root
        presenter.ApplyUpdateStatus(new TrayUpdateStatus("Version: 0.4.0", "Check for updates", IsActionEnabled: true, ShowActionInRootMenu: false));

        // Disconnected state: single device line + separator + [hidden update] + More + Quit
        presenter.Apply(new BatteryProbeResult(
            [new DeviceBatteryDisplay("No supported devices")],
            "Last checked: 12:00:00",
            string.Empty,
            ShowDiagnostics: false,
            "PowerGauge\nNo supported devices"));

        var visibleRootDisconnected = presenter.RootMenu.Items
            .OfType<NativeMenuItem>()
            .Where(i => i.IsVisible)
            .Select(i => i.Header ?? string.Empty)
            .ToArray();
        Assert.Equal(["No supported devices", "-", "More", "Quit"], visibleRootDisconnected);

        var visibleMoreIdle = presenter.MoreMenu.Items
            .OfType<NativeMenuItem>()
            .Where(i => i.IsVisible)
            .Select(i => i.Header ?? string.Empty)
            .ToArray();
        Assert.Equal(["Last checked: 12:00:00", "Version: 0.4.0", "-", "Refresh", "Check for updates", "Open diagnostics log"], visibleMoreIdle);

        // Connected device + update available: 2-line device section + Install update surfaced in root
        presenter.Apply(new BatteryProbeResult(
            [new DeviceBatteryDisplay("Razer Viper Ultimate (Wireless)", "84%")],
            "Last updated: 12:01:00",
            string.Empty,
            ShowDiagnostics: false,
            "PowerGauge\nRazer Viper Ultimate: 84%",
            IconBatteryPercent: 84));
        presenter.ApplyUpdateStatus(new TrayUpdateStatus(
            "Version: 0.4.0 (v0.5.0 available)",
            "Install update (v0.5.0)",
            IsActionEnabled: true,
            "Update available: v0.5.0",
            ShowActionInRootMenu: true));

        var visibleRootConnected = presenter.RootMenu.Items
            .OfType<NativeMenuItem>()
            .Where(i => i.IsVisible)
            .Select(i => i.Header ?? string.Empty)
            .ToArray();
        Assert.Equal(["Razer Viper Ultimate (Wireless)", "84%", "-", "Install update (v0.5.0)", "More", "Quit"], visibleRootConnected);

        var visibleMoreUpdateReady = presenter.MoreMenu.Items
            .OfType<NativeMenuItem>()
            .Where(i => i.IsVisible)
            .Select(i => i.Header ?? string.Empty)
            .ToArray();
        Assert.Equal(["Last updated: 12:01:00", "Version: 0.4.0 (v0.5.0 available)", "-", "Refresh", "Open diagnostics log"], visibleMoreUpdateReady);
    }
}
