using APISwitch.Models;

namespace APISwitch.UI.Views;

public sealed class SessionListItem
{
    public SessionListItem(SessionMeta session, string title, string projectGroupName, string relativeTime, string fileSize)
    {
        Session = session;
        Title = title;
        ProjectGroupName = projectGroupName;
        RelativeTime = relativeTime;
        FileSize = fileSize;
    }

    public SessionMeta Session { get; }

    public string Title { get; }

    public string ProjectGroupName { get; }

    public string RelativeTime { get; }

    public string FileSize { get; }
}
