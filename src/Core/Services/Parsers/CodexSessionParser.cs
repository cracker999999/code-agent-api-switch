using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using APISwitch.Models;

namespace APISwitch.Services.Parsers;

/// <summary>
/// Codex 会话解析器 - 封装所有 Codex JSONL 解析逻辑
/// </summary>
public class CodexSessionParser : BaseSessionParser, ISessionParser
{
    private const int HeadLineCount = 10;
    private const int TailLineCount = 30;

    // 上下文压缩事件在会话详情中显示的提示文案
    private const string ContextCompactedNotice = "Context compacted";

    private static readonly Regex CodexImageOpenTagPattern = new(
        @"^\s*<image\b[^>]*>\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CodexImageCloseTagPattern = new(
        @"^\s*</image>\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _codexSessionIndexPath;
    private Dictionary<string, string>? _codexSessionThreadNameIndex;
    private bool _codexSessionThreadNameIndexLoaded;

    public CodexSessionParser(string? codexSessionIndexPath = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _codexSessionIndexPath = codexSessionIndexPath ?? Path.Combine(userProfile, ".codex", "session_index.jsonl");
    }

    public SessionMeta? ParseSession(string filePath)
    {
        var (headLines, tailLines) = SessionFileUtils.ReadHeadAndTailLines(filePath, HeadLineCount, TailLineCount);

        string? sessionId = null;
        string? parentSessionId = null;
        string? agentPath = null;
        string? agentNickname = null;
        string? projectDir = null;
        string? customTitle = null;
        DateTime? createdAt = null;
        DateTime? lastActiveAt = null;
        var isSubagent = false;
        var hasSessionMeta = false;
        var hasMessage = false;

        foreach (var line in headLines.Concat(tailLines))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!JsonFieldExtractor.TryGetString(root, "type", out var eventType))
            {
                continue;
            }

            // 解析 session_meta 事件
            if (string.Equals(eventType, "session_meta", StringComparison.OrdinalIgnoreCase) &&
                TryGetObject(root, "payload", out var metaPayload))
            {
                // 子代理文件会在首行元数据后附带继承的主会话元数据，只能采用首个 session_meta。
                if (!hasSessionMeta)
                {
                    hasSessionMeta = true;
                    if (JsonFieldExtractor.TryGetString(metaPayload, "id", out var parsedSessionId))
                    {
                        sessionId = parsedSessionId;
                    }

                    if (JsonFieldExtractor.TryGetString(metaPayload, "cwd", out var parsedProjectDir))
                    {
                        projectDir = parsedProjectDir;
                    }

                    isSubagent = JsonFieldExtractor.TryGetString(metaPayload, "thread_source", out var threadSource) &&
                        string.Equals(threadSource, "subagent", StringComparison.OrdinalIgnoreCase);

                    if (TryGetObject(metaPayload, "source", out var source) &&
                        TryGetObject(source, "subagent", out var subagent) &&
                        TryGetObject(subagent, "thread_spawn", out var threadSpawn))
                    {
                        isSubagent = true;
                        if (JsonFieldExtractor.TryGetString(threadSpawn, "parent_thread_id", out var parsedParentSessionId))
                        {
                            parentSessionId = parsedParentSessionId;
                        }

                        if (JsonFieldExtractor.TryGetString(threadSpawn, "agent_path", out var parsedAgentPath))
                        {
                            agentPath = parsedAgentPath;
                        }

                        if (JsonFieldExtractor.TryGetString(threadSpawn, "agent_nickname", out var parsedAgentNickname))
                        {
                            agentNickname = parsedAgentNickname;
                        }
                    }

                    createdAt = TryGetDateTime(metaPayload, "timestamp") ?? TryGetDateTime(root, "timestamp");
                }
            }
            // 解析 thread_name_updated 事件
            else if (string.Equals(eventType, "event_msg", StringComparison.OrdinalIgnoreCase) &&
                     TryGetObject(root, "payload", out var eventPayload) &&
                     JsonFieldExtractor.TryGetString(eventPayload, "type", out var payloadType) &&
                     string.Equals(payloadType, "thread_name_updated", StringComparison.OrdinalIgnoreCase) &&
                     JsonFieldExtractor.TryGetString(eventPayload, "thread_name", out var threadName))
            {
                customTitle = threadName;
            }
            // 检查是否有消息
            else if (string.Equals(eventType, "response_item", StringComparison.OrdinalIgnoreCase) &&
                     TryGetObject(root, "payload", out var responsePayload) &&
                     TryExtractMessage(responsePayload, out _, out _))
            {
                hasMessage = true;
            }

            // 更新最后活跃时间
            var lineTimestamp = TryGetDateTime(root, "timestamp");
            if (lineTimestamp.HasValue && (!lastActiveAt.HasValue || lineTimestamp.Value > lastActiveAt.Value))
            {
                lastActiveAt = lineTimestamp.Value;
            }

            if (TryGetObject(root, "payload", out var payloadTimestampOwner))
            {
                var payloadTimestamp = TryGetDateTime(payloadTimestampOwner, "timestamp");
                if (payloadTimestamp.HasValue && (!lastActiveAt.HasValue || payloadTimestamp.Value > lastActiveAt.Value))
                {
                    lastActiveAt = payloadTimestamp.Value;
                }
            }
        }

