using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WPF.Services;

// WPF 系统 MessageBox 的文字不可选中,这里用一个最小自定义 Window 替代。
// 仅供需要复制错误信息的场景使用(如 API 测试结果)。
// 项目同时引用 WinForms 和 WPF 命名空间,所有 UI 类型都用全限定名以避免歧义。
internal static class DialogService
{
    public static void ShowInfo(Window owner, string title, string message)
    {
        ShowMessage(owner, title, message);
    }

    public static void ShowError(Window owner, string title, string message)
    {
        ShowMessage(owner, title, message);
    }

    private static void ShowMessage(Window owner, string title, string message)
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
            Margin = new Thickness(0, 0, 0, 14)
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "确认",
            MinWidth = 80,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };

        var buttonRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Children = { okButton }
        };

        var panel = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(20),
            Children = { textBox, buttonRow }
        };

        var dialog = new Window
        {
            Title = title,
            Owner = owner,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            MinWidth = 320
        };

        okButton.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }
}
