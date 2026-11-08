using System.Collections.ObjectModel;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.UI.Views;

public partial class ModelSearchDialog : Window
{
    private readonly IReadOnlyList<Provider> _providers;
    private readonly ModelDiscoveryService _modelDiscoveryService;
    private readonly ObservableCollection<ModelSearchResult> _matches = new();
    private CancellationTokenSource? _searchCancellation;

    public ModelSearchDialog(IReadOnlyList<Provider> providers, ModelDiscoveryService modelDiscoveryService)
    {
        _providers = providers;
        _modelDiscoveryService = modelDiscoveryService;
        InitializeComponent();
        ResultsItemsControl.ItemsSource = _matches;
        ProgressTextBlock.Text = providers.Count == 0 ? "暂无供应商" : $"共 {providers.Count} 个供应商";
        Opened += (_, _) => ModelIdTextBox.Focus();
        // 关闭窗口必须取消本批请求，避免在后台继续查询供应商。
        Closed += (_, _) => _searchCancellation?.Cancel();
    }

    private async void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_searchCancellation is not null)
        {
            _searchCancellation.Cancel();
            SearchButton.IsEnabled = false;
            return;
        }

        var modelId = ModelIdTextBox.Text?.Trim() ?? string.Empty;
        if (modelId.Length == 0)
        {
            ProgressTextBlock.Text = "请输入模型 ID";
            ModelIdTextBox.Focus();
            return;
        }

        _matches.Clear();
        ErrorsTextBox.Text = string.Empty;
        ErrorsExpander.IsVisible = false;
        if (_providers.Count == 0)
        {
            ProgressTextBlock.Text = "暂无供应商";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        ModelIdTextBox.IsEnabled = false;
        SearchButton.Content = "停止";
        var completed = 0;
        var failed = 0;
        ProgressTextBlock.Text = $"正在查询：0/{_providers.Count}";

        try
        {
            await foreach (var result in _modelDiscoveryService.FindProvidersByModelAsync(_providers, modelId, cancellation.Token))
            {
                completed++;
                if (result.IsMatch)
                {
                    _matches.Add(result);
                }

                if (result.ErrorMessage.Length > 0)
                {
                    failed++;
                    ErrorsTextBox.Text += $"{result.Provider.Name}（{result.ToolName}）\n{result.ErrorMessage}\n\n";
                    ErrorsExpander.Header = $"查询失败（{failed}）";
                    ErrorsExpander.IsVisible = true;
                }

                ProgressTextBlock.Text = completed < _providers.Count
                    ? $"已查询 {completed}/{_providers.Count}，找到 {_matches.Count} 个，失败 {failed} 个"
                    : $"查询完成：找到 {_matches.Count} 个供应商，失败 {failed} 个";
            }

            if (_matches.Count == 0 && failed == 0)
            {
                ProgressTextBlock.Text = $"未找到提供该模型的供应商（已查询 {completed} 个）";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ProgressTextBlock.Text = $"已停止：已查询 {completed}/{_providers.Count}，找到 {_matches.Count} 个，失败 {failed} 个";
        }
        catch (Exception ex)
        {
            ProgressTextBlock.Text = $"查询失败：{ex.Message}";
        }
        finally
        {
            _searchCancellation = null;
            ModelIdTextBox.IsEnabled = true;
            SearchButton.IsEnabled = true;
            SearchButton.Content = "查询";
        }
    }
}
