using System.IO;
using APISwitch.UI.Services;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace APISwitch.UI.Views;

public partial class SessionWindow : Window
{
    private readonly SessionService _sessionService = new();

    private string _currentProviderId = SessionService.ProviderCodex;
    private SessionMeta? _selectedSession;
    private int _reloadSessionsVersion;
    private int _loadMessagesVersion;
    private ListBox? _selectedGroupListBox;
    private bool _isSwitchingSelection;
    private List<SessionListItem> _allSessionItems = [];
    private int _allSubagentCount;
    private string _searchKeyword = string.Empty;

    public SessionWindow(string? initialProviderId = null)
    {
        _currentProviderId = SessionService.NormalizeProviderId(initialProviderId);
        InitializeComponent();

        UpdateTabButtons();
        _ = ReloadSessionsAsync();
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

    public async Task SelectProviderAsync(string providerId)
    {
        var targetProviderId = SessionService.NormalizeProviderId(providerId);
        if (string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = targetProviderId;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void CodexTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SessionService.IsCodex(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderCodex;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void ClaudeTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SessionService.IsClaude(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderClaude;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void GrokTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SessionService.IsGrok(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderGrok;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void SessionGroupListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is not SessionListItem item)
        {
            if (_isSwitchingSelection)
            {
                return;
            }

            if (_selectedGroupListBox is not null && !ReferenceEquals(_selectedGroupListBox, listBox))
            {
                return;
            }

            _selectedGroupListBox = null;
            _selectedSession = null;
            ResetDetailPanel();
            return;
        }

        if (_selectedGroupListBox is not null && !ReferenceEquals(_selectedGroupListBox, listBox))
        {
            _isSwitchingSelection = true;
            _selectedGroupListBox.SelectedItem = null;
            _isSwitchingSelection = false;
        }

        _selectedGroupListBox = listBox;

        _selectedSession = item.Session;
        SessionTitleTextBlock.Text = item.Title;
        UpdateSessionIdDisplay(_selectedSession.SessionId);
        SessionProjectPathButton.Content = _selectedSession.ProjectDir;
        SessionProjectPathButton.IsVisible = !string.IsNullOrWhiteSpace(_selectedSession.ProjectDir);
        SessionActionPanel.IsVisible = true;
        ShowMessagePlaceholder("加载中...");

        var currentVersion = ++_loadMessagesVersion;
        List<SessionMessage> messages;

        try
        {
            messages = await Task.Run(() => _sessionService.LoadMessages(_selectedSession.ProviderId, _selectedSession.SourcePath));
        }
        catch (Exception ex)
        {
            if (currentVersion != _loadMessagesVersion)
            {
                return;
            }

            await DialogService.ShowErrorAsync(this, "错误", $"加载会话失败：{ex.Message}");
            ShowMessagePlaceholder("加载失败");
            return;
        }

        if (currentVersion != _loadMessagesVersion)
        {
            return;
        }

        RenderMessages(messages);
    }

    private void SubagentToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton || toggleButton.DataContext is not SessionListItem item)
        {
            return;
        }

        var isExpanded = toggleButton.IsChecked == true;
        item.SetExpanded(isExpanded);
        if (!isExpanded &&
            _selectedGroupListBox?.SelectedItem is SessionListItem selectedItem &&
            item.ContainsDescendant(selectedItem))
        {
            _selectedGroupListBox.SelectedItem = item;
        }

        e.Handled = true;
    }

    private async void DeleteSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSession is null)
        {
            return;
        }

        var selectedSession = _selectedSession;
        var confirmationMessage = SessionService.IsCodex(selectedSession.ProviderId)
            ? $"确认删除会话“{selectedSession.Title}”及其所有子代理吗？"
            : $"确认删除会话“{selectedSession.Title}”吗？";
        var confirmed = await DialogService.ConfirmAsync(this, "删除确认", confirmationMessage);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await Task.Run(() => _sessionService.DeleteSession(
                selectedSession.ProviderId,
                selectedSession.SessionId,
                selectedSession.SourcePath));
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"删除会话失败：{ex.Message}");
            return;
        }

        _selectedSession = null;
        _loadMessagesVersion++;
        await ReloadSessionsAsync();
        ResetDetailPanel();
    }

    private async void ResumeSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSession is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSession.SessionId))
        {
            await DialogService.ShowErrorAsync(this, "错误", "当前会话没有可用的 Session ID，无法恢复。");
            return;
        }

        // 交互式 CLI 必须在独立终端里运行，否则用户看不到输入输出。
        var (command, workingDirectory) = SessionService.BuildResumeCommand(_selectedSession);
        var result = ShellLauncher.OpenTerminalCommand(command, workingDirectory);
        if (result.Status == OpenTerminalStatus.Ok)
        {
            return;
        }

        await DialogService.ShowErrorAsync(this, "错误", $"恢复会话失败：{result.ErrorMessage}");
    }

    private async void SessionProjectPathButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSession is null || string.IsNullOrWhiteSpace(_selectedSession.ProjectDir))
        {
            return;
        }

        await OpenDirectoryWithDialogAsync(_selectedSession.ProjectDir.Trim(), "目录不存在或无法访问", "打开目录失败");
    }

    private async void SessionIdTextBlock_Tapped(object? sender, TappedEventArgs e)
    {
        if (_selectedSession is null || string.IsNullOrWhiteSpace(_selectedSession.SourcePath))
        {
            return;
        }

        // 会话文件可能被删除或移动，只要父目录仍存在，就打开文件所在文件夹。
        var sessionFileDirectory = Path.GetDirectoryName(_selectedSession.SourcePath.Trim());
        await OpenDirectoryWithDialogAsync(sessionFileDirectory, "会话文件目录不存在或无法访问", "打开会话文件目录失败");
    }

    private async Task OpenDirectoryWithDialogAsync(string? directory, string notFoundMessage, string failurePrefix)
    {
        var result = ShellLauncher.OpenDirectory(directory);
        switch (result.Status)
        {
            case OpenDirectoryStatus.NotFound:
                await DialogService.ShowInfoAsync(this, "提示", notFoundMessage);
                break;
            case OpenDirectoryStatus.Failed:
                await DialogService.ShowErrorAsync(this, "错误", $"{failurePrefix}：{result.ErrorMessage}");
                break;
        }
    }

    private async Task ReloadSessionsAsync()
    {
        var reloadVersion = ++_reloadSessionsVersion;
        var targetProviderId = _currentProviderId;

        _selectedSession = null;
        _loadMessagesVersion++;
        _selectedGroupListBox = null;

        SessionGroupsItemsControl.ItemsSource = Array.Empty<SessionGroupItem>();
        _allSessionItems = [];
        _allSubagentCount = 0;
        SessionCountTextBlock.Text = "会话列表（加载中...）";
        SessionEmptyTextBlock.IsVisible = false;
        ResetDetailPanel();

        List<SessionListItem> items;
        int subagentCount;

        try
        {
            (items, subagentCount) = await Task.Run(() =>
            {
                var scannedSessions = SessionService.IsCodex(targetProviderId)
                    ? _sessionService.ScanCodexSessions()
                    : SessionService.IsClaude(targetProviderId)
                        ? _sessionService.ScanClaudeSessions()
                        : _sessionService.ScanGrokSessions();
                return SessionListBuilder.BuildItems(targetProviderId, scannedSessions);
            });
        }
        catch (Exception ex)
        {
            if (reloadVersion != _reloadSessionsVersion ||
                !string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await DialogService.ShowErrorAsync(this, "错误", $"扫描会话失败：{ex.Message}");
            items = new List<SessionListItem>();
            subagentCount = 0;
        }

        if (reloadVersion != _reloadSessionsVersion ||
            !string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _allSessionItems = items;
        _allSubagentCount = subagentCount;
        ApplySearchFilter();
    }

    private void SessionSearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
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
        ClearSelectedSessionForSearch();
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var isSearchActive = !string.IsNullOrWhiteSpace(_searchKeyword);
        var filteredItems = SessionListBuilder.FilterItems(_allSessionItems, _searchKeyword);
        var subagentCount = isSearchActive
            ? filteredItems.Count(item => item.Session.IsSubagent)
            : _allSubagentCount;

        SessionGroupsItemsControl.ItemsSource = BuildSessionGroups(filteredItems, isSearchActive);
        SessionCountTextBlock.Text = SessionListBuilder.BuildCountText(filteredItems.Count, subagentCount);
        SessionEmptyTextBlock.Text = isSearchActive ? "未找到匹配会话" : "暂无会话";
        SessionEmptyTextBlock.IsVisible = filteredItems.Count == 0;
    }

    private void ClearSelectedSessionForSearch()
    {
        _loadMessagesVersion++;
        _isSwitchingSelection = true;
        if (_selectedGroupListBox is not null)
        {
            _selectedGroupListBox.SelectedItem = null;
        }

        _isSwitchingSelection = false;
        _selectedGroupListBox = null;
        _selectedSession = null;
        ResetDetailPanel();
    }

    private void ResetDetailPanel()
    {
        SessionTitleTextBlock.Text = "请选择左侧会话";
        UpdateSessionIdDisplay(null);
        SessionProjectPathButton.Content = string.Empty;
        SessionProjectPathButton.IsVisible = false;
        SessionActionPanel.IsVisible = false;
        ShowMessagePlaceholder("选中会话后查看聊天详情");
    }

    private void UpdateSessionIdDisplay(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            SessionIdTextBlock.Text = string.Empty;
            SessionIdTextBlock.IsVisible = false;
            return;
        }

        SessionIdTextBlock.Text = sessionId;
        SessionIdTextBlock.IsVisible = true;
    }

    private void ShowMessagePlaceholder(string text)
    {
        MessagesPanel.Children.Clear();
        MessagesPanel.Children.Add(CreateSelectableTextElement(text, 13, CreateBrush("#9CA3AF"), textWrapping: TextWrapping.Wrap));
    }

    private void RenderMessages(IReadOnlyList<SessionMessage> messages)
    {
        MessagesPanel.Children.Clear();

        if (messages.Count == 0)
        {
            ShowMessagePlaceholder("暂无消息");
            return;
        }

        foreach (var view in SessionMessageViewBuilder.Build(_selectedSession?.ProviderId, messages))
        {
            MessagesPanel.Children.Add(view switch
            {
                BubbleMessageView bubble => CreateBubbleMessageElement(bubble),
                ErrorMessageView error => CreateErrorMessageElement(error),
                CollapsedMessageView collapsed => CreateCollapsedMessageElement(collapsed),
                _ => throw new NotSupportedException($"未知的消息展示类型:{view.GetType().Name}")
            });
        }
    }

    private static Control CreateBubbleMessageElement(BubbleMessageView view)
    {
        var isUser = view.IsUser;
        var bubble = new Border
        {
            Background = isUser ? CreateBrush("#2563EB") : CreateBrush("#F3F4F6"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 520
        };

        var container = new StackPanel
        {
            Spacing = 6
        };

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var roleText = CreateSelectableTextElement(
            view.Title,
            12,
            isUser ? Brushes.White : CreateBrush("#3B82F6"),
            FontWeight.SemiBold);
        header.Children.Add(roleText);

        AddTimestampToHeader(header, view, CreateBrush(isUser ? "#DBEAFE" : "#6B7280"), 1);

        container.Children.Add(header);

        foreach (var imageDataUrl in view.ImageDataUrls)
        {
            var imageElement = CreateImageElementFromDataUrl(imageDataUrl);
            if (imageElement is not null)
            {
                container.Children.Add(imageElement);
            }
        }

        if (view.HasContent)
        {
            container.Children.Add(CreateSelectableTextElement(
                view.Content,
                13,
                isUser ? Brushes.White : CreateBrush("#111827"),
                textWrapping: TextWrapping.Wrap));
        }

        bubble.Child = container;
        return bubble;
    }

    private static Control CreateErrorMessageElement(ErrorMessageView view)
    {
        var bubble = new Border
        {
            Background = CreateBrush("#FEF2F2"),
            BorderBrush = CreateBrush("#FCA5A5"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 520
        };

        var container = new StackPanel
        {
            Spacing = 6
        };

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(CreateSelectableTextElement(
            view.Title,
            12,
            CreateBrush("#B91C1C"),
            FontWeight.SemiBold));

        AddTimestampToHeader(header, view, CreateBrush("#B91C1C"), 1);

        container.Children.Add(header);
        container.Children.Add(CreateSelectableTextElement(
            view.Content,
            13,
            CreateBrush("#7F1D1D"),
            textWrapping: TextWrapping.Wrap));

        bubble.Child = container;
        return bubble;
    }

    private static Control CreateCollapsedMessageElement(CollapsedMessageView view)
    {
        var root = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 0
        };

        var collapsedChevron = Geometry.Parse("M 1,2 L 6,7 L 11,2");
        var expandedChevron = Geometry.Parse("M 1,7 L 6,2 L 11,7");
        var chevronPath = new global::Avalonia.Controls.Shapes.Path
        {
            Data = collapsedChevron,
            Stroke = CreateBrush("#6B7280"),
            StrokeThickness = 1,
            Width = 14,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.None
        };

        var chevronCircle = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = chevronPath,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(chevronCircle);

        var titleText = CreateSelectableTextElement(view.Title, 12, CreateBrush("#1E3A8A"), FontWeight.SemiBold);
        titleText.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        AddTimestampToHeader(header, view, CreateBrush("#6B7280"), 2, new Thickness(8, 0, 0, 0));

        var headerButton = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = header
        };

        var contentBorder = new Border
        {
            Background = CreateBrush("#EEF2FF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = CreateSelectableTextElement(view.Content, 12, CreateBrush("#1E3A8A"), textWrapping: TextWrapping.Wrap),
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var isExpanded = false;
        headerButton.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(headerButton).Properties.IsLeftButtonPressed)
            {
                return;
            }

            isExpanded = !isExpanded;
            contentBorder.IsVisible = isExpanded;
            chevronPath.Data = isExpanded ? expandedChevron : collapsedChevron;
        };

        root.Children.Add(headerButton);
        root.Children.Add(contentBorder);
        return root;
    }

    private static void AddTimestampToHeader(
        Grid header,
        SessionMessageView view,
        IBrush foreground,
        int column,
        Thickness? margin = null)
    {
        if (!view.HasTimestamp)
        {
            return;
        }

        var timeText = CreateSelectableTextElement(view.TimestampText, 12, foreground);
        if (margin.HasValue)
        {
            timeText.Margin = margin.Value;
        }

        Grid.SetColumn(timeText, column);
        header.Children.Add(timeText);
    }

    private static Control CreateSelectableTextElement(
        string text,
        double fontSize,
        IBrush foreground,
        FontWeight? fontWeight = null,
        TextWrapping textWrapping = TextWrapping.NoWrap)
    {
        var textBlock = new SelectableTextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            TextWrapping = textWrapping,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (fontWeight.HasValue)
        {
            textBlock.FontWeight = fontWeight.Value;
        }

        return textBlock;
    }

    private static Control? CreateImageElementFromDataUrl(string imageDataUrl)
    {
        var image = DecodeDataUrlImage(imageDataUrl);
        if (image is null)
        {
            return null;
        }

        var imageControl = new Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            MaxWidth = 420,
            MaxHeight = 320,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(imageControl, "双击查看原图");

        imageControl.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            ShowOriginalImageWindow(image, imageControl);
            e.Handled = true;
        };

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = imageControl
        };
    }

    private static void ShowOriginalImageWindow(Bitmap image, Control sourceElement)
    {
        var previewImage = new Image
        {
            Source = image,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var scrollViewer = new ScrollViewer
        {
            Content = previewImage,
            Background = CreateBrush("#111827"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var windowSize = GetOriginalImageWindowSize(image);
        var owner = TopLevel.GetTopLevel(sourceElement) as Window;
        var window = new Window
        {
            Title = "图片预览",
            Width = windowSize.Width,
            Height = windowSize.Height,
            MinWidth = 320,
            MinHeight = 240,
            Content = scrollViewer,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

        if (owner is null)
        {
            window.Show();
            return;
        }

        window.Show(owner);
    }

    private static Size GetOriginalImageWindowSize(Bitmap image)
    {
        var windowWidth = Math.Clamp(image.PixelSize.Width + 40, 320, 1200);
        var windowHeight = Math.Clamp(image.PixelSize.Height + 70, 240, 800);
        return new Size(windowWidth, windowHeight);
    }

    private static Bitmap? DecodeDataUrlImage(string imageDataUrl)
    {
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return null;
        }

        var commaIndex = imageDataUrl.IndexOf(',');
        if (commaIndex <= 0 || commaIndex >= imageDataUrl.Length - 1)
        {
            return null;
        }

        var prefix = imageDataUrl[..commaIndex];
        if (!prefix.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var base64Part = imageDataUrl[(commaIndex + 1)..];
        try
        {
            var bytes = Convert.FromBase64String(base64Part);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<SessionGroupItem> BuildSessionGroups(
        IReadOnlyList<SessionListItem> items,
        bool isExpanded)
    {
        return items
            .GroupBy(item => item.ProjectGroupName)
            .Select(group => new SessionGroupItem(group.Key, group.ToList())
            {
                IsExpanded = isExpanded
            })
            .ToList();
    }

    private void UpdateTabButtons()
    {
        SetTabButtonSelectedState(CodexTabButton, SessionService.IsCodex(_currentProviderId));
        SetTabButtonSelectedState(ClaudeTabButton, SessionService.IsClaude(_currentProviderId));
        SetTabButtonSelectedState(GrokTabButton, SessionService.IsGrok(_currentProviderId));
    }

    private static void SetTabButtonSelectedState(Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Background = CreateBrush("#2563EB");
            button.Foreground = Brushes.White;
            button.BorderBrush = CreateBrush("#1D4ED8");
            button.BorderThickness = new Thickness(1);
            return;
        }

        button.Background = Brushes.White;
        button.Foreground = CreateBrush("#111827");
        button.BorderBrush = CreateBrush("#D1D5DB");
        button.BorderThickness = new Thickness(1);
    }

    private static IBrush CreateBrush(string hexColor)
    {
        return new SolidColorBrush(Color.Parse(hexColor));
    }

    private sealed class SessionGroupItem
    {
        public SessionGroupItem(string groupName, List<SessionListItem> items)
        {
            GroupName = groupName;
            Items = items;
        }

        public string GroupName { get; }

        public List<SessionListItem> Items { get; }

        public bool IsExpanded { get; set; }
    }

}
