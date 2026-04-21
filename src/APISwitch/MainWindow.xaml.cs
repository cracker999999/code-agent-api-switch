using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APISwitch.Models;
using APISwitch.Services;
using APISwitch.Views;

namespace APISwitch;

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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void CodexTabButton_Click(object sender, RoutedEventArgs e)
    {
        _currentToolType = 0;
        UpdateTabButtons();
        LoadProviders();
    }

    private void ClaudeTabButton_Click(object sender, RoutedEventArgs e)
    {
        _currentToolType = 1;
        UpdateTabButtons();
        LoadProviders();
    }

    private void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProviderDialog(_currentToolType)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var provider = dialog.Provider;
        provider.SortOrder = GetNextSortOrder();
        _databaseService.AddProvider(provider);
        LoadProviders();
    }

    private void SessionManagerButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSessionManagerWindow();
    }

    internal void OpenSessionManagerWindow()
    {
        if (_sessionWindow is null)
        {
            _sessionWindow = new SessionWindow
            {
                Owner = this
            };
            _sessionWindow.Closed += (_, _) => _sessionWindow = null;
            _sessionWindow.Show();
            return;
        }

        if (_sessionWindow.WindowState == WindowState.Minimized)
        {
            _sessionWindow.WindowState = WindowState.Normal;
        }

        if (!_sessionWindow.IsVisible)
        {
            _sessionWindow.Show();
        }

        _sessionWindow.Activate();
    }

    private void OpenConfigDirectoryButton_Click(object sender, RoutedEventArgs e)
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
            System.Windows.MessageBox.Show(
                this,
                $"打开目录失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void BaseUrlHyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: Provider provider })
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
            System.Windows.MessageBox.Show(
                this,
                $"打开链接失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ActivateProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
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
            System.Windows.MessageBox.Show(this, ex.Message, "启用失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async void TestProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not Provider provider)
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
                System.Windows.MessageBox.Show(
                    this,
                    $"响应时间：{result.ResponseTimeMs ?? 0} ms",
                    "测试成功",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                _databaseService.UpdateTestStatus(provider.Id, 2);
                LoadProviders();
                System.Windows.MessageBox.Show(
                    this,
                    result.Message,
                    "测试失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private void EditProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
        {
            return;
        }

        OpenEditProviderDialog(provider);
    }

    private void ProviderCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: Provider provider } card)
        {
            return;
        }

        if (IsClickFromInteractiveElement(e.OriginalSource as DependencyObject, card))
        {
            return;
        }

        OpenEditProviderDialog(provider);
        e.Handled = true;
    }

    private void OpenEditProviderDialog(Provider provider)
    {
        var dialog = new ProviderDialog(provider)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var updatedProvider = dialog.Provider;
        updatedProvider.TestStatus = 0;
        _databaseService.UpdateProvider(updatedProvider);
        LoadProviders();
    }

    private static bool IsClickFromInteractiveElement(DependencyObject? source, DependencyObject container)
    {
        var current = source;
        while (current is not null && current != container)
        {
            if (current is System.Windows.Controls.Button || current is Hyperlink)
            {
                return true;
            }

            current = GetParentObject(current);
        }

        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is FrameworkContentElement contentElement)
        {
            return contentElement.Parent;
        }

        if (child is Visual visual)
        {
            return VisualTreeHelper.GetParent(visual);
        }

        return null;
    }

    private void DeleteProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"确认删除供应商“{provider.Name}”吗？",
            "删除确认",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _databaseService.DeleteProvider(provider.Id);
        LoadProviders();
    }

    private void MoveProviderUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
        {
            return;
        }

        _databaseService.MoveProviderUp(provider.Id, _currentToolType);
        LoadProviders();
    }

    private void MoveProviderDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
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

        if (System.Windows.Application.Current is App app)
        {
            app.RefreshTrayTooltip();
        }
    }

    private int GetNextSortOrder()
    {
        var providers = _databaseService.GetProviders(_currentToolType);
        if (providers.Count == 0)
        {
            return 1;
        }

        return providers.Max(p => p.SortOrder) + 1;
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

        return Regex.Replace(
            normalized,
            @"/v1/?(?=($|[?#]))",
            string.Empty,
            RegexOptions.IgnoreCase);
    }

    private static void SetTabButtonSelectedState(System.Windows.Controls.Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
            button.Foreground = System.Windows.Media.Brushes.White;
            button.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1D4ED8"));
            button.BorderThickness = new System.Windows.Thickness(1);
            return;
        }

        button.Background = System.Windows.Media.Brushes.White;
        button.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111827"));
        button.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D1D5DB"));
        button.BorderThickness = new System.Windows.Thickness(1);
    }
}
