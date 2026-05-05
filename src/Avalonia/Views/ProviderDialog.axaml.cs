using APISwitch.Avalonia.Services;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.Avalonia.Views;

public partial class ProviderDialog : Window
{
    private readonly ModelDiscoveryService _modelDiscoveryService = new();
    private List<string> _allModels = new();

    private readonly Provider _provider;

    public ProviderDialog(int toolType)
    {
        InitializeComponent();

        _provider = new Provider
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

        _provider = new Provider
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

        NameTextBox.Text = _provider.Name;
        RemarkTextBox.Text = _provider.Remark;
        BaseUrlTextBox.Text = _provider.BaseUrl;
        ApiKeyTextBox.Text = _provider.ApiKey;
        TestModelTextBox.Text = _provider.TestModel;
    }

    private async void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        var remark = RemarkTextBox.Text?.Trim() ?? string.Empty;
        var baseUrl = BaseUrlTextBox.Text?.Trim() ?? string.Empty;
        var apiKey = ApiKeyTextBox.Text?.Trim() ?? string.Empty;
        var testModel = TestModelTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            await DialogService.ShowInfoAsync(this, "提示", "Name、BaseUrl、ApiKey 不能为空");
            return;
        }

        _provider.Name = name;
        _provider.Remark = remark;
        _provider.BaseUrl = baseUrl;
        _provider.ApiKey = apiKey;
        _provider.TestModel = testModel;

        Close(_provider);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private async void FetchModelsButton_Click(object? sender, RoutedEventArgs e)
    {
        var originalContent = FetchModelsButton.Content;
        FetchModelsButton.IsEnabled = false;
        FetchModelsButton.Content = "获取中...";

        try
        {
            var probeProvider = new Provider
            {
                ToolType = _provider.ToolType,
                Name = NameTextBox.Text?.Trim() ?? string.Empty,
                BaseUrl = BaseUrlTextBox.Text?.Trim() ?? string.Empty,
                ApiKey = ApiKeyTextBox.Text?.Trim() ?? string.Empty
            };

            var result = await _modelDiscoveryService.GetModelsAsync(probeProvider);
            if (!result.Success)
            {
                _allModels = new List<string>();
                ModelListBox.ItemsSource = _allModels;
                ModelErrorTextBlock.Text = result.ErrorMessage;
                ModelErrorTextBlock.IsVisible = true;
                return;
            }

            _allModels = result.Models;
            ApplyModelFilter();

            if (_allModels.Count == 0)
            {
                ModelErrorTextBlock.Text = "模型列表为空";
                ModelErrorTextBlock.IsVisible = true;
            }
            else
            {
                ModelErrorTextBlock.Text = string.Empty;
                ModelErrorTextBlock.IsVisible = false;
            }
        }
        finally
        {
            FetchModelsButton.IsEnabled = true;
            FetchModelsButton.Content = originalContent;
        }
    }

    private void ModelSearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyModelFilter();
    }

    private void ApplyModelFilter()
    {
        var keyword = ModelSearchTextBox.Text?.Trim() ?? string.Empty;
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
