using System.Windows;
using APISwitch.Models;
using APISwitch.Services;

namespace WPF.Views;

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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        // 输入空白时,AppSettingsService.Save 内部会回退到默认值,这里不强校验。
        var settings = new AppSettings
        {
            CodexTestModel = CodexTestModelTextBox.Text,
            CodexEndpointPath = CodexEndpointPathTextBox.Text,
            CodexPromptText = CodexPromptTextBox.Text,
            CodexVersion = CodexVersionTextBox.Text,
            ClaudeTestModel = ClaudeTestModelTextBox.Text,
            ClaudeEndpointPath = ClaudeEndpointPathTextBox.Text,
            ClaudePromptText = ClaudePromptTextBox.Text,
            ClaudeVersion = ClaudeVersionTextBox.Text
        };

        try
        {
            _settingsService.Save(settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"保存设置失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        // 只覆盖输入框,不写库;用户需点"确认"才生效。
        ApplyToInputs(AppSettings.CreateDefault());
    }

    private void ApplyToInputs(AppSettings settings)
    {
        CodexTestModelTextBox.Text = settings.CodexTestModel;
        CodexEndpointPathTextBox.Text = settings.CodexEndpointPath;
        CodexPromptTextBox.Text = settings.CodexPromptText;
        CodexVersionTextBox.Text = settings.CodexVersion;
        ClaudeTestModelTextBox.Text = settings.ClaudeTestModel;
        ClaudeEndpointPathTextBox.Text = settings.ClaudeEndpointPath;
        ClaudePromptTextBox.Text = settings.ClaudePromptText;
        ClaudeVersionTextBox.Text = settings.ClaudeVersion;
    }
}
