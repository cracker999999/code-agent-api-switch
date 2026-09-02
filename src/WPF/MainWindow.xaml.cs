using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using APISwitch.Extensions;
using APISwitch.Models;
using APISwitch.Services;
using APISwitch.Utilities;
using WPF.Services;
using WPF.Views;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using DragEventHandler = System.Windows.DragEventHandler;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace WPF;

public partial class MainWindow : Window
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigWriterService _configWriterService;
    private readonly AppSettingsService _appSettingsService;
    private readonly ApiTestService _apiTestService;
    private SessionWindow? _sessionWindow;
    private int _currentToolType;
    private readonly DispatcherTimer _providerAutoScrollTimer;
    private readonly ProviderDragController _providerDragController;
    private List<Provider> _allProviders = [];
    private string _searchKeyword = string.Empty;
    private AppSettings _cachedSettings;

    private const string ProviderDragFormat = "APISwitch.Provider";

    public MainWindow(DatabaseService databaseService, ConfigWriterService configWriterService, AppSettingsService appSettingsService)
    {
        _databaseService = databaseService;
        _configWriterService = configWriterService;
        _appSettingsService = appSettingsService;
        _apiTestService = new ApiTestService(_appSettingsService);
        _cachedSettings = _appSettingsService.Load();
        _currentToolType = 0;

        InitializeComponent();

        _providerAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _providerAutoScrollTimer.Tick += ProviderAutoScrollTimer_Tick;

        _providerDragController = new ProviderDragController(
            GetProviderCardGeometry,
            GetProviderDragScrollState,
            SetProviderDropIndicator);
        _providerDragController.AutoScrollActiveChanged += SetProviderAutoScrollActive;

        // 即使子控件先将拖放事件标记为已处理，窗口仍需接收最终 Drop。
        AddHandler(DragDrop.DragOverEvent, new DragEventHandler(MainWindow_DragOver), true);
        AddHandler(DragDrop.DragLeaveEvent, new DragEventHandler(MainWindow_DragLeave), true);
        AddHandler(DragDrop.DropEvent, new DragEventHandler(MainWindow_Drop), true);

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

    private void GrokTabButton_Click(object sender, RoutedEventArgs e)
    {
        _currentToolType = 2;
        UpdateTabButtons();
        LoadProviders();
    }

    private void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProviderDialog(_currentToolType, _appSettingsService)
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

    private void PromptButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PromptWindow(_databaseService)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void SessionManagerButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSessionManagerWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_appSettingsService)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    internal void OpenSessionManagerWindow()
    {
        var providerId = GetCurrentProviderId();
        if (_sessionWindow is null)
        {
            _sessionWindow = new SessionWindow(providerId)
            {
                Owner = this
            };
            _sessionWindow.Closed += (_, _) => _sessionWindow = null;
            _sessionWindow.Show();
            return;
        }

        _ = _sessionWindow.SelectProviderAsync(providerId);

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

    private string GetCurrentProviderId()
    {
        return SessionService.GetProviderIdForToolType(_currentToolType);
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
                DialogService.ShowInfo(this, "测试成功", $"供应商：{provider.Name}\n响应时间：{result.ResponseTimeMs ?? 0} ms");
            }
            else
            {
                _databaseService.UpdateTestStatus(provider.Id, 2);
                LoadProviders();
                DialogService.ShowError(this, "测试失败", $"供应商：{provider.Name}\n{result.Message}");
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
        var dialog = new ProviderDialog(provider, _appSettingsService)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var updatedProvider = dialog.Provider;
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

    private IReadOnlyList<Provider> LoadedProviders =>
        ProvidersItemsControl.ItemsSource as IReadOnlyList<Provider>
        ?? throw new InvalidOperationException("供应商列表的数据源必须实现 IReadOnlyList<Provider>。");

    private void ProviderDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider } handle ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var startPoint = e.GetPosition(ProvidersItemsControl);
        _providerDragController.BeginHandlePress(startPoint.X, startPoint.Y);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void ProviderDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Provider provider } handle ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(ProvidersItemsControl);
        if (!_providerDragController.TryStartDrag(provider, currentPoint.X, currentPoint.Y))
        {
            return;
        }

        e.Handled = true;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(ProviderDragFormat, provider.Id.ToString(), false);
            DragDrop.DoDragDrop(handle, data, DragDropEffects.Move);
        }
        finally
        {
            if (handle.IsMouseCaptured)
            {
                handle.ReleaseMouseCapture();
            }

            _providerDragController.EndDrag();
        }
    }

    private void ProviderDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _providerDragController.ReleaseHandle();
        if (sender is UIElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }
    }

    private void ProviderDragHandle_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _providerDragController.ReleaseHandle();
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedProviderId(e.Data, out var providerId) ||
            !_providerDragController.IsDraggingProvider(providerId))
        {
            return;
        }

        UpdateProviderDragTarget(e);
        // 自有拖拽在布局瞬间更新时仍保持 Move，避免 OLE 拖放被临时判定为无效。
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void MainWindow_DragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_providerDragController.HasActiveDrag &&
            (position.X < 0 || position.X > ActualWidth || position.Y < 0 || position.Y > ActualHeight))
        {
            UpdateProviderDragTarget(e);
            e.Handled = true;
        }
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedProviderId(e.Data, out var providerId) ||
            !_providerDragController.IsDraggingProvider(providerId))
        {
            return;
        }

        UpdateProviderDragTarget(e);
        var hasTarget = _providerDragController.HasPendingTarget;
        if (hasTarget && _providerDragController.GetPendingMove(LoadedProviders) is { } move)
        {
            _databaseService.MoveProviderToIndex(move.ProviderId, move.ToolType, move.DestinationIndex);
            LoadProviders();
        }

        e.Effects = hasTarget ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        _providerDragController.EndDrag();
    }

    private void UpdateProviderDragTarget(DragEventArgs e)
    {
        var viewportPosition = e.GetPosition(ProvidersScrollViewer);
        var contentPosition = e.GetPosition(ProvidersItemsControl);
        _providerDragController.UpdateTarget(
            LoadedProviders,
            viewportPosition.X,
            viewportPosition.Y,
            contentPosition.Y);
    }

    private static bool TryGetDraggedProviderId(System.Windows.IDataObject data, out int providerId)
    {
        providerId = 0;
        return data.GetDataPresent(ProviderDragFormat, false) &&
               data.GetData(ProviderDragFormat, false) is string value &&
               int.TryParse(value, out providerId) &&
               providerId > 0;
    }

    private ProviderCardGeometry? GetProviderCardGeometry(int index)
    {
        if (ProvidersItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter container)
        {
            return null;
        }

        var template = container.ContentTemplate ?? ProvidersItemsControl.ItemTemplate;
        if (template?.FindName("ProviderCard", container) is not Border card)
        {
            return null;
        }

        var top = card.TranslatePoint(new Point(0, 0), ProvidersItemsControl).Y;
        return new ProviderCardGeometry(top, card.ActualHeight);
    }

    private ProviderDragScrollState GetProviderDragScrollState()
    {
        return new ProviderDragScrollState(
            ProvidersScrollViewer.ActualWidth,
            ProvidersScrollViewer.ActualHeight,
            ProvidersScrollViewer.VerticalOffset,
            ProvidersScrollViewer.ScrollableHeight,
            ProviderDropInsertionIndicator.Height);
    }

    private void SetProviderDropIndicator(double? offset)
    {
        if (offset is not double indicatorOffset)
        {
            ProviderDropInsertionIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        ProviderDropIndicatorTransform.Y = indicatorOffset;
        ProviderDropInsertionIndicator.Visibility = Visibility.Visible;
    }

    private void SetProviderAutoScrollActive(bool isActive)
    {
        if (isActive)
        {
            _providerAutoScrollTimer.Start();
        }
        else
        {
            _providerAutoScrollTimer.Stop();
        }
    }

    private void ProviderAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_providerDragController.TryGetAutoScrollDirection(out var direction))
        {
            _providerAutoScrollTimer.Stop();
            return;
        }

        if (direction < 0)
        {
            ProvidersScrollViewer.LineUp();
        }
        else
        {
            ProvidersScrollViewer.LineDown();
        }

        // LineUp/LineDown 在后续布局阶段才更新 VerticalOffset，延后到布局完成后再换算插入槽位。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, CompleteProviderAutoScroll);
    }

    private void CompleteProviderAutoScroll()
    {
        if (_providerDragController.HasActiveDrag)
        {
            _providerDragController.CompleteAutoScrollTick(LoadedProviders);
        }
    }

    private void LoadProviders()
    {
        _providerDragController.ClearGeometry();
        _allProviders = _databaseService.GetProviders(_currentToolType);
        for (var index = 0; index < _allProviders.Count; index++)
        {
            _allProviders[index].CanMoveUp = index > 0;
            _allProviders[index].CanMoveDown = index < _allProviders.Count - 1;
            _allProviders[index].TestModelDisplay = _allProviders[index].GetEffectiveTestModel(_cachedSettings);
        }

        ApplySearchFilter();

        if (System.Windows.Application.Current is App app)
        {
            app.RefreshTrayTooltip();
        }
    }

    private void ApplySearchFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchKeyword))
        {
            ProvidersItemsControl.ItemsSource = _allProviders;
            return;
        }

        var keyword = _searchKeyword.Trim();
        var filtered = _allProviders.Where(p =>
            p.MatchesSearchKeyword(keyword) ||
            (p.GetEffectiveTestModel(_cachedSettings)?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();

        ProvidersItemsControl.ItemsSource = filtered;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var newKeyword = textBox.Text ?? string.Empty;
        if (newKeyword == _searchKeyword)
        {
            return;
        }

        _searchKeyword = newKeyword;
        ApplySearchFilter();
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
        SetTabButtonSelectedState(GrokTabButton, _currentToolType == 2);
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
