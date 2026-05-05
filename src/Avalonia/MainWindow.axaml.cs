using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using APISwitch.Avalonia.Services;
using APISwitch.Avalonia.Views;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.Avalonia;

public partial class MainWindow : Window
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigWriterService _configWriterService;
    private readonly ApiTestService _apiTestService;

    private SessionWindow? _sessionWindow;
    private int _currentToolType;

    public MainWindow(DatabaseService databaseService, ConfigWriterService configWriterService)
    {
        _databaseService = databaseService;
        _configWriterService = configWriterService;
        _apiTestService = new ApiTestService();

        _currentToolType = 0;
        InitializeComponent();

        UpdateTabButtons();
        LoadProviders();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    public async void OpenSessionManagerWindow()
    {
        try
        {
            var providerId = GetCurrentProviderId();
            if (_sessionWindow is null)
            {
                _sessionWindow = new SessionWindow(providerId)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _sessionWindow.Closed += (_, _) => _sessionWindow = null;

                if (IsVisible)
                {
                    _sessionWindow.Show(this);
                }
                else
                {
                    _sessionWindow.Show();
                }

                return;
            }

            await _sessionWindow.SelectProviderAsync(providerId);
            _sessionWindow.ShowAndActivate();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"打开会话管理失败：{ex.Message}");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (global::Avalonia.Application.Current is App app && app.HasStatusIcon && !app.IsExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void CodexTabButton_Click(object? sender, RoutedEventArgs e)
    {
        _currentToolType = 0;
        UpdateTabButtons();
        LoadProviders();
    }

    private void ClaudeTabButton_Click(object? sender, RoutedEventArgs e)
    {
        _currentToolType = 1;
        UpdateTabButtons();
        LoadProviders();
    }

    private async void AddProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ProviderDialog(_currentToolType);
        var provider = await dialog.ShowDialog<Provider?>(this);
        if (provider is null)
        {
            return;
        }

        provider.SortOrder = GetNextSortOrder();
        _databaseService.AddProvider(provider);
        LoadProviders();
    }

    private void SessionManagerButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenSessionManagerWindow();
    }

    private void OpenConfigDirectoryButton_Click(object? sender, RoutedEventArgs e)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targetDirectory = _currentToolType == 0
            ? Path.Combine(userProfile, ".codex")
            : Path.Combine(userProfile, ".claude");

        try
        {
            Directory.CreateDirectory(targetDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = targetDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowErrorAsync(this, "错误", $"打开目录失败：{ex.Message}");
        }
    }

    private void BaseUrlButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        var input = provider.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        try
        {
            var openUrl = BuildOpenUrl(input);
            Process.Start(new ProcessStartInfo
            {
                FileName = openUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowErrorAsync(this, "错误", $"打开链接失败：{ex.Message}");
        }
    }

    private async void ActivateProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        if (provider.IsActive)
        {
            return;
        }

        try
        {
            _databaseService.ActivateProvider(provider.Id, provider.ToolType);
            _configWriterService.ApplyProvider(provider);
            LoadProviders();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "启用失败", ex.Message);
        }
    }

    private async void TestProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Provider provider } button)
        {
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "测试中...";

        try
        {
            var result = await _apiTestService.TestProviderAsync(provider);
            if (result.Success)
            {
                _databaseService.UpdateTestStatus(provider.Id, 1);
                LoadProviders();
                await DialogService.ShowInfoAsync(this, "测试成功", $"响应时间：{result.ResponseTimeMs ?? 0} ms");
                return;
            }

            _databaseService.UpdateTestStatus(provider.Id, 2);
            LoadProviders();
            await DialogService.ShowErrorAsync(this, "测试失败", result.Message);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private async void EditProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        await OpenEditProviderDialogAsync(provider);
    }

    private async Task OpenEditProviderDialogAsync(Provider provider)
    {
        var dialog = new ProviderDialog(provider);
        var updatedProvider = await dialog.ShowDialog<Provider?>(this);
        if (updatedProvider is null)
        {
            return;
        }

        var hasConnectionChanged =
            !string.Equals(provider.BaseUrl, updatedProvider.BaseUrl, StringComparison.Ordinal) ||
            !string.Equals(provider.ApiKey, updatedProvider.ApiKey, StringComparison.Ordinal);

        if (hasConnectionChanged)
        {
            updatedProvider.TestStatus = 0;
        }

        _databaseService.UpdateProvider(updatedProvider);
        if (updatedProvider.IsActive && hasConnectionChanged)
        {
            _configWriterService.ApplyProvider(updatedProvider);
        }

        LoadProviders();
    }

    private async void DeleteProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(this, "删除确认", $"确认删除供应商“{provider.Name}”吗？");
        if (!confirmed)
        {
            return;
        }

        _databaseService.DeleteProvider(provider.Id);
        LoadProviders();
    }

    private void MoveProviderUpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        _databaseService.MoveProviderUp(provider.Id, _currentToolType);
        LoadProviders();
    }

    private void MoveProviderDownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider })
        {
            return;
        }

        _databaseService.MoveProviderDown(provider.Id, _currentToolType);
        LoadProviders();
    }

    private void LoadProviders()
    {
        var providers = _databaseService.GetProviders(_currentToolType);
        for (var index = 0; index < providers.Count; index++)
        {
            providers[index].CanMoveUp = index > 0;
            providers[index].CanMoveDown = index < providers.Count - 1;
        }

        ProvidersItemsControl.ItemsSource = providers;
    }

    private int GetNextSortOrder()
    {
        var providers = _databaseService.GetProviders(_currentToolType);
        if (providers.Count == 0)
        {
            return 1;
        }

        return providers.Max(item => item.SortOrder) + 1;
    }

    private string GetCurrentProviderId()
    {
        return _currentToolType == 1
            ? SessionService.ProviderClaude
            : SessionService.ProviderCodex;
    }

    private void UpdateTabButtons()
    {
        SetTabButtonSelectedState(CodexTabButton, _currentToolType == 0);
        SetTabButtonSelectedState(ClaudeTabButton, _currentToolType == 1);
    }

    private static string BuildOpenUrl(string input)
    {
        var normalized = input;
        if (!Regex.IsMatch(normalized, @"^[a-z][a-z0-9+\-.]*://", RegexOptions.IgnoreCase))
        {
            normalized = $"https://{normalized}";
        }

        return Regex.Replace(normalized, @"/v1/?(?=($|[?#]))", string.Empty, RegexOptions.IgnoreCase);
    }

    private static void SetTabButtonSelectedState(Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Background = global::Avalonia.Media.Brush.Parse("#2563EB");
            button.Foreground = global::Avalonia.Media.Brushes.White;
            button.BorderBrush = global::Avalonia.Media.Brush.Parse("#1D4ED8");
            button.BorderThickness = new global::Avalonia.Thickness(1);
            return;
        }

        button.Background = global::Avalonia.Media.Brushes.White;
        button.Foreground = global::Avalonia.Media.Brush.Parse("#111827");
        button.BorderBrush = global::Avalonia.Media.Brush.Parse("#D1D5DB");
        button.BorderThickness = new global::Avalonia.Thickness(1);
    }
}
