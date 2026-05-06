using System.Diagnostics;
using System.IO;
using APISwitch.UI.Services;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    public SessionWindow(string? initialProviderId = null)
    {
        _currentProviderId = NormalizeProviderId(initialProviderId);
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
        var targetProviderId = NormalizeProviderId(providerId);
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
        if (string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderCodex;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void ClaudeTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentProviderId, SessionService.ProviderClaude, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderClaude;
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
        DeleteSessionButton.IsVisible = true;
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

    private async void DeleteSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSession is null)
        {
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(this, "删除确认", $"确认删除会话“{_selectedSession.Title}”吗？");
        if (!confirmed)
        {
            return;
        }

        try
        {
            _sessionService.DeleteSession(_selectedSession.ProviderId, _selectedSession.SessionId, _selectedSession.SourcePath);
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

    private async void SessionProjectPathButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedSession is null || string.IsNullOrWhiteSpace(_selectedSession.ProjectDir))
        {
            return;
        }

        var projectDir = _selectedSession.ProjectDir.Trim();
        if (!Directory.Exists(projectDir))
        {
            await DialogService.ShowInfoAsync(this, "提示", "目录不存在或无法访问");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = projectDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"打开目录失败：{ex.Message}");
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
        SessionCountTextBlock.Text = "会话列表（加载中...）";
        SessionEmptyTextBlock.IsVisible = false;
        ResetDetailPanel();

        List<SessionMeta> sessions;

        try
        {
            sessions = await Task.Run(() =>
                string.Equals(targetProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase)
                    ? _sessionService.ScanCodexSessions()
                    : _sessionService.ScanClaudeSessions());
        }
        catch (Exception ex)
        {
            if (reloadVersion != _reloadSessionsVersion ||
                !string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await DialogService.ShowErrorAsync(this, "错误", $"扫描会话失败：{ex.Message}");
            sessions = new List<SessionMeta>();
        }

        if (reloadVersion != _reloadSessionsVersion ||
            !string.Equals(_currentProviderId, targetProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var items = sessions
            .Select(session => new SessionListItem(
                session,
                BuildDisplayTitle(session),
                BuildProjectGroupName(session),
                FormatRelativeTime(session.LastActiveAt),
                FormatFileSize(GetSessionFileLength(session.SourcePath))))
            .ToList();

        SessionGroupsItemsControl.ItemsSource = BuildSessionGroups(items);
        SessionCountTextBlock.Text = $"会话列表 ({items.Count})";
        SessionEmptyTextBlock.IsVisible = items.Count == 0;
    }

    private static string BuildDisplayTitle(SessionMeta session)
    {
        if (!string.IsNullOrWhiteSpace(session.Title))
        {
            return session.Title;
        }

        return string.IsNullOrWhiteSpace(session.SessionId) ? "未命名会话" : session.SessionId;
    }

    private static string BuildProjectGroupName(SessionMeta session)
    {
        if (!string.IsNullOrWhiteSpace(session.ProjectDir))
        {
            var normalized = session.ProjectDir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "未分组项目";
    }

    private static long GetSessionFileLength(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return 0;
        }

        try
        {
            return new FileInfo(sourcePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string FormatFileSize(long fileSizeBytes)
    {
        if (fileSizeBytes < 1024)
        {
            return $"{fileSizeBytes} B";
        }

        var sizeKb = fileSizeBytes / 1024d;
        if (sizeKb < 1024)
        {
            return $"{sizeKb:0.0} KB";
        }

        var sizeMb = sizeKb / 1024d;
        if (sizeMb < 1024)
        {
            return $"{sizeMb:0.0} MB";
        }

        var sizeGb = sizeMb / 1024d;
        return $"{sizeGb:0.0} GB";
    }

    private void ResetDetailPanel()
    {
        SessionTitleTextBlock.Text = "请选择左侧会话";
        UpdateSessionIdDisplay(null);
        SessionProjectPathButton.Content = string.Empty;
        SessionProjectPathButton.IsVisible = false;
        DeleteSessionButton.IsVisible = false;
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

        var isCodexSession = _selectedSession is not null &&
            string.Equals(_selectedSession.ProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase);

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                MessagesPanel.Children.Add(CreateCollapsedMessageElement("工具", message.Content, message.Timestamp));
                continue;
            }

            if (isCodexSession &&
                index == 0 &&
                string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase))
            {
                MessagesPanel.Children.Add(CreateCollapsedMessageElement("developer", message.Content, message.Timestamp));
                continue;
            }

            var isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
            MessagesPanel.Children.Add(CreateBubbleMessageElement(
                message.Content,
                isUser,
                GetRoleDisplayName(message.Role),
                message.Timestamp,
                message.ImageDataUrls));
        }
    }

    private static Control CreateBubbleMessageElement(
        string content,
        bool isUser,
        string roleDisplayName,
        DateTime timestamp,
        IReadOnlyList<string> imageDataUrls)
    {
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
            roleDisplayName,
            12,
            isUser ? Brushes.White : CreateBrush("#3B82F6"),
            FontWeight.SemiBold);
        header.Children.Add(roleText);

        var timeText = CreateSelectableTextElement(
            FormatMessageTime(timestamp),
            12,
            isUser ? CreateBrush("#DBEAFE") : CreateBrush("#6B7280"));
        Grid.SetColumn(timeText, 1);
        header.Children.Add(timeText);

        container.Children.Add(header);

        foreach (var imageDataUrl in imageDataUrls)
        {
            var imageElement = CreateImageElementFromDataUrl(imageDataUrl);
            if (imageElement is not null)
            {
                container.Children.Add(imageElement);
            }
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            container.Children.Add(CreateSelectableTextElement(
                content,
                13,
                isUser ? Brushes.White : CreateBrush("#111827"),
                textWrapping: TextWrapping.Wrap));
        }

        bubble.Child = container;
        return bubble;
    }

    private static Control CreateCollapsedMessageElement(string title, string content, DateTime timestamp)
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
            Stroke = CreateBrush("#374151"),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 12,
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chevronCircle = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderBrush = CreateBrush("#4B5563"),
            BorderThickness = new Thickness(2),
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

        var titleText = CreateSelectableTextElement(title, 12, CreateBrush("#1E3A8A"), FontWeight.SemiBold);
        titleText.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        var timeText = CreateSelectableTextElement(FormatMessageTime(timestamp), 12, CreateBrush("#6B7280"));
        timeText.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(timeText, 2);
        header.Children.Add(timeText);

        var headerButton = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = header
        };

        var contentBorder = new Border
        {
            Background = CreateBrush("#EEF2FF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = CreateSelectableTextElement(content, 12, CreateBrush("#1E3A8A"), textWrapping: TextWrapping.Wrap),
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var isExpanded = false;
        headerButton.Click += (_, _) =>
        {
            isExpanded = !isExpanded;
            contentBorder.IsVisible = isExpanded;
            chevronPath.Data = isExpanded ? expandedChevron : collapsedChevron;
        };

        root.Children.Add(headerButton);
        root.Children.Add(contentBorder);
        return root;
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

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                MaxWidth = 420,
                MaxHeight = 320
            }
        };
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

    private static List<SessionGroupItem> BuildSessionGroups(IReadOnlyList<SessionListItem> items)
    {
        return items
            .GroupBy(item => item.ProjectGroupName)
            .Select(group => new SessionGroupItem(group.Key, group.ToList()))
            .ToList();
    }

    private void UpdateTabButtons()
    {
        SetTabButtonSelectedState(CodexTabButton, string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase));
        SetTabButtonSelectedState(ClaudeTabButton, string.Equals(_currentProviderId, SessionService.ProviderClaude, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var now = DateTime.Now;
        var delta = now - timestamp;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)} 分钟前";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)delta.TotalHours)} 小时前";
        }

        if (delta < TimeSpan.FromDays(7))
        {
            return $"{Math.Max(1, (int)delta.TotalDays)} 天前";
        }

        return timestamp.ToString("yyyy/MM/dd");
    }

    private static string GetRoleDisplayName(string role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return "用户";
        }

        if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
        {
            return "developer";
        }

        if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return "工具";
        }

        return "AI";
    }

    private static string NormalizeProviderId(string? providerId)
    {
        return string.Equals(providerId, SessionService.ProviderClaude, StringComparison.OrdinalIgnoreCase)
            ? SessionService.ProviderClaude
            : SessionService.ProviderCodex;
    }

    private static string FormatMessageTime(DateTime timestamp)
    {
        return timestamp.ToString("yyyy/M/d HH:mm:ss");
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
