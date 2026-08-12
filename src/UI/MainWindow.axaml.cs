using System.Diagnostics;
using System.Text.RegularExpressions;
using APISwitch.Extensions;
using APISwitch.UI.Services;
using APISwitch.UI.Views;
using APISwitch.Models;
using APISwitch.Services;
using APISwitch.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace APISwitch.UI;

public partial class MainWindow : Window
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigWriterService _configWriterService;
    private readonly AppSettingsService _appSettingsService;
    private readonly ApiTestService _apiTestService;
    private readonly TranslateTransform _providerDropIndicatorTransform;
    private readonly DispatcherTimer _providerAutoScrollTimer;
    private readonly ProviderDragController _providerDragController;
    private bool _initialProvidersLoaded;

    private SessionWindow? _sessionWindow;
    private int _currentToolType;
    private List<Provider> _allProviders = [];
    private string _searchKeyword = string.Empty;
    private AppSettings _cachedSettings;

    private static readonly DataFormat<string> ProviderDragFormat =
        DataFormat.CreateStringApplicationFormat("APISwitch.Provider");

    public MainWindow(DatabaseService databaseService, ConfigWriterService configWriterService, AppSettingsService appSettingsService)
    {
        _databaseService = databaseService;
        _configWriterService = configWriterService;
        _appSettingsService = appSettingsService;
        _apiTestService = new ApiTestService(_appSettingsService);
        _cachedSettings = _appSettingsService.Load();

        _currentToolType = 0;
        InitializeComponent();

        _providerDropIndicatorTransform = new TranslateTransform();
        ProviderDropInsertionIndicator.RenderTransform = _providerDropIndicatorTransform;

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

        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, MainWindow_DragOver);
        DragDrop.AddDragLeaveHandler(this, MainWindow_DragLeave);
        DragDrop.AddDropHandler(this, MainWindow_Drop);

        UpdateTabButtons();
        Opened += MainWindow_Opened;
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_initialProvidersLoaded)
        {
            return;
        }

        _initialProvidersLoaded = true;
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

    private void GrokTabButton_Click(object? sender, RoutedEventArgs e)
    {
        _currentToolType = 2;
        UpdateTabButtons();
        LoadProviders();
    }

    private async void AddProviderButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ProviderDialog(_currentToolType, _appSettingsService);
        var provider = await dialog.ShowDialog<Provider?>(this);
        if (provider is null)
        {
            return;
        }

        provider.SortOrder = GetNextSortOrder();
        _databaseService.AddProvider(provider);
        LoadProviders();
    }

    private async void PromptButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = new PromptWindow(_databaseService);
        await window.ShowDialog(this);
    }

    private void SessionManagerButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenSessionManagerWindow();
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_appSettingsService);
        await dialog.ShowDialog<bool>(this);
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
                await DialogService.ShowInfoAsync(this, "测试成功", $"供应商：{provider.Name}\n响应时间：{result.ResponseTimeMs ?? 0} ms");
                return;
            }

            _databaseService.UpdateTestStatus(provider.Id, 2);
            LoadProviders();
            await DialogService.ShowErrorAsync(this, "测试失败", $"供应商：{provider.Name}\n{result.Message}");
        }
        catch (Exception ex)
        {
            _databaseService.UpdateTestStatus(provider.Id, 2);
            LoadProviders();
            await DialogService.ShowErrorAsync(this, "测试失败", $"供应商：{provider.Name}\n测试过程异常：{ex.Message}");
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
        var dialog = new ProviderDialog(provider, _appSettingsService);
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

    private IReadOnlyList<Provider> LoadedProviders =>
        ProvidersItemsControl.ItemsSource as IReadOnlyList<Provider>
        ?? throw new InvalidOperationException("供应商列表的数据源必须实现 IReadOnlyList<Provider>。");

    private void ProviderDragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: Provider } handle ||
            !e.Pointer.IsPrimary ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var startPoint = e.GetPosition(ProvidersItemsControl);
        _providerDragController.BeginHandlePress(startPoint.X, startPoint.Y);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private async void ProviderDragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control { DataContext: Provider provider } handle ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
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
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(ProviderDragFormat, provider.Id.ToString()));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "拖拽失败", $"调整供应商顺序失败：{ex.Message}");
        }
        finally
        {
            e.Pointer.Capture(null);
            EndProviderDrag();
        }
    }

    private void ProviderDragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _providerDragController.ReleaseHandle();
        e.Pointer.Capture(null);
    }

    private void ProviderDragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _providerDragController.ReleaseHandle();
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        if (!TryGetDraggedProviderId(e.DataTransfer, out var providerId) ||
            !_providerDragController.IsDraggingProvider(providerId))
        {
            return;
        }

        UpdateProviderDragTarget(e);
        // 自有拖拽在布局瞬间更新时仍保持 Move，避免原生拖放被临时判定为无效。
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void MainWindow_DragLeave(object? sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_providerDragController.HasActiveDrag &&
            (position.X < 0 || position.X > Bounds.Width || position.Y < 0 || position.Y > Bounds.Height))
        {
            UpdateProviderDragTarget(e);
            e.Handled = true;
        }
    }

    private void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        if (!TryGetDraggedProviderId(e.DataTransfer, out var providerId) ||
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

        e.DragEffects = hasTarget ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        EndProviderDrag();
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

    private static bool TryGetDraggedProviderId(IDataTransfer data, out int providerId)
    {
        return int.TryParse(data.TryGetValue(ProviderDragFormat), out providerId) && providerId > 0;
    }

    private ProviderCardGeometry? GetProviderCardGeometry(int index)
    {
        var container = ProvidersItemsControl.ContainerFromIndex(index);
        var card = container?
            .GetSelfAndVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(item => item.Name == "ProviderCard");
        var cardTop = card?.TranslatePoint(new Point(0, 0), ProvidersItemsControl);
        if (card is null || cardTop is null)
        {
            return null;
        }

        return new ProviderCardGeometry(cardTop.Value.Y, card.Bounds.Height);
    }

    private ProviderDragScrollState GetProviderDragScrollState()
    {
        var offset = ProvidersScrollViewer.Offset;
        var maxOffsetY = Math.Max(
            0,
            ProvidersScrollViewer.Extent.Height - ProvidersScrollViewer.Viewport.Height);
        return new ProviderDragScrollState(
            ProvidersScrollViewer.Bounds.Width,
            ProvidersScrollViewer.Bounds.Height,
            offset.Y,
            maxOffsetY,
            ProviderDropInsertionIndicator.Height);
    }

    private void SetProviderDropIndicator(double? offset)
    {
        if (offset is not double indicatorOffset)
        {
            ProviderDropInsertionIndicator.IsVisible = false;
            return;
        }

        _providerDropIndicatorTransform.Y = indicatorOffset;
        ProviderDropInsertionIndicator.IsVisible = true;
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

        _providerDragController.CompleteAutoScrollTick(LoadedProviders);
    }

    private void EndProviderDrag()
    {
        _providerDragController.EndDrag();
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

        if (global::Avalonia.Application.Current is App app)
        {
            app.RefreshTrayTooltip(_databaseService);
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

    private void SearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
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

    private async void ProviderCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Control { DataContext: Provider provider } card)
        {
            return;
        }

        if (IsClickFromInteractiveElement(e.Source as StyledElement, card))
        {
            return;
        }

        await OpenEditProviderDialogAsync(provider);
        e.Handled = true;
    }

    private static bool IsClickFromInteractiveElement(StyledElement? source, StyledElement container)
    {
        var current = source;
        while (current is not null && current != container)
        {
            if (current is Button)
            {
                return true;
            }

            current = current.Parent as StyledElement;
        }

        return false;
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
        return SessionService.GetProviderIdForToolType(_currentToolType);
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
