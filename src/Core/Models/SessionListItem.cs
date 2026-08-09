using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace APISwitch.Models;

public sealed class SessionListItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isVisible;

    public SessionListItem(
        SessionMeta session,
        string title,
        string projectGroupName,
        string secondaryText,
        string fileSize,
        int depth,
        bool isVisible,
        IReadOnlyList<SessionListItem> children)
    {
        Session = session;
        Title = title;
        ProjectGroupName = projectGroupName;
        SecondaryText = secondaryText;
        FileSize = fileSize;
        IndentWidth = depth * 14d;
        _isVisible = isVisible;
        Children = children;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionMeta Session { get; }

    public string Title { get; }

    public string ProjectGroupName { get; }

    public string SecondaryText { get; }

    public string FileSize { get; }

    public double IndentWidth { get; }

    public IReadOnlyList<SessionListItem> Children { get; }

    public bool HasChildren => Children.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        private set => SetField(ref _isExpanded, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetField(ref _isVisible, value);
    }

    public void SetExpanded(bool isExpanded)
    {
        IsExpanded = isExpanded;
        foreach (var child in Children)
        {
            child.SetVisible(isExpanded);
        }
    }

    public bool ContainsDescendant(SessionListItem item)
    {
        return Children.Any(child => ReferenceEquals(child, item) || child.ContainsDescendant(item));
    }

    private void SetVisible(bool isVisible)
    {
        IsVisible = isVisible;
        foreach (var child in Children)
        {
            // 重新显示父项时，只恢复原本处于展开状态的后代分支。
            child.SetVisible(isVisible && IsExpanded);
        }
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
