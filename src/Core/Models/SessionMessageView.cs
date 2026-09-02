namespace APISwitch.Models;

// 会话消息的展示模型:role 到"气泡 / 错误 / 折叠"的分派结果。
// 两套 UI 都按具体子类型选择 DataTemplate,不再各自用 C# 拼控件树。
public abstract class SessionMessageView
{
    // 气泡里显示的角色名,折叠块里显示的标题("工具" / "developer")。
    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    // 预格式化的时间文本;无时间戳的消息为空,由 HasTimestamp 控制是否显示。
    public string TimestampText { get; init; } = string.Empty;

    public bool HasTimestamp => TimestampText.Length > 0;
}

public sealed class BubbleMessageView : SessionMessageView
{
    public bool IsUser { get; init; }

    public IReadOnlyList<string> ImageDataUrls { get; init; } = Array.Empty<string>();

    // 只有图片没有文字的消息不应留出空文本行。
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
}

public sealed class ErrorMessageView : SessionMessageView
{
}

public sealed class CollapsedMessageView : SessionMessageView
{
}