        if (!hasMessage)
        {
            return null;
        }

        // 从全局索引中查找 thread_name
        if (string.IsNullOrWhiteSpace(customTitle))
        {
            customTitle = TryFindThreadNameFromIndex(sessionId);
        }

        projectDir ??= string.Empty;
        var fallbackTime = File.GetLastWriteTime(filePath);
        var resolvedCreatedAt = createdAt ?? lastActiveAt ?? fallbackTime;
        var resolvedLastActiveAt = lastActiveAt ?? resolvedCreatedAt;

        return new SessionMeta
        {
            ProviderId = SessionService.ProviderCodex,
            SessionId = sessionId ?? string.Empty,
            ParentSessionId = parentSessionId ?? string.Empty,
            IsSubagent = isSubagent,
            AgentPath = agentPath ?? string.Empty,
            AgentNickname = agentNickname ?? string.Empty,
            Title = FirstNonEmpty(
                isSubagent ? BuildAgentTitle(agentPath, agentNickname) : string.Empty,
                NormalizeTitleText(customTitle),
                BuildSessionTitle(projectDir, string.Empty)),
            ProjectDir = projectDir,
            CreatedAt = resolvedCreatedAt,
            LastActiveAt = resolvedLastActiveAt,
            SourcePath = Path.GetFullPath(filePath)
        };
    }

    public List<SessionMessage> LoadMessages(string filePath)
    {
        var messages = new List<SessionMessage>();

        try
        {
            foreach (var line in SessionFileUtils.ReadAllLinesShared(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (!JsonFieldExtractor.TryGetString(root, "type", out var eventType))
                {
                    continue;
                }

                if (!TryGetObject(root, "payload", out var payload))
                {
                    continue;
                }

                // event_msg 中只关心上下文压缩事件：它本身没有正文，按发生顺序插入一条提示消息
                if (string.Equals(eventType, "event_msg", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsContextCompactedEvent(payload))
                    {
                        messages.Add(new SessionMessage
                        {
                            Role = "assistant",
                            Content = ContextCompactedNotice,
                            Timestamp = ResolveMessageTimestamp(root, payload, filePath)
                        });
                    }

                    continue;
                }

                if (!string.Equals(eventType, "response_item", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryExtractMessage(payload, out var role, out var content, out var imageDataUrls))
                {
                    continue;
                }

                messages.Add(new SessionMessage
                {
                    Role = role,
                    Content = content,
                    ImageDataUrls = imageDataUrls,
                    Timestamp = ResolveMessageTimestamp(root, payload, filePath)
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SessionMessage>();
        }

        return messages;
    }

    /// <summary>
    /// 判断 event_msg 的 payload 是否为上下文压缩事件
    /// </summary>
    private static bool IsContextCompactedEvent(JsonElement payload)
    {
        return JsonFieldExtractor.TryGetString(payload, "type", out var payloadType) &&
               string.Equals(payloadType, "context_compacted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 消息时间优先取行级 timestamp，其次 payload.timestamp，最后回退文件修改时间
    /// </summary>
    private static DateTime ResolveMessageTimestamp(JsonElement root, JsonElement payload, string filePath)
    {
        return TryGetDateTime(root, "timestamp")
            ?? TryGetDateTime(payload, "timestamp")
            ?? File.GetLastWriteTime(filePath);
    }

    /// <summary>
    /// 提取 Codex 消息 (简化版本,不含图片)
    /// </summary>
    private static bool TryExtractMessage(JsonElement payload, out string role, out string content)
    {
        return TryExtractMessage(payload, out role, out content, out _);
    }

    /// <summary>
    /// 提取 Codex 消息 (完整版本,含图片)
    /// </summary>
    private static bool TryExtractMessage(
        JsonElement payload,
        out string role,
        out string content,
        out List<string> imageDataUrls)
    {
        role = string.Empty;
        content = string.Empty;
        imageDataUrls = new List<string>();

        if (!JsonFieldExtractor.TryGetString(payload, "type", out var payloadType))
        {
            return false;
        }

        // message 类型
        if (string.Equals(payloadType, "message", StringComparison.OrdinalIgnoreCase))
        {
            role = JsonFieldExtractor.TryGetString(payload, "role", out var parsedRole) ? parsedRole : "assistant";
            if (TryExtractMessageContent(payload, out content, out imageDataUrls))
            {
                return !string.IsNullOrWhiteSpace(content) || imageDataUrls.Count > 0;
            }

            imageDataUrls = ExtractInputImageDataUrls(payload);
            content = JsonFieldExtractor.ExtractText(payload, "content");
            return !string.IsNullOrWhiteSpace(content) || imageDataUrls.Count > 0;
        }

        // function_call 类型
        if (string.Equals(payloadType, "function_call", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = JsonFieldExtractor.TryGetString(payload, "name", out var parsedToolName) ? parsedToolName : "unknown";
            role = "assistant";
            content = $"[Tool: {toolName}]";
            return true;
        }

        // function_call_output 类型
        if (string.Equals(payloadType, "function_call_output", StringComparison.OrdinalIgnoreCase))
        {
            role = "tool";
            content = JsonFieldExtractor.ExtractText(payload, "output");
            return !string.IsNullOrWhiteSpace(content);
        }

        return false;
    }

    /// <summary>
    /// 提取消息内容 - 处理 content 字段可能是字符串或数组的情况
    /// </summary>
    private static bool TryExtractMessageContent(JsonElement payload, out string content, out List<string> imageDataUrls)
    {
        content = string.Empty;
        imageDataUrls = new List<string>();

        if (!JsonFieldExtractor.TryGetProperty(payload, "content", out var contentElement))
        {
            return false;
        }

        // content 是字符串
        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            content = JsonFieldExtractor.ExtractText(payload, "content");
            imageDataUrls = ExtractInputImageDataUrls(payload);
            return true;
        }

        // content 是数组 - 可能包含文本和图片
        var items = contentElement.EnumerateArray().ToList();
        var textParts = new List<string>();
        for (var index = 0; index < items.Count; index++)
        {
            // 尝试匹配 <image>...</image> 包裹的图片
            if (TryMatchWrappedInputImage(items, index, out var wrappedImageUrl))
            {
                imageDataUrls.Add(wrappedImageUrl);
                index += 2; // 跳过 <image> 标签和 </image> 标签
                continue;
            }

            // 尝试提取独立的图片
            if (TryExtractInputImageDataUrl(items[index], out var standaloneImageUrl))
            {
                imageDataUrls.Add(standaloneImageUrl);
                continue;
            }

            // 提取文本
            var part = JsonFieldExtractor.ExtractText(items[index]);
            if (!string.IsNullOrWhiteSpace(part))
            {
                textParts.Add(part.Trim());
            }
        }

        content = string.Join(Environment.NewLine, textParts).Trim();
        return true;
    }

    /// <summary>
    /// 匹配 Codex 特有的 <image>...</image> 包裹格式
    /// </summary>
    private static bool TryMatchWrappedInputImage(IReadOnlyList<JsonElement> items, int startIndex, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (startIndex + 2 >= items.Count)
        {
            return false;
        }

        // 检查开始标签
        if (!TryExtractInputText(items[startIndex], out var openTagText) ||
            !CodexImageOpenTagPattern.IsMatch(openTagText))
        {
            return false;
        }

        // 提取图片 URL
        if (!TryExtractInputImageDataUrl(items[startIndex + 1], out imageUrl))
        {
            return false;
        }

        // 检查结束标签
        if (!TryExtractInputText(items[startIndex + 2], out var closeTagText) ||
            !CodexImageCloseTagPattern.IsMatch(closeTagText))
        {
            imageUrl = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 提取 input_text 类型的文本
    /// </summary>
    private static bool TryExtractInputText(JsonElement item, out string text)
    {
        text = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
               JsonFieldExtractor.TryGetString(item, "type", out var itemType) &&
               string.Equals(itemType, "input_text", StringComparison.OrdinalIgnoreCase) &&
               JsonFieldExtractor.TryGetString(item, "text", out text);
    }

    /// <summary>
    /// 提取 input_image 类型的图片 URL
    /// </summary>
    private static bool TryExtractInputImageDataUrl(JsonElement item, out string imageUrl)
    {
        imageUrl = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
               JsonFieldExtractor.TryGetString(item, "type", out var itemType) &&
               string.Equals(itemType, "input_image", StringComparison.OrdinalIgnoreCase) &&
               JsonFieldExtractor.TryGetString(item, "image_url", out imageUrl);
    }

    /// <summary>
    /// 从 payload 中提取所有图片 URL
    /// </summary>
    private static List<string> ExtractInputImageDataUrls(JsonElement payload)
    {
        var urls = new List<string>();
        if (!JsonFieldExtractor.TryGetProperty(payload, "content", out var contentElement))
        {
            return urls;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return urls;
        }

        foreach (var item in contentElement.EnumerateArray())
        {
            if (TryExtractInputImageDataUrl(item, out var imageUrl))
            {
                urls.Add(imageUrl);
            }
        }

        return urls;
    }

    private string TryFindThreadNameFromIndex(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return string.Empty;
        }

        EnsureThreadNameIndexLoaded();
        if (_codexSessionThreadNameIndex is null)
        {
            return string.Empty;
        }

        return _codexSessionThreadNameIndex.TryGetValue(sessionId, out var threadName)
            ? threadName
            : string.Empty;
    }

    private void EnsureThreadNameIndexLoaded()
    {
        if (_codexSessionThreadNameIndexLoaded)
        {
            return;
        }

        _codexSessionThreadNameIndexLoaded = true;
        _codexSessionThreadNameIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_codexSessionIndexPath))
        {
            return;
        }

        foreach (var line in SessionFileUtils.ReadAllLinesShared(_codexSessionIndexPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!JsonFieldExtractor.TryGetString(root, "id", out var id) ||
                    !JsonFieldExtractor.TryGetString(root, "thread_name", out var threadName))
                {
                    continue;
                }

                _codexSessionThreadNameIndex[id] = threadName;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }
    }

    private static DateTime? TryGetDateTime(JsonElement element, string propertyName)
    {
        if (!JsonFieldExtractor.TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return ParseDateTime(value);
    }

    private static DateTime? ParseDateTime(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                return dto.LocalDateTime;
            }

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixFromString))
            {
                return ParseUnixTime(unixFromString);
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var unix))
            {
                return ParseUnixTime(unix);
            }

            if (value.TryGetDouble(out var unixDouble))
            {
                var unixLong = Convert.ToInt64(Math.Truncate(unixDouble));
                return ParseUnixTime(unixLong);
            }
        }

        return null;
    }

    private static DateTime? ParseUnixTime(long unix)
    {
        try
        {
            return unix > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix).LocalDateTime
                : DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Codex 特有的 BuildSessionTitle：只取最后一级目录名
    private static string BuildSessionTitle(string projectDir, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(projectDir))
        {
            var normalized = projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return fallback;
    }

    private static string BuildAgentTitle(string? agentPath, string? agentNickname)
    {
        if (!string.IsNullOrWhiteSpace(agentPath))
        {
            var taskName = Path.GetFileName(agentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(taskName))
            {
                return NormalizeTitleText(taskName);
            }
        }

        return NormalizeTitleText(agentNickname);
    }
}
