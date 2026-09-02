using APISwitch.Models;

namespace APISwitch.Services;

// 把会话消息映射为展示模型。分派规则原先在 WPF / Avalonia 两份 code-behind 里各写一遍,统一到这里。
public static class SessionMessageViewBuilder
{
    private const string TimestampFormat = "yyyy/M/d HH:mm:ss";

    public static List<SessionMessageView> Build(string? providerId, IReadOnlyList<SessionMessage> messages)
    {
        // Codex 与 Grok 的首条 developer 消息是系统指令,折叠显示;Claude 会话没有这个约定。
        var isCodexStyleSession = SessionService.IsCodex(providerId) || SessionService.IsGrok(providerId);

        var views = new List<SessionMessageView>(messages.Count);
        for (var index = 0; index < messages.Count; index++)
        {
            views.Add(BuildOne(messages[index], index, isCodexStyleSession));
        }

        return views;
    }

    private static SessionMessageView BuildOne(SessionMessage message, int index, bool isCodexStyleSession)
    {
        var timestampText = message.Timestamp.HasValue
            ? message.Timestamp.Value.ToString(TimestampFormat)
            : string.Empty;

        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            // 带图片的工具结果必须直接可见,折叠起来用户看不到图。
            return message.ImageDataUrls.Count > 0
                ? NewBubble(message, timestampText, isUser: false)
                : NewCollapsed("工具", message.Content, timestampText);
        }

        if (string.Equals(message.Role, "error", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorMessageView
            {
                Title = "错误",
                Content = message.Content,
                TimestampText = timestampText
            };
        }

        if (isCodexStyleSession && index == 0 &&
            string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase))
        {
            return NewCollapsed("developer", message.Content, timestampText);
        }

        var isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
        return NewBubble(message, timestampText, isUser);
    }

    private static BubbleMessageView NewBubble(SessionMessage message, string timestampText, bool isUser) => new()
    {
        Title = SessionService.GetRoleDisplayName(message.Role),
        Content = message.Content,
        TimestampText = timestampText,
        IsUser = isUser,
        ImageDataUrls = message.ImageDataUrls
    };

    private static CollapsedMessageView NewCollapsed(string title, string content, string timestampText) => new()
    {
        Title = title,
        Content = content,
        TimestampText = timestampText
    };
}
