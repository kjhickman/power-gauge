using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace PowerGauge.Tray;

internal static class TrayMenuTheme
{
    private const string MenuBackgroundKey = "TrayMenuBackground";
    private const string MenuBorderKey = "TrayMenuBorder";
    private const string MenuForegroundKey = "TrayMenuForeground";
    internal const string ItemDisabledForegroundKey = "TrayItemDisabledForeground";
    private const string ItemHoverBrushKey = "TrayItemHoverBackground";
    private const string ItemPressedBrushKey = "TrayItemPressedBackground";
    private const string SeparatorBrushKey = "TrayMenuSeparator";
    private static readonly FuncValueConverter<int, bool> HasItemsConverter = new(static count => count > 0);

    public static ResourceDictionary CreateResources()
    {
        var resources = new ResourceDictionary();

        resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            [MenuBackgroundKey] = new SolidColorBrush(Color.Parse("#2B2B2B")),
            [MenuBorderKey] = new SolidColorBrush(Color.Parse("#454545")),
            [MenuForegroundKey] = new SolidColorBrush(Color.Parse("#F3F4F6")),
            [ItemDisabledForegroundKey] = new SolidColorBrush(Color.Parse("#A1A8B3")),
            [ItemHoverBrushKey] = new SolidColorBrush(Color.Parse("#29FFFFFF")),
            [ItemPressedBrushKey] = new SolidColorBrush(Color.Parse("#18FFFFFF")),
            [SeparatorBrushKey] = new SolidColorBrush(Color.Parse("#26FFFFFF")),
        };

        resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            [MenuBackgroundKey] = new SolidColorBrush(Color.Parse("#F9F9F9")),
            [MenuBorderKey] = new SolidColorBrush(Color.Parse("#D6D6D6")),
            [MenuForegroundKey] = new SolidColorBrush(Color.Parse("#1A1A1A")),
            [ItemDisabledForegroundKey] = new SolidColorBrush(Color.Parse("#5F6672")),
            [ItemHoverBrushKey] = new SolidColorBrush(Color.Parse("#14000000")),
            [ItemPressedBrushKey] = new SolidColorBrush(Color.Parse("#22000000")),
            [SeparatorBrushKey] = new SolidColorBrush(Color.Parse("#18000000")),
        };

        resources[typeof(Window)] = CreateWindowTheme();
        resources[typeof(Button)] = CreateButtonTheme();
        resources[typeof(MenuFlyoutPresenter)] = CreateMenuFlyoutPresenterTheme();
        resources[typeof(MenuItem)] = CreateMenuItemTheme();
        resources[typeof(Separator)] = CreateSeparatorTheme();

        return resources;
    }

    private static ControlTheme CreateWindowTheme()
    {
        return new ControlTheme(typeof(Window))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(MenuBackgroundKey)),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(MenuForegroundKey)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Window>((window, scope) =>
                {
                    var presenter = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.BackgroundProperty] = window[!TemplatedControl.BackgroundProperty],
                        [!ContentPresenter.ContentProperty] = window[!ContentControl.ContentProperty],
                        [!ContentPresenter.ContentTemplateProperty] = window[!ContentControl.ContentTemplateProperty],
                        [!ContentPresenter.PaddingProperty] = window[!TemplatedControl.PaddingProperty],
                        [!ContentPresenter.HorizontalContentAlignmentProperty] = window[!ContentControl.HorizontalContentAlignmentProperty],
                        [!ContentPresenter.VerticalContentAlignmentProperty] = window[!ContentControl.VerticalContentAlignmentProperty],
                    }.RegisterInNameScope(scope);

                    return new VisualLayerManager
                    {
                        Name = "PART_VisualLayerManager",
                        Child = presenter,
                    }.RegisterInNameScope(scope);
                })),
            },
        };
    }

    private static ControlTheme CreateButtonTheme()
    {
        return new ControlTheme(typeof(Button))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(MenuBorderKey)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(MenuForegroundKey)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 7)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((button, scope) =>
                    new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.BackgroundProperty] = button[!TemplatedControl.BackgroundProperty],
                        [!ContentPresenter.BorderBrushProperty] = button[!TemplatedControl.BorderBrushProperty],
                        [!ContentPresenter.BorderThicknessProperty] = button[!TemplatedControl.BorderThicknessProperty],
                        [!ContentPresenter.CornerRadiusProperty] = button[!TemplatedControl.CornerRadiusProperty],
                        [!ContentPresenter.PaddingProperty] = button[!TemplatedControl.PaddingProperty],
                        [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
                        [!ContentPresenter.ContentTemplateProperty] = button[!ContentControl.ContentTemplateProperty],
                        [!ContentPresenter.HorizontalContentAlignmentProperty] = button[!ContentControl.HorizontalContentAlignmentProperty],
                        [!ContentPresenter.VerticalContentAlignmentProperty] = button[!ContentControl.VerticalContentAlignmentProperty],
                    }.RegisterInNameScope(scope))),
            },
            Children =
            {
                new Style(x => x.Nesting().Class(":pointerover"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemHoverBrushKey)) },
                },
                new Style(x => x.Nesting().Class(":pressed"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemPressedBrushKey)) },
                },
                new Style(x => x.Nesting().Class("accent"))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#2563EB"))),
                        new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Color.Parse("#1D4ED8"))),
                        new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                        new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                    },
                    Children =
                    {
                        new Style(x => x.Nesting().Class(":pointerover"))
                        {
                            Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#3B82F6"))) },
                        },
                        new Style(x => x.Nesting().Class(":pressed"))
                        {
                            Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#1E40AF"))) },
                        },
                    },
                },
            },
        };
    }

    private static ControlTheme CreateMenuFlyoutPresenterTheme()
    {
        return new ControlTheme(typeof(MenuFlyoutPresenter))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(MenuBackgroundKey)),
                new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(MenuBorderKey)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(2, 4)),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(MenuForegroundKey)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(Layoutable.MinWidthProperty, 200.0),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<MenuFlyoutPresenter>((presenter, scope) =>
                {
                    var itemsPresenter = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [!ItemsPresenter.ItemsPanelProperty] = presenter[!ItemsControl.ItemsPanelProperty],
                    }.RegisterInNameScope(scope);

                    KeyboardNavigation.SetTabNavigation(itemsPresenter, KeyboardNavigationMode.Continue);

                    return new Border
                    {
                        Name = "LayoutRoot",
                        [!Border.BackgroundProperty] = presenter[!TemplatedControl.BackgroundProperty],
                        [!Border.BorderBrushProperty] = presenter[!TemplatedControl.BorderBrushProperty],
                        [!Border.BorderThicknessProperty] = presenter[!TemplatedControl.BorderThicknessProperty],
                        [!Border.CornerRadiusProperty] = presenter[!TemplatedControl.CornerRadiusProperty],
                        [!Decorator.PaddingProperty] = presenter[!TemplatedControl.PaddingProperty],
                        Child = itemsPresenter,
                    }.RegisterInNameScope(scope);
                })),
            },
        };
    }

    private static ControlTheme CreateMenuItemTheme()
    {
        return new ControlTheme(typeof(MenuItem))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(MenuForegroundKey)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<MenuItem>((item, scope) =>
                {
                    var headerPresenter = new ContentPresenter
                    {
                        Name = "PART_HeaderPresenter",
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        [!ContentPresenter.ContentProperty] = item[!HeaderedSelectingItemsControl.HeaderProperty],
                        [!ContentPresenter.ContentTemplateProperty] = item[!HeaderedSelectingItemsControl.HeaderTemplateProperty],
                    }.RegisterInNameScope(scope);

                    var chevron = new TextBlock
                    {
                        Name = "PART_Chevron",
                        Text = "›",
                        FontSize = 15,
                        Margin = new Thickness(12, -1, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        [!Visual.IsVisibleProperty] = new TemplateBinding(ItemsControl.ItemCountProperty) { Converter = HasItemsConverter },
                        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension(ItemDisabledForegroundKey),
                    }.RegisterInNameScope(scope);

                    var row = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    };
                    Grid.SetColumn(headerPresenter, 0);
                    Grid.SetColumn(chevron, 1);
                    row.Children.Add(headerPresenter);
                    row.Children.Add(chevron);

                    var subItemsPresenter = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [!ItemsPresenter.ItemsPanelProperty] = item[!ItemsControl.ItemsPanelProperty],
                    }.RegisterInNameScope(scope);
                    KeyboardNavigation.SetTabNavigation(subItemsPresenter, KeyboardNavigationMode.Continue);

                    var popup = new Popup
                    {
                        Name = "PART_Popup",
                        WindowManagerAddShadowHint = false,
                        Placement = PlacementMode.RightEdgeAlignedTop,
                        HorizontalOffset = 2,
                        IsLightDismissEnabled = false,
                        [!Popup.IsOpenProperty] = item[!!MenuItem.IsSubMenuOpenProperty],
                        Child = new Border
                        {
                            MinWidth = 200,
                            Padding = new Thickness(2, 4),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(8),
                            [!Border.BackgroundProperty] = new DynamicResourceExtension(MenuBackgroundKey),
                            [!Border.BorderBrushProperty] = new DynamicResourceExtension(MenuBorderKey),
                            Child = subItemsPresenter,
                        },
                    }.RegisterInNameScope(scope);

                    var rootPanel = new Panel();
                    rootPanel.Children.Add(new Border
                    {
                        Name = "PART_LayoutRoot",
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(4, 1),
                        Padding = new Thickness(10, 6),
                        [!Border.BackgroundProperty] = item[!TemplatedControl.BackgroundProperty],
                        Child = row,
                    }.RegisterInNameScope(scope));
                    rootPanel.Children.Add(popup);

                    return rootPanel;
                })),
            },
            Children =
            {
                new Style(x => x.Nesting().Class(":open"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemHoverBrushKey)) },
                },
                new Style(x => x.Nesting().Class(":selected"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemHoverBrushKey)) },
                },
                new Style(x => x.Nesting().Class(":pointerover"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemHoverBrushKey)) },
                },
                new Style(x => x.Nesting().Class(":pressed"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ItemPressedBrushKey)) },
                },
                new Style(x => x.Nesting().Class(":disabled"))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                        new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(ItemDisabledForegroundKey)),
                    },
                },
            },
        };
    }

    private static ControlTheme CreateSeparatorTheme()
    {
        return new ControlTheme(typeof(Separator))
        {
            Setters =
            {
                new Setter(InputElement.FocusableProperty, false),
                new Setter(Layoutable.HeightProperty, 1.0),
                new Setter(Layoutable.MarginProperty, new Thickness(8, 4)),
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(SeparatorBrushKey)),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Separator>((sep, _) => new Border
                {
                    [!Layoutable.HeightProperty] = sep[!Layoutable.HeightProperty],
                    [!Layoutable.HorizontalAlignmentProperty] = sep[!Layoutable.HorizontalAlignmentProperty],
                    [!Border.BackgroundProperty] = sep[!TemplatedControl.BackgroundProperty],
                })),
            },
        };
    }
}
