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
}
