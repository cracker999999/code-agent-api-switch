using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private int _currentToolType;

    public MainWindow(DatabaseService databaseService, ConfigWriterService configWriterService)
    {
        _databaseService = databaseService;
        _configWriterService = configWriterService;
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

    private void EditProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider })
        {
            return;
        }

        var dialog = new ProviderDialog(provider)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _databaseService.UpdateProvider(dialog.Provider);
        LoadProviders();
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

    private void LoadProviders()
    {
        ProvidersItemsControl.ItemsSource = _databaseService.GetProviders(_currentToolType);
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
