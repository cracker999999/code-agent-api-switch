using APISwitch.Models;
using APISwitch.Services;
using APISwitch.UI.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.UI.Views;

public partial class PromptWindow : Window
{
    private readonly DatabaseService _databaseService;

    public PromptWindow(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        InitializeComponent();
        LoadPrompts();
    }

    private async void AddPromptButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new PromptDialog();
        var content = await dialog.ShowDialog<string?>(this);
        if (content is null)
        {
            return;
        }

        _databaseService.AddPrompt(new PromptItem { Content = content });
        LoadPrompts();
    }

    private async void EditPromptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PromptItem prompt })
        {
            return;
        }

        var dialog = new PromptDialog(prompt.Content);
        var content = await dialog.ShowDialog<string?>(this);
        if (content is null)
        {
            return;
        }

        prompt.Content = content;
        _databaseService.UpdatePrompt(prompt);
        LoadPrompts();
    }

    private async void DeletePromptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PromptItem prompt })
        {
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(this, "删除确认", "确认删除这条 Prompt 吗？");
        if (!confirmed)
        {
            return;
        }

        _databaseService.DeletePrompt(prompt.Id);
        LoadPrompts();
    }

    private async void CopyPromptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PromptItem prompt })
        {
            return;
        }

        await ClipboardService.CopyTextAsync(this, prompt.Content);
    }

    private void LoadPrompts()
    {
        var prompts = _databaseService.GetPrompts();
        PromptItemsControl.ItemsSource = prompts;
        EmptyPromptTextBlock.IsVisible = prompts.Count == 0;
    }
}
