using System.Windows;
using WPF.Services;

namespace WPF.Views;

public partial class PromptDialog : Window
{
    public string PromptContent { get; private set; } = string.Empty;

    public PromptDialog(string? content = null)
    {
        InitializeComponent();
        Title = content is null ? "新增 Prompt" : "编辑 Prompt";
        PromptTextBox.Text = content ?? string.Empty;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var content = PromptTextBox.Text;
        if (string.IsNullOrWhiteSpace(content))
        {
            DialogService.ShowInfo(this, "提示", "Prompt 内容不能为空");
            return;
        }

        PromptContent = content;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
