using APISwitch.Models;

namespace APISwitch.Services;

public static class SessionListBuilder
{
    public static (List<SessionListItem> Items, int SubagentCount) BuildItems(
        string providerId,
        IReadOnlyList<SessionMeta> sessions)
    {
        return SessionService.IsCodex(providerId)
            ? BuildCodexSessionItems(sessions)
            : (BuildFlatSessionItems(sessions), 0);
    }

    public static string BuildCountText(int sessionCount, int subagentCount)
    {
        return subagentCount == 0
            ? $"会话列表 ({sessionCount})"
            : $"会话列表 ({sessionCount - subagentCount} 主会话 · {subagentCount} subagent)";
    }

    private static List<SessionListItem> BuildFlatSessionItems(IEnumerable<SessionMeta> sessions)
    {
        return sessions
            .Select(session => new SessionListItem(
                session,
                BuildDisplayTitle(session),
                BuildProjectGroupName(session),
                FormatRelativeTime(session.LastActiveAt),
                FormatFileSize(GetSessionFileLength(session.SourcePath)),
                depth: 0,
                isVisible: true,
                children: Array.Empty<SessionListItem>()))
            .ToList();
    }

    private static (List<SessionListItem> Items, int SubagentCount) BuildCodexSessionItems(
        IReadOnlyList<SessionMeta> sessions)
    {
        var items = new List<SessionListItem>(sessions.Count);
        var subagentCount = 0;
        foreach (var root in CodexSessionHierarchy.Build(sessions))
        {
            // 父会话缺失的 subagent 会成为根节点，因此根自身也必须单独计入。
            subagentCount += root.DescendantCount + (root.Session.IsSubagent ? 1 : 0);
            var projectGroupName = BuildProjectGroupName(root.Session);
            var rootItem = CreateCodexSessionItem(root, projectGroupName, depth: 0, isVisible: true);
            AddItemAndDescendants(rootItem, items);
        }

        return (items, subagentCount);
    }

    private static SessionListItem CreateCodexSessionItem(
        CodexSessionNode node,
        string projectGroupName,
        int depth,
        bool isVisible)
    {
        var children = node.Children
            .Select(child => CreateCodexSessionItem(child, projectGroupName, depth + 1, isVisible: false))
            .ToList();
        var relativeTime = FormatRelativeTime(node.LatestActivityAt);
        var secondaryText = node.Session.IsSubagent
            ? string.IsNullOrWhiteSpace(node.Session.AgentNickname)
                ? $"{relativeTime} · subagent"
                : $"{relativeTime} · subagent {node.Session.AgentNickname}"
            : node.DescendantCount > 0
                ? $"{relativeTime} · {node.DescendantCount} subagent"
                : relativeTime;

        return new SessionListItem(
            node.Session,
            BuildDisplayTitle(node.Session),
            projectGroupName,
            secondaryText,
            FormatFileSize(GetSessionFileLength(node.Session.SourcePath)),
            depth,
            isVisible,
            children);
    }

    private static void AddItemAndDescendants(SessionListItem item, ICollection<SessionListItem> items)
    {
        // 两个 UI 都以扁平列表呈现树，因此按深度优先顺序加入节点。
        items.Add(item);
        foreach (var child in item.Children)
        {
            AddItemAndDescendants(child, items);
        }
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
        if (string.IsNullOrWhiteSpace(sourcePath))
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
}
