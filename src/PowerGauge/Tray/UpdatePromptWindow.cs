using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using PowerGauge.Updates;

namespace PowerGauge.Tray;

internal sealed class UpdatePromptWindow : Window
{
    private static UpdatePromptWindow? s_activeWindow;
    private readonly TaskCompletionSource<bool> _resultTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private UpdatePromptWindow(UpdatePromptInfo prompt, WindowIcon? icon)
    {
        Title = $"{AppIdentity.ProductName} Update";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Padding = new Thickness(20, 18);
        Icon = icon;
        Content = BuildContent(prompt, CompleteAndClose);

        Closed += (_, _) =>
        {
            if (ReferenceEquals(s_activeWindow, this))
            {
                s_activeWindow = null;
            }

            _resultTcs.TrySetResult(false);
        };
    }

    public static Task<bool> ShowPromptAsync(UpdatePromptInfo prompt, WindowIcon? icon = null)
    {
        if (s_activeWindow is { } existing)
        {
            existing.Activate();
            return existing._resultTcs.Task;
        }

        var window = new UpdatePromptWindow(prompt, icon);
        s_activeWindow = window;
        window.Show();
        window.Activate();
        return window._resultTcs.Task;
    }

    public static void DismissActive() => s_activeWindow?.Close();

    internal static StackPanel BuildContent(UpdatePromptInfo prompt, Action<bool> onDecision)
    {
        var heading = new TextBlock
        {
            Text = $"{AppIdentity.ProductName} {prompt.AvailableVersion} is available",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        var message = new TextBlock
        {
            Text = prompt.CanSelfUpdate
                ? $"You are currently running {prompt.CurrentVersion}. Would you like to install the update and restart now?"
                : $"You are currently running {prompt.CurrentVersion}. Would you like to open the download page now?",
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension(TrayMenuTheme.ItemDisabledForegroundKey),
        };

        var laterButton = new Button
        {
            Content = "Later",
            MinWidth = 84,
            IsCancel = true,
        };
        laterButton.Click += (_, _) => onDecision(false);

        var confirmButton = new Button
        {
            Content = prompt.CanSelfUpdate ? $"Install update {prompt.AvailableVersion}" : "Open release page",
            MinWidth = 112,
            IsDefault = true,
        };
        confirmButton.Classes.Add("accent");
        confirmButton.Click += (_, _) => onDecision(true);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                laterButton,
                confirmButton,
            },
        };

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                heading,
                message,
                buttonRow,
            },
        };
    }

    private void CompleteAndClose(bool accepted)
    {
        _resultTcs.TrySetResult(accepted);
        Close();
    }
}
