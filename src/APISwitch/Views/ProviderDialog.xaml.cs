using System.Windows;
using APISwitch.Models;

namespace APISwitch.Views;

public partial class ProviderDialog : Window
{
    public Provider Provider { get; }

    public ProviderDialog(int toolType)
    {
        InitializeComponent();
        Provider = new Provider
        {
            ToolType = toolType,
            IsActive = false
        };
        Title = toolType switch
        {
            0 => "新增供应商（Codex）",
            1 => "新增供应商（Claude Code）",
            _ => "新增供应商"
        };
    }

    public ProviderDialog(Provider provider)
    {
        InitializeComponent();
        Provider = new Provider
        {
            Id = provider.Id,
            ToolType = provider.ToolType,
            Name = provider.Name,
            BaseUrl = provider.BaseUrl,
            ApiKey = provider.ApiKey,
            IsActive = provider.IsActive,
            SortOrder = provider.SortOrder
        };
        Title = "编辑供应商";

        NameTextBox.Text = Provider.Name;
        BaseUrlTextBox.Text = Provider.BaseUrl;
        ApiKeyTextBox.Text = Provider.ApiKey;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var baseUrl = BaseUrlTextBox.Text.Trim();
        var apiKey = ApiKeyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            System.Windows.MessageBox.Show(this, "Name、BaseUrl、ApiKey 不能为空", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        Provider.Name = name;
        Provider.BaseUrl = baseUrl;
        Provider.ApiKey = apiKey;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

