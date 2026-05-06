using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace APISwitch.Avalonia.Services;

internal static class DialogService
{
    public static async Task ShowInfoAsync(Window owner, string title, string message)
    {
        var dialog = CreateMessageDialog(title, message, showCancel: false);
        await dialog.ShowDialog<bool>(owner);
    }

    public static Task ShowErrorAsync(Window owner, string title, string message)
    {
        return ShowInfoAsync(owner, title, message);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = CreateMessageDialog(title, message, showCancel: true);
        var result = await dialog.ShowDialog<bool>(owner);
        return result;
    }

    private static Window CreateMessageDialog(string title, string message, bool showCancel)
    {
        var okButton = new Button
        {
            Content = "确认",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 80,
            IsVisible = showCancel,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                okButton,
                cancelButton
            }
        };

        var panel = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 420
                },
                buttons
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        okButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        return dialog;
    }
}
