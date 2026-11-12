using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DictionaryEntry;
using System.Collections.Generic;
using System.Globalization;

namespace PowerGauge.Tray;

public readonly record struct TrayIconThemePalette(bool IsDark, Color AccentColor)
{
    public static TrayIconThemePalette Default { get; } = new(IsDark: true, Color.Parse("#4B5563"));

    public static TrayIconThemePalette FromPlatformColors(PlatformColorValues colors)
    {
        var isDark = colors.ThemeVariant == PlatformThemeVariant.Dark;
        var accent = colors.AccentColor1.A > 0 ? colors.AccentColor1 : Color.Parse("#4B5563");
        return new TrayIconThemePalette(isDark, accent);
    }
}

public sealed class TrayIconRenderer
{
    private static readonly PixelSize IconSize = new(32, 32);
    private static readonly Vector IconDpi = new(96, 96);
    private readonly Dictionary<int, WindowIcon> _batteryIcons = [];
    private TrayIconThemePalette _palette = TrayIconThemePalette.Default;
    private WindowIcon? _placeholderIcon;

    internal TrayIconThemePalette CurrentPalette => _palette;

    internal int CachedBatteryIconCount => _batteryIcons.Count;

    public bool UpdateTheme(TrayIconThemePalette palette)
    {
        if (_palette == palette)
        {
            return false;
        }

        _palette = palette;
        _batteryIcons.Clear();
        _placeholderIcon = null;
        return true;
    }

    public WindowIcon Render(int? batteryPercent)
    {
        return batteryPercent is int percent
            ? _batteryIcons.Entry(percent).OrInsertWithKey(this, static (pct, self) => self.CreateBatteryIcon(pct))
            : _placeholderIcon ??= CreatePlaceholderIcon();
    }

    internal static IImmutableSolidColorBrush GetReadableForeground(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.62 ? Brushes.Black : Brushes.White;
    }

    internal Color GetBatteryBackgroundColor(int batteryPercent)
    {
        if (_palette.IsDark)
        {
            return batteryPercent switch
            {
                <= 20 => Color.Parse("#EF4444"),
                <= 40 => Color.Parse("#FACC15"),
                _ => Color.Parse("#22C55E"),
            };
        }

        return batteryPercent switch
        {
            <= 20 => Color.Parse("#DC2626"),
            <= 40 => Color.Parse("#EAB308"),
            _ => Color.Parse("#16A34A"),
        };
    }

    internal Color GetBorderColor()
    {
        return _palette.IsDark
            ? Color.Parse("#59FFFFFF")
            : Color.Parse("#400F172A");
    }

    private WindowIcon CreatePlaceholderIcon()
    {
        var baseSurface = _palette.IsDark ? Color.Parse("#374151") : Color.Parse("#E5E7EB");
        var blended = BlendColor(baseSurface, _palette.AccentColor, 0.45);
        return CreateIcon("?", blended, GetReadableForeground(blended));
    }

    private WindowIcon CreateBatteryIcon(int percent)
    {
        var background = GetBatteryBackgroundColor(percent);
        return CreateIcon(
            percent.ToString(CultureInfo.InvariantCulture),
            background,
            GetReadableForeground(background));
    }

    private WindowIcon CreateIcon(string label, Color background, IBrush foreground)
    {
        var visual = new Border
        {
            Width = IconSize.Width,
            Height = IconSize.Height,
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(GetBorderColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = label,
                Foreground = foreground,
                FontWeight = FontWeight.Bold,
                FontSize = GetFontSize(label),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };

        visual.Measure(new Size(IconSize.Width, IconSize.Height));
        visual.Arrange(new Rect(0, 0, IconSize.Width, IconSize.Height));

        var bitmap = new RenderTargetBitmap(IconSize, IconDpi);
        bitmap.Render(visual);
        return new WindowIcon(bitmap);
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        var inv = 1.0 - amount;
        return Color.FromRgb(
            (byte)(from.R * inv + to.R * amount),
            (byte)(from.G * inv + to.G * amount),
            (byte)(from.B * inv + to.B * amount));
    }

    private static double GetFontSize(string label)
    {
        return label.Length switch
        {
            1 => 19,
            2 => 16,
            _ => 12,
        };
    }
}
