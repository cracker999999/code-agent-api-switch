using System.Windows;
using APISwitch.Models;
using APISwitch.Services;
using WPF.Services;

namespace WPF.Views;

public partial class PromptWindow : Window
{
    private readonly DatabaseService _databaseService;

    public PromptWindow(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        InitializeComponent();
        LoadPrompts();
    }

    private void AddPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _databaseService.AddPrompt(new PromptItem { Content = dialog.PromptContent });
        LoadPrompts();
    }

    private void EditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PromptItem prompt })
        {
            return;
        }

        var dialog = new PromptDialog(prompt.Content) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        prompt.Content = dialog.PromptContent;
        _databaseService.UpdatePrompt(prompt);
        LoadPrompts();
    }

    private void DeletePromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PromptItem prompt })
        {
            return;
        }

        if (!DialogService.Confirm(this, "删除确认", "确认删除这条 Prompt 吗？"))
        {
            return;
        }

        _databaseService.DeletePrompt(prompt.Id);
        LoadPrompts();
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PromptItem prompt })
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(prompt.Content);
        }
        catch (Exception ex)
        {
            DialogService.ShowError(this, "错误", $"复制失败：{ex.Message}");
        }
    }

    private void LoadPrompts()
    {
        var prompts = _databaseService.GetPrompts();
        PromptItemsControl.ItemsSource = prompts;
        EmptyPromptTextBlock.Visibility = prompts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
