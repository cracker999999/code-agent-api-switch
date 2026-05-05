using System.Diagnostics;
using System.IO;
using System.Text;
using APISwitch.Avalonia.Services;
using APISwitch.Models;
using APISwitch.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace APISwitch.Avalonia.Views;

public partial class SessionWindow : Window
{
    private readonly SessionService _sessionService = new();

    private string _currentProviderId = SessionService.ProviderCodex;
    private SessionMeta? _selectedSession;
    private int _loadMessagesVersion;

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

    private async void SessionListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SessionListBox.SelectedItem is not SessionListItem item)
        {
            _selectedSession = null;
            ResetDetailPanel();
            return;
        }

        _selectedSession = item.Session;
        SessionTitleTextBlock.Text = item.Title;
        SessionProjectPathTextBlock.Text = _selectedSession.ProjectDir;
        SessionProjectPathTextBlock.IsVisible = !string.IsNullOrWhiteSpace(_selectedSession.ProjectDir);
        OpenProjectButton.IsVisible = !string.IsNullOrWhiteSpace(_selectedSession.ProjectDir);
        DeleteSessionButton.IsVisible = true;
        MessagesTextBox.Text = "加载中...";

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
            MessagesTextBox.Text = "加载失败";
            return;
        }

        if (currentVersion != _loadMessagesVersion)
        {
            return;
        }

        MessagesTextBox.Text = BuildMessagesText(messages);
        MessagesTextBox.CaretIndex = 0;
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

    private async void OpenProjectButton_Click(object? sender, RoutedEventArgs e)
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
        _selectedSession = null;
        _loadMessagesVersion++;

        SessionListBox.ItemsSource = null;
        SessionCountTextBlock.Text = "会话（加载中...）";
        SessionEmptyTextBlock.IsVisible = false;
        ResetDetailPanel();

        List<SessionMeta> sessions;

        try
        {
            sessions = await Task.Run(() =>
                string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase)
                    ? _sessionService.ScanCodexSessions()
                    : _sessionService.ScanClaudeSessions());
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(this, "错误", $"扫描会话失败：{ex.Message}");
            sessions = new List<SessionMeta>();
        }

        var items = sessions
            .Select(session => new SessionListItem(
                session,
                BuildDisplayTitle(session),
                FormatRelativeTime(session.LastActiveAt),
                FormatFileSize(GetSessionFileLength(session.SourcePath))))
            .ToList();

        SessionListBox.ItemsSource = items;
        SessionCountTextBlock.Text = $"会话列表 ({items.Count})";
        SessionEmptyTextBlock.IsVisible = items.Count == 0;
    }

    private void ResetDetailPanel()
    {
        SessionTitleTextBlock.Text = "请选择左侧会话";
        SessionProjectPathTextBlock.Text = string.Empty;
        SessionProjectPathTextBlock.IsVisible = false;
        OpenProjectButton.IsVisible = false;
        DeleteSessionButton.IsVisible = false;
        MessagesTextBox.Text = "选中会话后查看聊天详情";
    }

    private static string BuildDisplayTitle(SessionMeta session)
    {
        if (!string.IsNullOrWhiteSpace(session.Title))
        {
            return session.Title;
        }

        return string.IsNullOrWhiteSpace(session.SessionId) ? "未命名会话" : session.SessionId;
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

    private static string BuildMessagesText(IReadOnlyList<SessionMessage> messages)
    {
        if (messages.Count == 0)
        {
            return "暂无消息";
        }

        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            builder.Append('[')
                .Append(message.Timestamp.ToString("yyyy/M/d HH:mm:ss"))
                .Append("] ")
                .Append(GetRoleDisplayName(message.Role))
                .Append(':')
                .AppendLine();

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                builder.AppendLine(message.Content.Trim());
            }

            if (message.ImageDataUrls.Count > 0)
            {
                builder.AppendLine($"[images: {message.ImageDataUrls.Count}]");
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
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

    private void UpdateTabButtons()
    {
        SetTabButtonSelectedState(CodexTabButton, string.Equals(_currentProviderId, SessionService.ProviderCodex, StringComparison.OrdinalIgnoreCase));
        SetTabButtonSelectedState(ClaudeTabButton, string.Equals(_currentProviderId, SessionService.ProviderClaude, StringComparison.OrdinalIgnoreCase));
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

    private sealed class SessionListItem
    {
        public SessionListItem(SessionMeta session, string title, string relativeTime, string fileSize)
        {
            Session = session;
            Title = title;
            RelativeTime = relativeTime;
            FileSize = fileSize;
        }

        public SessionMeta Session { get; }

        public string Title { get; }

        public string RelativeTime { get; }

        public string FileSize { get; }
    }
}
