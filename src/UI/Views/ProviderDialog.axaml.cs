using APISwitch.UI.Services;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Text.Json;

namespace APISwitch.UI.Views;

public partial class ProviderDialog : Window
{
    private const string ProviderClipboardType = "APISwitch.ProviderClipboard";
    private const int ProviderClipboardVersion = 1;

    private readonly ModelDiscoveryService _modelDiscoveryService = new();
    private readonly ApiTestService _apiTestService;
    private List<string> _allModels = new();

    private readonly Provider _provider;

    public ProviderDialog(int toolType, AppSettingsService appSettingsService)
    {
        InitializeComponent();
        _apiTestService = new ApiTestService(appSettingsService);

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

    public ProviderDialog(Provider provider, AppSettingsService appSettingsService)
    {
        InitializeComponent();
        _apiTestService = new ApiTestService(appSettingsService);

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

    private async void QuickCopyButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            await DialogService.ShowErrorAsync(this, "错误", "复制失败：无法访问剪贴板");
            return;
        }

        try
        {
            var payload = new ProviderClipboardPayload
            {
                Type = ProviderClipboardType,
                Version = ProviderClipboardVersion,
                Provider = new ProviderClipboardData
                {
                    Name = NameTextBox.Text?.Trim() ?? string.Empty,
                    BaseUrl = BaseUrlTextBox.Text?.Trim() ?? string.Empty,
                    ApiKey = ApiKeyTextBox.Text?.Trim() ?? string.Empty,
                    TestModel = TestModelTextBox.Text?.Trim() ?? string.Empty,
                    Remark = RemarkTextBox.Text?.Trim() ?? string.Empty
                }
            };

            var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await clipboard.SetTextAsync(content);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"复制失败：{ex.Message}");
        }
    }

    private async void QuickPasteButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            await DialogService.ShowErrorAsync(this, "错误", "粘贴失败：无法访问剪贴板");
            return;
        }

        var clipboardContent = await clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(clipboardContent))
        {
            await DialogService.ShowInfoAsync(this, "提示", "剪贴板为空");
            return;
        }

        ProviderClipboardPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ProviderClipboardPayload>(clipboardContent);
        }
        catch (JsonException ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"粘贴失败：结构化内容格式错误。{ex.Message}");
            return;
        }

        // Fail Fast: 只接受本功能写入的结构化数据，避免未知结构污染表单输入。
        if (payload is null ||
            payload.Provider is null ||
            !string.Equals(payload.Type, ProviderClipboardType, StringComparison.Ordinal) ||
            payload.Version != ProviderClipboardVersion)
        {
            await DialogService.ShowErrorAsync(this, "错误", "粘贴失败：剪贴板内容不是有效的供应商结构化数据");
            return;
        }

        NameTextBox.Text = payload.Provider.Name ?? string.Empty;
        BaseUrlTextBox.Text = payload.Provider.BaseUrl ?? string.Empty;
        ApiKeyTextBox.Text = payload.Provider.ApiKey ?? string.Empty;
        TestModelTextBox.Text = payload.Provider.TestModel ?? string.Empty;
        RemarkTextBox.Text = payload.Provider.Remark ?? string.Empty;
    }

    private async void FetchModelsButton_Click(object? sender, RoutedEventArgs e)
    {
        var originalContent = FetchModelsButton.Content;
        FetchModelsButton.IsEnabled = false;
        FetchModelsButton.Content = "获取中...";

        try
        {
            var probeProvider = BuildProbeProvider(includeTestModel: false);

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

    private async void TestModelButton_Click(object? sender, RoutedEventArgs e)
    {
        var originalContent = TestModelButton.Content;
        TestModelButton.IsEnabled = false;
        TestModelButton.Content = "测试中...";

        try
        {
            var probeProvider = BuildProbeProvider(includeTestModel: true);

            var result = await _apiTestService.TestProviderAsync(probeProvider);
            if (result.Success)
            {
                await DialogService.ShowInfoAsync(this, "测试成功", $"供应商：{probeProvider.Name}\n响应时间：{result.ResponseTimeMs ?? 0} ms");
                return;
            }

            await DialogService.ShowErrorAsync(this, "测试失败", $"供应商：{probeProvider.Name}\n{result.Message}");
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "测试失败", $"测试过程异常：{ex.Message}");
        }
        finally
        {
            TestModelButton.Content = originalContent;
            TestModelButton.IsEnabled = true;
        }
    }

    private void ModelSearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyModelFilter();
    }

    private void ModelListBoxItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: string model } ||
            string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        TestModelTextBox.Text = model;
    }

    private Provider BuildProbeProvider(bool includeTestModel)
    {
        return new Provider
        {
            ToolType = _provider.ToolType,
            Name = NameTextBox.Text?.Trim() ?? string.Empty,
            BaseUrl = BaseUrlTextBox.Text?.Trim() ?? string.Empty,
            ApiKey = ApiKeyTextBox.Text?.Trim() ?? string.Empty,
            TestModel = includeTestModel ? (TestModelTextBox.Text?.Trim() ?? string.Empty) : string.Empty
        };
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

    private sealed class ProviderClipboardPayload
    {
        public string Type { get; set; } = string.Empty;

        public int Version { get; set; }

        public ProviderClipboardData? Provider { get; set; }
    }
}
