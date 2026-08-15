using System.IO;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using APISwitch.Models;
using APISwitch.Services;
using WPF.Services;
using Media = System.Windows.Media;

namespace WPF.Views;

public partial class SessionWindow : Window
{
    private readonly SessionService _sessionService = new();
    private string _currentProviderId = SessionService.ProviderCodex;
    private SessionMeta? _selectedSession;
    private int _loadMessagesVersion;
    private double _sessionListWheelStepRemainder;
    private double _messagesWheelStepRemainder;
    private List<SessionListItem> _allSessionItems = [];
    private int _allSubagentCount;
    private string _searchKeyword = string.Empty;

    public bool IsSessionSearchActive => !string.IsNullOrWhiteSpace(_searchKeyword);

    public SessionWindow(string? initialProviderId = null)
    {
        _currentProviderId = NormalizeProviderId(initialProviderId);
        InitializeComponent();
        UpdateTabButtons();
        _ = ReloadSessionsAsync();
    }

    public async Task SelectProviderAsync(string providerId)
    {
        var targetProviderId = NormalizeProviderId(providerId);
        if (string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = targetProviderId;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void CodexTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsCodex(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderCodex;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void ClaudeTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsClaude(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderClaude;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void GrokTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsGrok(_currentProviderId))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderGrok;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void SessionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionListBox.SelectedItem is not SessionListItem item)
        {
            _selectedSession = null;
            ResetDetailPanel();
            return;
        }

        _selectedSession = item.Session;
        SessionTitleTextBlock.Text = item.Title;
        UpdateSessionIdDisplay(_selectedSession.SessionId);
        UpdateProjectPathDisplay(_selectedSession.ProjectDir);
        SessionActionPanel.Visibility = Visibility.Visible;
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

            System.Windows.MessageBox.Show(
                this,
                $"加载会话失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            ShowMessagePlaceholder("加载失败");
            return;
        }

        if (currentVersion != _loadMessagesVersion)
        {
            return;
        }

        RenderMessages(messages);
    }

    private void SubagentToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton || toggleButton.DataContext is not SessionListItem item)
        {
            return;
        }

        var isExpanded = toggleButton.IsChecked == true;
        item.SetExpanded(isExpanded);
        if (!isExpanded &&
            SessionListBox.SelectedItem is SessionListItem selectedItem &&
            item.ContainsDescendant(selectedItem))
        {
            SessionListBox.SelectedItem = item;
        }

