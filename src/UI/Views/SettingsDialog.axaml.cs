using APISwitch.Models;
using APISwitch.Services;
using APISwitch.UI.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.UI.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettingsService _settingsService;

    public SettingsDialog(AppSettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;

        var current = _settingsService.Load();
        ApplyToInputs(current);
    }

    private async void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        // 输入空白时,AppSettingsService.Save 内部会回退到默认值,这里不强校验。
        var settings = new AppSettings
        {
            CodexTestModel = CodexTestModelTextBox.Text ?? string.Empty,
            CodexEndpointPath = CodexEndpointPathTextBox.Text ?? string.Empty,
            CodexPromptText = CodexPromptTextBox.Text ?? string.Empty,
            ClaudeTestModel = ClaudeTestModelTextBox.Text ?? string.Empty,
            ClaudeEndpointPath = ClaudeEndpointPathTextBox.Text ?? string.Empty,
            ClaudePromptText = ClaudePromptTextBox.Text ?? string.Empty
        };

        try
        {
            _settingsService.Save(settings);
            Close(true);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"保存设置失败：{ex.Message}");
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void RestoreDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        // 只覆盖输入框,不写库;用户需点"确认"才生效。
        ApplyToInputs(AppSettings.CreateDefault());
    }

    private void ApplyToInputs(AppSettings settings)
    {
        CodexTestModelTextBox.Text = settings.CodexTestModel;
        CodexEndpointPathTextBox.Text = settings.CodexEndpointPath;
        CodexPromptTextBox.Text = settings.CodexPromptText;
        ClaudeTestModelTextBox.Text = settings.ClaudeTestModel;
        ClaudeEndpointPathTextBox.Text = settings.ClaudeEndpointPath;
        ClaudePromptTextBox.Text = settings.ClaudePromptText;
    }
}
