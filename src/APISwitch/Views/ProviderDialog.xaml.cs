using System.Windows;
using APISwitch.Models;
using APISwitch.Services;

namespace APISwitch.Views;

public partial class ProviderDialog : Window
{
    private readonly ModelDiscoveryService _modelDiscoveryService = new();
    private List<string> _allModels = new();

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

        ModelSearchTextBox.Text = string.Empty;
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
            SortOrder = provider.SortOrder,
            TestStatus = provider.TestStatus,
            TestModel = provider.TestModel,
            Remark = provider.Remark
        };
        Title = "编辑供应商";

        NameTextBox.Text = Provider.Name;
        RemarkTextBox.Text = Provider.Remark;
        BaseUrlTextBox.Text = Provider.BaseUrl;
        ApiKeyTextBox.Text = Provider.ApiKey;
        TestModelTextBox.Text = Provider.TestModel;
        ModelSearchTextBox.Text = string.Empty;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var remark = RemarkTextBox.Text.Trim();
        var baseUrl = BaseUrlTextBox.Text.Trim();
        var apiKey = ApiKeyTextBox.Text.Trim();
        var testModel = TestModelTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            System.Windows.MessageBox.Show(this, "Name、BaseUrl、ApiKey 不能为空", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        Provider.Name = name;
        Provider.Remark = remark;
        Provider.BaseUrl = baseUrl;
        Provider.ApiKey = apiKey;
        Provider.TestModel = testModel;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var originalContent = FetchModelsButton.Content;
        FetchModelsButton.IsEnabled = false;
        FetchModelsButton.Content = "获取中...";

        try
        {
            var probeProvider = new Provider
            {
                ToolType = Provider.ToolType,
                Name = NameTextBox.Text.Trim(),
                BaseUrl = BaseUrlTextBox.Text.Trim(),
                ApiKey = ApiKeyTextBox.Text.Trim()
            };

            var result = await _modelDiscoveryService.GetModelsAsync(probeProvider);
            if (!result.Success)
            {
                _allModels = new List<string>();
                ModelListBox.ItemsSource = _allModels;
                ModelErrorTextBlock.Text = result.ErrorMessage;
                ModelErrorTextBlock.Visibility = Visibility.Visible;
                return;
            }

            _allModels = result.Models;
            ApplyModelFilter();

            if (_allModels.Count == 0)
            {
                ModelErrorTextBlock.Text = "模型列表为空";
                ModelErrorTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                ModelErrorTextBlock.Text = string.Empty;
                ModelErrorTextBlock.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            FetchModelsButton.IsEnabled = true;
            FetchModelsButton.Content = originalContent;
        }
    }

    private void ModelSearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyModelFilter();
    }

    private void ApplyModelFilter()
    {
        var keyword = ModelSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ModelListBox.ItemsSource = _allModels;
            return;
        }

        var filtered = _allModels
            .Where(model => model.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ModelListBox.ItemsSource = filtered;
    }
}