        e.Handled = true;
    }

    private void SessionListBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var scrollViewer = FindDescendantScrollViewer(SessionListBox);
        ApplyScaledWheel(scrollViewer, e, 0.5, ref _sessionListWheelStepRemainder);
    }

    private void MessagesScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        ApplyScaledWheel(MessagesScrollViewer, e, 2.0, ref _messagesWheelStepRemainder);
    }

    private async void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSession is null)
        {
            return;
        }

        var selectedSession = _selectedSession;
        var confirmationMessage = SessionService.IsCodex(selectedSession.ProviderId)
            ? $"确认删除会话“{selectedSession.Title}”及其所有子代理吗？"
            : $"确认删除会话“{selectedSession.Title}”吗？";
        var result = System.Windows.MessageBox.Show(
            this,
            confirmationMessage,
            "删除确认",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
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
            System.Windows.MessageBox.Show(
                this,
                $"删除会话失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }

        _selectedSession = null;
        _loadMessagesVersion++;
        await ReloadSessionsAsync();
        ResetDetailPanel();
    }

    private void ResumeSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSession is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSession.SessionId))
        {
            DialogService.ShowError(this, "错误", "当前会话没有可用的 Session ID，无法恢复。");
            return;
        }

        // 交互式 CLI 必须在独立终端里运行，否则用户看不到输入输出。
        var (command, workingDirectory) = SessionService.BuildResumeCommand(_selectedSession);
        var result = ShellLauncher.OpenTerminalCommand(command, workingDirectory);
        if (result.Status == OpenTerminalStatus.Ok)
        {
            return;
        }

        DialogService.ShowError(this, "错误", $"恢复会话失败：{result.ErrorMessage}");
    }

    private async Task ReloadSessionsAsync()
    {
        var targetProviderId = _currentProviderId;
        _selectedSession = null;
        _loadMessagesVersion++;
        SessionListBox.SelectedItem = null;
        SessionListBox.ItemsSource = null;
        _allSessionItems = [];
        _allSubagentCount = 0;
        SessionCountTextBlock.Text = "会话（加载中...）";
        SessionEmptyTextBlock.Visibility = Visibility.Collapsed;
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
            System.Windows.MessageBox.Show(
                this,
                $"扫描会话失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            items = new List<SessionListItem>();
            subagentCount = 0;
        }

        _allSessionItems = items;
        _allSubagentCount = subagentCount;
        ApplySearchFilter();
    }

    private void SessionSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
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
        _selectedSession = null;
        _loadMessagesVersion++;
        SessionListBox.SelectedItem = null;
        ResetDetailPanel();
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var isSearchActive = IsSessionSearchActive;
        var filteredItems = SessionListBuilder.FilterItems(_allSessionItems, _searchKeyword);
        var subagentCount = isSearchActive
            ? filteredItems.Count(item => item.Session.IsSubagent)
            : _allSubagentCount;

        var groupedView = CollectionViewSource.GetDefaultView(filteredItems);
        groupedView.GroupDescriptions.Clear();
        groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SessionListItem.ProjectGroupName)));

        SessionListBox.ItemsSource = groupedView;
        SessionCountTextBlock.Text = SessionListBuilder.BuildCountText(filteredItems.Count, subagentCount);
        SessionEmptyTextBlock.Text = isSearchActive ? "未找到匹配会话" : "暂无会话";
        SessionEmptyTextBlock.Visibility = filteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetDetailPanel()
    {
        SessionTitleTextBlock.Text = "请选择左侧会话";
        UpdateSessionIdDisplay(null);
        SessionProjectPathTextBlock.Text = string.Empty;
        SessionProjectPathTextBlock.ToolTip = null;
        SessionProjectPathTextBlock.Visibility = Visibility.Collapsed;
        SessionActionPanel.Visibility = Visibility.Collapsed;
        ShowMessagePlaceholder("选中会话后查看聊天详情");
    }

    private void UpdateSessionIdDisplay(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            SessionIdTextBlock.Text = string.Empty;
            SessionIdTextBlock.ToolTip = null;
            SessionIdTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        SessionIdTextBlock.Text = sessionId;
        SessionIdTextBlock.ToolTip = sessionId;
        SessionIdTextBlock.Visibility = Visibility.Visible;
    }

    private void SessionIdTextBlock_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selectedSession is null || string.IsNullOrWhiteSpace(_selectedSession.SourcePath))
        {
            return;
        }

        // 会话文件可能被删除或移动，只要父目录仍存在，就打开文件所在文件夹。
        var sessionFileDirectory = Path.GetDirectoryName(_selectedSession.SourcePath.Trim());
        OpenDirectoryWithDialog(sessionFileDirectory, "会话文件目录不存在或无法访问", "打开会话文件目录失败");
    }

    private void OpenDirectoryWithDialog(string? directory, string notFoundMessage, string failurePrefix)
    {
        var result = ShellLauncher.OpenDirectory(directory);
        switch (result.Status)
        {
            case OpenDirectoryStatus.NotFound:
                System.Windows.MessageBox.Show(
                    this,
                    notFoundMessage,
                    "提示",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                break;
            case OpenDirectoryStatus.Failed:
                System.Windows.MessageBox.Show(
                    this,
                    $"{failurePrefix}：{result.ErrorMessage}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                break;
        }
    }

    private void UpdateProjectPathDisplay(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            SessionProjectPathTextBlock.Text = string.Empty;
            SessionProjectPathTextBlock.ToolTip = null;
            SessionProjectPathTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        SessionProjectPathTextBlock.Text = projectDir;
        SessionProjectPathTextBlock.ToolTip = projectDir;
        SessionProjectPathTextBlock.Visibility = Visibility.Visible;
    }

    private void SessionProjectPathTextBlock_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selectedSession is null || string.IsNullOrWhiteSpace(_selectedSession.ProjectDir))
        {
            return;
        }

        OpenDirectoryWithDialog(_selectedSession.ProjectDir.Trim(), "目录不存在或无法访问", "打开目录失败");
    }

    private void ShowMessagePlaceholder(string text)
    {
        MessagesPanel.Children.Clear();
        var placeholder = CreateSelectableTextElement(
            text,
            13,
            CreateBrush("#9CA3AF"),
            textWrapping: TextWrapping.Wrap);
        placeholder.Margin = new Thickness(0, 10, 0, 0);
        MessagesPanel.Children.Add(placeholder);
    }

    private void RenderMessages(IReadOnlyList<SessionMessage> messages)
    {
        MessagesPanel.Children.Clear();

        if (messages.Count == 0)
        {
            ShowMessagePlaceholder("暂无消息");
            return;
        }

        var isCodexSession = _selectedSession is not null &&
            (SessionService.IsCodex(_selectedSession.ProviderId) || SessionService.IsGrok(_selectedSession.ProviderId));

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                if (message.ImageDataUrls.Count > 0)
                {
                    MessagesPanel.Children.Add(CreateBubbleMessageElement(
                        message.Content,
                        false,
                        SessionService.GetRoleDisplayName(message.Role),
                        message.Timestamp,
                        message.ImageDataUrls));
                    continue;
                }

                MessagesPanel.Children.Add(CreateToolMessageElement(message.Content, message.Timestamp));
                continue;
            }

            if (string.Equals(message.Role, "error", StringComparison.OrdinalIgnoreCase))
            {
                MessagesPanel.Children.Add(CreateErrorMessageElement(message.Content, message.Timestamp));
                continue;
            }

            if (isCodexSession &&
                index == 0 &&
                string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase))
            {
                MessagesPanel.Children.Add(CreateDeveloperMessageElement(message.Content, message.Timestamp));
                continue;
            }

            var isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
            MessagesPanel.Children.Add(CreateBubbleMessageElement(
                message.Content,
                isUser,
                SessionService.GetRoleDisplayName(message.Role),
                message.Timestamp,
                message.ImageDataUrls));
        }

        MessagesScrollViewer.ScrollToHome();
    }

    private static FrameworkElement CreateBubbleMessageElement(
        string content,
        bool isUser,
        string roleDisplayName,
        DateTime? timestamp,
        IReadOnlyList<string> imageDataUrls)
    {
        var bubble = new Border
        {
            Background = isUser
                ? CreateBrush("#2563EB")
                : CreateBrush("#F3F4F6"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = isUser ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left,
            MaxWidth = 520
        };

        var container = new StackPanel();
        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var roleText = CreateSelectableTextElement(
            roleDisplayName,
            12,
            isUser ? Media.Brushes.White : CreateBrush("#3B82F6"),
            FontWeights.SemiBold);
        header.Children.Add(roleText);

        AddTimestampToHeader(header, timestamp, CreateBrush(isUser ? "#DBEAFE" : "#6B7280"));

        container.Children.Add(header);

        foreach (var imageDataUrl in imageDataUrls)
        {
            var imageElement = CreateImageElementFromDataUrl(imageDataUrl);
            if (imageElement is null)
            {
                continue;
            }

            container.Children.Add(imageElement);
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            container.Children.Add(CreateSelectableTextElement(
                content,
                13,
                isUser ? Media.Brushes.White : CreateBrush("#111827"),
                textWrapping: TextWrapping.Wrap));
        }

        bubble.Child = container;

        return bubble;
    }

    private static FrameworkElement CreateToolMessageElement(string content, DateTime? timestamp)
    {
        return CreateCollapsedMessageElement("工具", content, timestamp);
    }

    private static FrameworkElement CreateErrorMessageElement(string content, DateTime? timestamp)
    {
        var bubble = new Border
        {
            Background = CreateBrush("#FEF2F2"),
            BorderBrush = CreateBrush("#FCA5A5"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            MaxWidth = 520
        };

        var container = new StackPanel();
        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(CreateSelectableTextElement(
            "错误",
            12,
            CreateBrush("#B91C1C"),
            FontWeights.SemiBold));

        AddTimestampToHeader(header, timestamp, CreateBrush("#B91C1C"));

        container.Children.Add(header);
        container.Children.Add(CreateSelectableTextElement(
            content,
            13,
            CreateBrush("#7F1D1D"),
            textWrapping: TextWrapping.Wrap));

        bubble.Child = container;
        return bubble;
    }

    private static FrameworkElement CreateDeveloperMessageElement(string content, DateTime? timestamp)
    {
        return CreateCollapsedMessageElement("developer", content, timestamp);
    }

    private static FrameworkElement CreateCollapsedMessageElement(string title, string content, DateTime? timestamp)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(CreateSelectableTextElement(
            title,
            12,
            CreateBrush("#1E3A8A"),
            FontWeights.SemiBold));

        AddTimestampToHeader(header, timestamp, CreateBrush("#6B7280"));

        var expander = new Expander
        {
            Header = header,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };

        expander.Content = new Border
        {
            Background = CreateBrush("#EEF2FF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = CreateSelectableTextElement(
                content,
                12,
                CreateBrush("#1E3A8A"),
                textWrapping: TextWrapping.Wrap)
        };

        return expander;
    }

    private static void AddTimestampToHeader(Grid header, DateTime? timestamp, Media.Brush foreground)
    {
        if (!timestamp.HasValue)
        {
            return;
        }

        var timestampText = CreateSelectableTextElement(FormatMessageTime(timestamp.Value), 12, foreground);
        Grid.SetColumn(timestampText, 1);
        header.Children.Add(timestampText);
    }

    private static System.Windows.Controls.TextBox CreateSelectableTextElement(
        string text,
        double fontSize,
        Media.Brush foreground,
        FontWeight? fontWeight = null,
        TextWrapping textWrapping = TextWrapping.NoWrap)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            Background = Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsReadOnly = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            TextWrapping = textWrapping,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (fontWeight.HasValue)
        {
            textBox.FontWeight = fontWeight.Value;
        }

        return textBox;
    }

    private static FrameworkElement? CreateImageElementFromDataUrl(string imageDataUrl)
    {
        var imageSource = DecodeDataUrlImage(imageDataUrl);
        if (imageSource is null)
        {
            return null;
        }

        var imageControl = new System.Windows.Controls.Image
        {
            Source = imageSource,
            Stretch = Media.Stretch.Uniform,
            MaxWidth = 420,
            MaxHeight = 320,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "双击查看原图"
        };

        imageControl.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            ShowOriginalImageWindow(imageSource, imageControl);
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

    private static void ShowOriginalImageWindow(Media.ImageSource imageSource, DependencyObject sourceElement)
    {
        var previewImage = new System.Windows.Controls.Image
        {
            Source = imageSource,
            Stretch = Media.Stretch.None,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var scrollViewer = new ScrollViewer
        {
            Content = previewImage,
            Background = CreateBrush("#111827"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var windowSize = GetOriginalImageWindowSize(imageSource);
        var owner = Window.GetWindow(sourceElement);
        var window = new Window
        {
            Title = "图片预览",
            Width = windowSize.Width,
            Height = windowSize.Height,
            MinWidth = 320,
            MinHeight = 240,
            Content = scrollViewer,
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

        window.Show();
    }

    private static System.Windows.Size GetOriginalImageWindowSize(Media.ImageSource imageSource)
    {
        var imageWidth = imageSource.Width;
        var imageHeight = imageSource.Height;
        var windowWidth = Math.Clamp(imageWidth + 40, 320, SystemParameters.PrimaryScreenWidth * 0.9);
        var windowHeight = Math.Clamp(imageHeight + 70, 240, SystemParameters.PrimaryScreenHeight * 0.9);
        return new System.Windows.Size(windowWidth, windowHeight);
    }

    private static Media.ImageSource? DecodeDataUrlImage(string imageDataUrl)
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
            var imageBytes = Convert.FromBase64String(base64Part);
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private void UpdateTabButtons()
    {
        SetTabButtonSelectedState(CodexTabButton, SessionService.IsCodex(_currentProviderId));
        SetTabButtonSelectedState(ClaudeTabButton, SessionService.IsClaude(_currentProviderId));
        SetTabButtonSelectedState(GrokTabButton, SessionService.IsGrok(_currentProviderId));
    }

    private static string NormalizeProviderId(string? providerId)
    {
        return SessionService.NormalizeProviderId(providerId);
    }

    private static string FormatMessageTime(DateTime timestamp)
    {
        return timestamp.ToString("yyyy/M/d HH:mm:ss");
    }

    private static void SetTabButtonSelectedState(System.Windows.Controls.Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Background = CreateBrush("#2563EB");
            button.Foreground = Media.Brushes.White;
            button.BorderBrush = CreateBrush("#1D4ED8");
            button.BorderThickness = new Thickness(1);
            return;
        }

        button.Background = Media.Brushes.White;
        button.Foreground = CreateBrush("#111827");
        button.BorderBrush = CreateBrush("#D1D5DB");
        button.BorderThickness = new Thickness(1);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        for (var index = 0; index < Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = Media.VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void ApplyScaledWheel(
        ScrollViewer? scrollViewer,
        System.Windows.Input.MouseWheelEventArgs e,
        double scale,
        ref double stepRemainder)
    {
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var wheelLines = SystemParameters.WheelScrollLines;

        var steps = (e.Delta / (double)System.Windows.Input.Mouse.MouseWheelDeltaForOneLine) * wheelLines * scale;
        stepRemainder += steps;

        var wholeSteps = (int)Math.Truncate(Math.Abs(stepRemainder));
        if (wholeSteps <= 0)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var isUp = stepRemainder > 0;
        for (var index = 0; index < wholeSteps; index++)
        {
            if (isUp)
            {
                scrollViewer.LineUp();
            }
            else
            {
                scrollViewer.LineDown();
            }
        }

        stepRemainder -= isUp ? wholeSteps : -wholeSteps;
    }

    private static Media.SolidColorBrush CreateBrush(string hexColor)
    {
        return new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(hexColor));
    }

}
