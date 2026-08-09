using APISwitch.Models;

namespace APISwitch.Services;

public static class CodexSessionHierarchy
{
    public static List<CodexSessionNode> Build(IReadOnlyList<SessionMeta> sessions)
    {
        var nodes = sessions.Select(session => new CodexSessionNode(session)).ToList();
        var nodesBySessionId = new Dictionary<string, CodexSessionNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Session.SessionId))
            {
                nodesBySessionId.TryAdd(node.Session.SessionId, node);
            }
        }

        foreach (var node in nodes)
        {
            var parentSessionId = node.Session.ParentSessionId;
            if (!node.Session.IsSubagent ||
                string.IsNullOrWhiteSpace(parentSessionId) ||
                !nodesBySessionId.TryGetValue(parentSessionId, out var parent) ||
                ReferenceEquals(parent, node) ||
                WouldCreateCycle(node, parent, nodesBySessionId))
            {
                continue;
            }

            node.Parent = parent;
            parent.MutableChildren.Add(node);
        }

        var roots = nodes.Where(node => node.Parent is null).ToList();
        foreach (var root in roots)
        {
            UpdateAggregateValues(root);
        }

        return roots
            .OrderByDescending(node => node.LatestActivityAt)
            .ToList();
    }

    private static bool WouldCreateCycle(
        CodexSessionNode node,
        CodexSessionNode parent,
        IReadOnlyDictionary<string, CodexSessionNode> nodesBySessionId)
    {
        // 元数据来自外部日志，损坏的父链不能导致递归聚合无限循环。
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = parent;
        while (!string.IsNullOrWhiteSpace(current.Session.SessionId) && visited.Add(current.Session.SessionId))
        {
            if (ReferenceEquals(current, node))
            {
                return true;
            }

            var parentSessionId = current.Session.ParentSessionId;
            if (string.IsNullOrWhiteSpace(parentSessionId) ||
                !nodesBySessionId.TryGetValue(parentSessionId, out var nextParent))
            {
                return false;
            }

            current = nextParent;
        }

        return false;
    }

    private static void UpdateAggregateValues(CodexSessionNode node)
    {
        foreach (var child in node.MutableChildren)
        {
            UpdateAggregateValues(child);
        }

        node.MutableChildren.Sort((left, right) => right.LatestActivityAt.CompareTo(left.LatestActivityAt));
        var latestChildActivityAt = node.MutableChildren.Count == 0
            ? node.Session.LastActiveAt
            : node.MutableChildren[0].LatestActivityAt;
        node.LatestActivityAt = latestChildActivityAt > node.Session.LastActiveAt
            ? latestChildActivityAt
            : node.Session.LastActiveAt;
        node.DescendantCount = node.MutableChildren.Sum(child => child.DescendantCount + 1);
    }
}

public sealed class CodexSessionNode
{
    internal CodexSessionNode(SessionMeta session)
    {
        Session = session;
        LatestActivityAt = session.LastActiveAt;
    }

    public SessionMeta Session { get; }

    public IReadOnlyList<CodexSessionNode> Children => MutableChildren;

    public DateTime LatestActivityAt { get; internal set; }

    public int DescendantCount { get; internal set; }

    internal CodexSessionNode? Parent { get; set; }

    internal List<CodexSessionNode> MutableChildren { get; } = new();
}
