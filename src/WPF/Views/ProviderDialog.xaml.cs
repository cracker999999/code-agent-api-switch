using System.Text.Json;
using System.Windows;
using APISwitch.Models;
using APISwitch.Services;
using WPF.Services;

namespace WPF.Views;

public partial class ProviderDialog : Window
{
    private const string ProviderClipboardType = "APISwitch.ProviderClipboard";
    private const int ProviderClipboardVersion = 1;

    private readonly ModelDiscoveryService _modelDiscoveryService = new();
    private readonly ApiTestService _apiTestService;
    private List<string> _allModels = new();

    public Provider Provider { get; }

    public ProviderDialog(int toolType, AppSettingsService appSettingsService)
    {
        InitializeComponent();
        _apiTestService = new ApiTestService(appSettingsService);
        Provider = new Provider
        {
            ToolType = toolType,
            IsActive = false
        };
        Title = toolType switch
        {
            0 => "新增供应商（Codex）",
            1 => "新增供应商（Claude Code）",
            2 => "新增供应商（Grok）",
            _ => "新增供应商"
        };

        ModelSearchTextBox.Text = string.Empty;
        var settings = appSettingsService.Load();
        TestModelPlaceholderTextBlock.Text = toolType switch
        {
            0 => settings.CodexTestModel,
            1 => settings.ClaudeTestModel,
            2 => settings.GrokTestModel,
            _ => string.Empty
        };
    }

    public ProviderDialog(Provider provider, AppSettingsService appSettingsService)
    {
        InitializeComponent();
        _apiTestService = new ApiTestService(appSettingsService);
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
        var settings = appSettingsService.Load();
        TestModelPlaceholderTextBlock.Text = Provider.ToolType switch
        {
            0 => settings.CodexTestModel,
            1 => settings.ClaudeTestModel,
            2 => settings.GrokTestModel,
            _ => string.Empty
        };
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

    private void QuickCopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var payload = new ProviderClipboardPayload
            {
                Type = ProviderClipboardType,
                Version = ProviderClipboardVersion,
                Provider = new ProviderClipboardData
                {
                    Name = NameTextBox.Text.Trim(),
                    BaseUrl = BaseUrlTextBox.Text.Trim(),
                    ApiKey = ApiKeyTextBox.Text.Trim(),
                    TestModel = TestModelTextBox.Text.Trim(),
                    Remark = RemarkTextBox.Text.Trim()
                }
            };

            var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.Windows.Clipboard.SetText(content);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"复制失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void QuickPasteButton_Click(object sender, RoutedEventArgs e)
    {
        var clipboardContent = System.Windows.Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(clipboardContent))
        {
            System.Windows.MessageBox.Show(this, "剪贴板为空", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        ProviderClipboardPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ProviderClipboardPayload>(clipboardContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            System.Windows.MessageBox.Show(this, $"粘贴失败：结构化内容格式错误。{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // Fail Fast: 只接受本功能生成的结构化数据，避免把未知结构写入表单。
        if (payload is null ||
            payload.Provider is null ||
            !string.Equals(payload.Type, ProviderClipboardType, StringComparison.Ordinal) ||
            payload.Version != ProviderClipboardVersion)
        {
            System.Windows.MessageBox.Show(this, "粘贴失败：剪贴板内容不是有效的供应商结构化数据", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        NameTextBox.Text = payload.Provider.Name ?? string.Empty;
        BaseUrlTextBox.Text = payload.Provider.BaseUrl ?? string.Empty;
        ApiKeyTextBox.Text = payload.Provider.ApiKey ?? string.Empty;
        TestModelTextBox.Text = payload.Provider.TestModel ?? string.Empty;
        RemarkTextBox.Text = payload.Provider.Remark ?? string.Empty;
    }

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
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

    private async void TestModelButton_Click(object sender, RoutedEventArgs e)
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
                DialogService.ShowInfo(this, "测试成功", $"供应商：{probeProvider.Name}\n响应时间：{result.ResponseTimeMs ?? 0} ms");
                return;
            }

            DialogService.ShowError(this, "测试失败", $"供应商：{probeProvider.Name}\n{result.Message}");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(this, "测试失败", $"测试过程异常：{ex.Message}");
        }
        finally
        {
            TestModelButton.Content = originalContent;
            TestModelButton.IsEnabled = true;
        }
    }

    private void ModelSearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyModelFilter();
    }

    private void ModelListBoxItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: string model } ||
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
            ToolType = Provider.ToolType,
            Name = NameTextBox.Text.Trim(),
            BaseUrl = BaseUrlTextBox.Text.Trim(),
            ApiKey = ApiKeyTextBox.Text.Trim(),
            TestModel = includeTestModel ? TestModelTextBox.Text.Trim() : string.Empty
        };
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

    private sealed class ProviderClipboardPayload
    {
        public string Type { get; set; } = string.Empty;

        public int Version { get; set; }

        public ProviderClipboardData? Provider { get; set; }
    }
}
