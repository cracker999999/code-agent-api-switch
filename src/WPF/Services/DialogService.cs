using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WPF.Services;

// WPF 系统 MessageBox 的文字不可选中,这里用一个最小自定义 Window 替代。
// 统一用于提示和确认，避免系统 MessageBox 的提示音，并保留文字复制能力。
// 项目同时引用 WinForms 和 WPF 命名空间,所有 UI 类型都用全限定名以避免歧义。
internal static class DialogService
{
    public static void ShowInfo(Window? owner, string title, string message)
    {
        ShowMessage(owner, title, message);
    }

    public static void ShowError(Window? owner, string title, string message)
    {
        ShowMessage(owner, title, message);
    }

    public static bool Confirm(Window owner, string title, string message)
    {
        return ShowMessage(owner, title, message, showCancel: true);
    }

    private static bool ShowMessage(Window? owner, string title, string message, bool showCancel = false)
    {
        // 用只读 TextBox 承载文字,自带选中 / 复制 / 右键菜单能力
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = message,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            MaxHeight = 360,
            MaxWidth = 460,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 20)
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = showCancel ? "是(_Y)" : "确认",
            MinWidth = 80,
            MinHeight = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = !showCancel
        };

        var buttonRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Children = { okButton }
        };

        if (showCancel)
        {
            buttonRow.Children.Add(new System.Windows.Controls.Button
            {
                Content = "否(_N)",
                MinWidth = 80,
                MinHeight = 32,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true
            });
        }

        var panel = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(20, 28, 20, 28),
            Children = { textBox, buttonRow }
        };

        var dialog = new Window
        {
            Title = title,
            Owner = owner,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = owner is null,
            MinWidth = 320
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;
        // 只有明确确认才返回 true；“否”、Esc 和关闭窗口都不得触发删除。
        return dialog.ShowDialog() == true;
    }
}
