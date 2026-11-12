using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using PowerGauge.Tray;
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
}
