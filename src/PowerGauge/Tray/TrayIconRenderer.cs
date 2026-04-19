using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using System.Globalization;

namespace PowerGauge.Tray;

public sealed class TrayIconRenderer
{
    private static readonly PixelSize IconSize = new(32, 32);
    private static readonly Vector IconDpi = new(96, 96);
    private readonly Dictionary<int, WindowIcon> _batteryIcons = [];
    private WindowIcon? _placeholderIcon;

    public WindowIcon Render(int? batteryPercent)
    {
        if (batteryPercent is not int percent)
        {
            return _placeholderIcon ??= CreateIcon("?", Color.Parse("#6b7280"), Brushes.White);
        }

        if (_batteryIcons.TryGetValue(percent, out var icon))
        {
            return icon;
        }

        var background = GetBackgroundColor(percent);
        var foreground = percent is > 40 and <= 100
            ? Brushes.White
            : Brushes.Black;
        var label = percent.ToString(CultureInfo.InvariantCulture);

        icon = CreateIcon(label, background, foreground);
        _batteryIcons[percent] = icon;
        return icon;
    }

    private static WindowIcon CreateIcon(string label, Color background, IBrush foreground)
    {
        var visual = new Border
        {
            Width = IconSize.Width,
            Height = IconSize.Height,
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(Color.Parse("#111827")),
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

    private static double GetFontSize(string label)
    {
        return label.Length switch
        {
            1 => 19,
            2 => 16,
            _ => 12,
        };
    }

    private static Color GetBackgroundColor(int batteryPercent)
    {
        return batteryPercent switch
        {
            <= 20 => Color.Parse("#dc2626"),
            <= 40 => Color.Parse("#facc15"),
            _ => Color.Parse("#16a34a"),
        };
    }
}
