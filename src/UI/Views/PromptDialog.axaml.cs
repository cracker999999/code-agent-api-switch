using APISwitch.UI.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.UI.Views;

public partial class PromptDialog : Window
{
    public PromptDialog(string? content = null)
    {
        InitializeComponent();
        Title = content is null ? "新增 Prompt" : "编辑 Prompt";
        PromptTextBox.Text = content ?? string.Empty;
    }

    private async void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        var content = PromptTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            await DialogService.ShowInfoAsync(this, "提示", "Prompt 内容不能为空");
            return;
        }

        Close(content);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
