using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using APISwitch.Models;
using APISwitch.Services;
using Media = System.Windows.Media;

namespace APISwitch.Views;

public partial class SessionWindow : Window
{
    private readonly SessionService _sessionService = new();
    private string _currentProviderId = SessionService.ProviderCodex;
    private SessionMeta? _selectedSession;
    private int _loadMessagesVersion;
    private double _sessionListWheelStepRemainder;
    private double _messagesWheelStepRemainder;

    public SessionWindow()
    {
        InitializeComponent();
        UpdateTabButtons();
        _ = ReloadSessionsAsync();
    }

    private async void CodexTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderCodex;
        UpdateTabButtons();
        await ReloadSessionsAsync();
    }

    private async void ClaudeTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentProviderId, SessionService.ProviderClaude, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentProviderId = SessionService.ProviderClaude;
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
        UpdateProjectPathDisplay(_selectedSession.ProjectDir);
        DeleteSessionButton.Visibility = Visibility.Visible;
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

        var result = System.Windows.MessageBox.Show(
            this,
            $"确认删除会话“{_selectedSession.Title}”吗？",
            "删除确认",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _sessionService.DeleteSession(_selectedSession.ProviderId, _selectedSession.SessionId, _selectedSession.SourcePath);
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

    private async Task ReloadSessionsAsync()
    {
        _selectedSession = null;
        _loadMessagesVersion++;
        SessionListBox.SelectedItem = null;
        SessionListBox.ItemsSource = null;
        SessionCountTextBlock.Text = "会话（加载中...）";
        SessionEmptyTextBlock.Visibility = Visibility.Collapsed;
        ResetDetailPanel();

        List<SessionMeta> sessions;
        try
        {
            sessions = await Task.Run(() => string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase)
                ? _sessionService.ScanCodexSessions()
                : _sessionService.ScanClaudeSessions());
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"扫描会话失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            sessions = new List<SessionMeta>();
        }

        var items = sessions
            .Select(session => new SessionListItem(
                session,
                BuildDisplayTitle(session),
                FormatRelativeTime(session.LastActiveAt)))
            .ToList();

        SessionListBox.ItemsSource = items;
        SessionCountTextBlock.Text = $"会话列表 ({items.Count})";
        SessionEmptyTextBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildDisplayTitle(SessionMeta session)
    {
        if (!string.IsNullOrWhiteSpace(session.Title))
        {
            return session.Title;
        }

        return string.IsNullOrWhiteSpace(session.SessionId) ? "未命名会话" : session.SessionId;
    }

    private void ResetDetailPanel()
    {
        SessionTitleTextBlock.Text = "请选择左侧会话";
        SessionProjectPathTextBlock.Text = string.Empty;
        SessionProjectPathTextBlock.ToolTip = null;
        SessionProjectPathTextBlock.Visibility = Visibility.Collapsed;
        DeleteSessionButton.Visibility = Visibility.Collapsed;
        ShowMessagePlaceholder("选中会话后查看聊天详情");
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

        var projectDir = _selectedSession.ProjectDir.Trim();
        if (!Directory.Exists(projectDir))
        {
            System.Windows.MessageBox.Show(
                this,
                "目录不存在或无法访问",
                "提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
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
            System.Windows.MessageBox.Show(
                this,
                $"打开目录失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
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

        foreach (var message in messages)
        {
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                MessagesPanel.Children.Add(CreateToolMessageElement(message.Content, message.Timestamp));
                continue;
            }

            var isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
            MessagesPanel.Children.Add(CreateBubbleMessageElement(
                message.Content,
                isUser,
                GetRoleDisplayName(message.Role),
                message.Timestamp));
        }

        MessagesScrollViewer.ScrollToHome();
    }

    private static FrameworkElement CreateBubbleMessageElement(string content, bool isUser, string roleDisplayName, DateTime timestamp)
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

        var timestampText = CreateSelectableTextElement(
            FormatMessageTime(timestamp),
            12,
            isUser ? CreateBrush("#DBEAFE") : CreateBrush("#6B7280"));
        Grid.SetColumn(timestampText, 1);
        header.Children.Add(timestampText);

        container.Children.Add(header);
        container.Children.Add(CreateSelectableTextElement(
            content,
            13,
            isUser ? Media.Brushes.White : CreateBrush("#111827"),
            textWrapping: TextWrapping.Wrap));
        bubble.Child = container;

        return bubble;
    }

    private static FrameworkElement CreateToolMessageElement(string content, DateTime timestamp)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(CreateSelectableTextElement(
            "工具",
            12,
            CreateBrush("#1E3A8A"),
            FontWeights.SemiBold));

        var timestampText = CreateSelectableTextElement(
            FormatMessageTime(timestamp),
            12,
            CreateBrush("#6B7280"));
        Grid.SetColumn(timestampText, 1);
        header.Children.Add(timestampText);

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
        if (wheelLines <= 0)
        {
            wheelLines = 3;
        }

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

    private sealed class SessionListItem
    {
        public SessionListItem(SessionMeta session, string title, string relativeTime)
        {
            Session = session;
            Title = title;
            RelativeTime = relativeTime;
        }

        public SessionMeta Session { get; }

        public string Title { get; }

        public string RelativeTime { get; }
    }
}
