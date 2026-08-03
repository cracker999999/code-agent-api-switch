using System.Globalization;
using System.Text;
using System.Text.Json;
using APISwitch.Models;

namespace APISwitch.Services.Parsers;

/// <summary>
/// Grok 会话解析器 - 采用宽松的 JSONL 字段识别策略，兼容 Grok 常见日志结构。
/// </summary>
public class GrokSessionParser : BaseSessionParser, ISessionParser
{
    private const int HeadLineCount = 10;
    private const int TailLineCount = 30;
    private const string UpdatesFileName = "updates.jsonl";
    private const string RewindPointsFileName = "rewind_points.jsonl";

    private sealed class ParsedMessage(SessionMessage message, int? promptIndex)
    {
        public SessionMessage Message { get; } = message;

        public int? PromptIndex { get; } = promptIndex;

        public ParsedMessage? LinkedAssistantMessage { get; set; }

        public IReadOnlyList<string> LinkedToolCallIds { get; set; } = Array.Empty<string>();
    }

    private sealed record TimestampCandidate(string Role, string ContentKey, DateTime Timestamp);

    private sealed record TimestampTarget(ParsedMessage ParsedMessage, string ContentKey);

    private sealed class TimestampSources
    {
        public List<TimestampCandidate> MessageCandidates { get; } = new();

        public Dictionary<string, DateTime> ToolCallTimestamps { get; } = new(StringComparer.Ordinal);
    }

    public SessionMeta? ParseSession(string filePath)
    {
        var (headLines, tailLines) = SessionFileUtils.ReadHeadAndTailLines(filePath, HeadLineCount, TailLineCount);

        string? sessionId = null;
        string? projectDir = null;
        string? title = null;
        DateTime? createdAt = null;
        DateTime? lastActiveAt = null;
        var hasMessage = false;
        var sessionDirectory = TryGetGrokSessionDirectory(filePath);
        if (!string.IsNullOrWhiteSpace(sessionDirectory))
        {
            sessionId = TryGetSessionIdFromDirectory(sessionDirectory);
            TryApplySidecarMetadata(sessionDirectory, ref projectDir, ref title, ref createdAt, ref lastActiveAt);
            projectDir ??= TryDecodeProjectDirFromSessionDirectory(sessionDirectory);
        }

        foreach (var line in headLines.Concat(tailLines))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            sessionId ??= FindString(root, "sessionId", "session_id", "conversationId", "conversation_id");
            projectDir ??= FindString(root, "cwd", "workingDirectory", "working_directory", "projectDir", "project_dir");
            title ??= FindString(root, "title", "summary", "customTitle", "custom-title", "thread_name");
            createdAt ??= FindDateTimeMultiple(root, "timestamp", "created_at", "createdAt");
            lastActiveAt ??= FindDateTimeMultiple(root, "timestamp", "last_active_at", "lastActiveAt", "updated_at", "updatedAt");

            if (TryExtractMetadata(root, out var metadata))
            {
                sessionId ??= metadata.SessionId;
                projectDir ??= metadata.ProjectDir;
                title ??= metadata.Title;
                if (metadata.CreatedAt != default)
                {
                    createdAt ??= metadata.CreatedAt;
                }

                if (metadata.LastActiveAt != default)
                {
                    lastActiveAt ??= metadata.LastActiveAt;
                }
                continue;
            }

            if (TryExtractMessage(root, out _))
            {
                hasMessage = true;
            }
        }

        if (!hasMessage)
        {
            return null;
        }

        var fallbackTime = File.GetLastWriteTime(filePath);
        var resolvedCreatedAt = createdAt ?? lastActiveAt ?? fallbackTime;
        var resolvedLastActiveAt = lastActiveAt ?? resolvedCreatedAt;
        sessionId ??= TryGetSessionIdFromFilePath(filePath);
        projectDir ??= string.Empty;

        return new SessionMeta
        {
            ProviderId = SessionService.ProviderGrok,
            SessionId = sessionId ?? string.Empty,
            Title = FirstNonEmpty(
                NormalizeTitleText(title),
                BuildSessionTitle(projectDir, string.Empty)),
            ProjectDir = projectDir,
            CreatedAt = resolvedCreatedAt,
            LastActiveAt = resolvedLastActiveAt,
            SourcePath = Path.GetFullPath(filePath)
        };
    }

    public List<SessionMessage> LoadMessages(string filePath)
    {
        var parsedMessages = new List<ParsedMessage>();
        var pendingReasoningMessages = new List<ParsedMessage>();

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
                TryGetString(root, "type", out var eventType);

                ParsedMessage? parsedMessage = null;
                if (TryExtractMessage(root, out var message))
                {
                    parsedMessage = new ParsedMessage(message, TryGetPromptIndex(root));
                    parsedMessages.Add(parsedMessage);
                }

                if (string.Equals(eventType, "reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    if (parsedMessage is not null)
                    {
                        pendingReasoningMessages.Add(parsedMessage);
                    }

                    continue;
                }

                // 新一轮用户提问意味着上一轮在思考阶段被中断，残留的 reasoning 不能挪用后续 assistant 的时间。
                if (parsedMessage is not null &&
                    string.Equals(parsedMessage.Message.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    pendingReasoningMessages.Clear();
                }

                if (!string.Equals(eventType, "assistant", StringComparison.OrdinalIgnoreCase) ||
                    pendingReasoningMessages.Count == 0)
                {
                    continue;
                }

                var toolCallIds = GetToolCallIds(root);
                foreach (var reasoningMessage in pendingReasoningMessages)
                {
                    reasoningMessage.LinkedAssistantMessage = parsedMessage;
                    reasoningMessage.LinkedToolCallIds = toolCallIds;
                }

                pendingReasoningMessages.Clear();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SessionMessage>();
        }

        ApplySidecarMessageTimestamps(filePath, parsedMessages);
        return parsedMessages.Select(static parsed => parsed.Message).ToList();
    }

    private static void ApplySidecarMessageTimestamps(string filePath, IReadOnlyList<ParsedMessage> parsedMessages)
    {
        var sessionDirectory = TryGetGrokSessionDirectory(filePath);
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            return;
        }

        var timestampSources = LoadUpdateTimestampSources(sessionDirectory);
        ApplyUpdateTimestampCandidates(parsedMessages, timestampSources.MessageCandidates);

        // rewind_points 的 created_at 是发送前的文件快照时间，比真实发送时间早数秒，只当 updates 匹配不上时的兜底。
        ApplyRewindPointTimestamps(sessionDirectory, parsedMessages);
        ApplyLinkedReasoningTimestamps(parsedMessages, timestampSources.ToolCallTimestamps);
    }

    /// <summary>
    /// 逐行解析 JSONL 侧车文件 - 容忍单行残缺（Grok 运行期持续追加），文件缺失或不可读时静默跳过。
    /// </summary>
    private static void ForEachJsonLine(string filePath, Action<JsonElement> handleLine)
    {
        try
        {
            foreach (var line in SessionFileUtils.EnumerateLinesShared(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    handleLine(document.RootElement);
                }
                catch (JsonException)
                {
                    // 末行可能尚未写完整；忽略该行，已完成的消息仍应可查看。
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 侧车文件缺失或被独占，时间戳回填降级为不可用。
        }
    }

    private static void ApplyRewindPointTimestamps(
        string sessionDirectory,
        IReadOnlyList<ParsedMessage> parsedMessages)
    {
        var timestampsByPromptIndex = new Dictionary<int, DateTime>();
        ForEachJsonLine(Path.Combine(sessionDirectory, RewindPointsFileName), root =>
        {
            var promptIndex = TryGetPromptIndex(root);
            var timestamp = FindDateTimeMultiple(root, "created_at", "createdAt", "timestamp");
            if (promptIndex.HasValue && timestamp.HasValue)
            {
                timestampsByPromptIndex[promptIndex.Value] = timestamp.Value;
            }
        });

        foreach (var parsedMessage in parsedMessages)
        {
            if (!string.Equals(parsedMessage.Message.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                parsedMessage.Message.Timestamp.HasValue ||
                !parsedMessage.PromptIndex.HasValue)
            {
                continue;
            }

            if (timestampsByPromptIndex.TryGetValue(parsedMessage.PromptIndex.Value, out var timestamp))
            {
                parsedMessage.Message.Timestamp = timestamp;
            }
        }
    }

    private static TimestampSources LoadUpdateTimestampSources(string sessionDirectory)
    {
        var sources = new TimestampSources();
        var assistantContent = new StringBuilder();
        DateTime? assistantStartedAt = null;

        ForEachJsonLine(Path.Combine(sessionDirectory, UpdatesFileName), root =>
        {
            if (!TryGetObject(root, "params", out var parameters) ||
                !TryGetObject(parameters, "update", out var update) ||
                !TryGetString(update, "sessionUpdate", out var updateType))
            {
                return;
            }

            var timestamp = FindDateTimeMultiple(root, "timestamp");
            if (IsAssistantMessageBoundary(updateType))
            {
                FlushAssistantTimestampCandidate(sources.MessageCandidates, assistantContent, ref assistantStartedAt);
            }

            if (string.Equals(updateType, "tool_call", StringComparison.OrdinalIgnoreCase) &&
                timestamp.HasValue &&
                TryGetString(update, "toolCallId", out var toolCallId) &&
                !string.IsNullOrWhiteSpace(toolCallId))
            {
                sources.ToolCallTimestamps.TryAdd(toolCallId, timestamp.Value);
            }

            if (string.Equals(updateType, "user_message_chunk", StringComparison.OrdinalIgnoreCase))
            {
                if (timestamp.HasValue && TryExtractUpdateText(update, out var userContent))
                {
                    var contentKey = NormalizeTimestampContent("user", userContent);
                    if (!string.IsNullOrWhiteSpace(contentKey))
                    {
                        sources.MessageCandidates.Add(new TimestampCandidate("user", contentKey, timestamp.Value));
                    }
                }

                return;
            }

            if (string.Equals(updateType, "agent_message_chunk", StringComparison.OrdinalIgnoreCase) &&
                timestamp.HasValue &&
                TryExtractUpdateText(update, out var assistantChunk))
            {
                assistantStartedAt ??= timestamp.Value;
                assistantContent.Append(assistantChunk);
            }
        });

        FlushAssistantTimestampCandidate(sources.MessageCandidates, assistantContent, ref assistantStartedAt);
        return sources;
    }

    /// <summary>
    /// 助手消息在 updates.jsonl 中被切成多个 chunk，遇到这些类型说明当前助手消息已结束。
    /// agent_thought_chunk 不是边界 - 它与 agent_message_chunk 交错出现在同一条助手消息内。
    /// </summary>
    private static bool IsAssistantMessageBoundary(string updateType)
    {
        return string.Equals(updateType, "user_message_chunk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(updateType, "tool_call", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(updateType, "turn_completed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(updateType, "rewind_marker", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(updateType, "retry_state", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlushAssistantTimestampCandidate(
        ICollection<TimestampCandidate> candidates,
        StringBuilder assistantContent,
        ref DateTime? assistantStartedAt)
    {
        if (assistantStartedAt.HasValue && assistantContent.Length > 0)
        {
            var contentKey = NormalizeTimestampContent("assistant", assistantContent.ToString());
            if (!string.IsNullOrWhiteSpace(contentKey))
            {
                candidates.Add(new TimestampCandidate("assistant", contentKey, assistantStartedAt.Value));
            }
        }

        assistantContent.Clear();
        assistantStartedAt = null;
    }

    private static bool TryExtractUpdateText(JsonElement update, out string text)
    {
        text = JsonFieldExtractor.ExtractText(update, "content");
        return !string.IsNullOrEmpty(text);
    }

    private static void ApplyUpdateTimestampCandidates(
        IReadOnlyList<ParsedMessage> parsedMessages,
        IReadOnlyList<TimestampCandidate> candidates)
    {
        var targets = parsedMessages
            .Where(static parsed => IsTimestampedMessageRole(parsed.Message.Role) &&
                                    !string.IsNullOrWhiteSpace(parsed.Message.Content))
            .Select(static parsed => new TimestampTarget(
                parsed,
                NormalizeTimestampContent(parsed.Message.Role, parsed.Message.Content)))
            .Where(static target => !string.IsNullOrWhiteSpace(target.ContentKey))
            .ToList();

        if (targets.Count == 0 || candidates.Count == 0)
        {
            return;
        }

        // updates.jsonl 会保留回退分支。使用当前 chat_history 消息序列做 LCS 对齐，只采纳仍在当前分支中的候选时间。
        var matches = new int[targets.Count + 1, candidates.Count + 1];
        for (var targetIndex = targets.Count - 1; targetIndex >= 0; targetIndex--)
        {
            for (var candidateIndex = candidates.Count - 1; candidateIndex >= 0; candidateIndex--)
            {
                matches[targetIndex, candidateIndex] = IsTimestampMatch(targets[targetIndex], candidates[candidateIndex])
                    ? matches[targetIndex + 1, candidateIndex + 1] + 1
                    : Math.Max(matches[targetIndex + 1, candidateIndex], matches[targetIndex, candidateIndex + 1]);
            }
        }

        var currentTarget = 0;
        var currentCandidate = 0;
        while (currentTarget < targets.Count && currentCandidate < candidates.Count)
        {
            // 填表时匹配即写入 +1，因此回溯只需判断是否匹配即可确定该格落在最优路径上。
            if (IsTimestampMatch(targets[currentTarget], candidates[currentCandidate]))
            {
                targets[currentTarget].ParsedMessage.Message.Timestamp ??= candidates[currentCandidate].Timestamp;
                currentTarget++;
                currentCandidate++;
                continue;
            }

            if (matches[currentTarget, currentCandidate + 1] >= matches[currentTarget + 1, currentCandidate])
            {
                currentCandidate++;
            }
            else
            {
                currentTarget++;
            }
        }
    }

    private static void ApplyLinkedReasoningTimestamps(
        IReadOnlyList<ParsedMessage> parsedMessages,
        IReadOnlyDictionary<string, DateTime> toolCallTimestamps)
    {
        foreach (var parsedMessage in parsedMessages)
        {
            if (parsedMessage.Message.Timestamp.HasValue)
            {
                continue;
            }

            if (parsedMessage.LinkedAssistantMessage?.Message.Timestamp is { } assistantTimestamp)
            {
                parsedMessage.Message.Timestamp = assistantTimestamp;
                continue;
            }

            foreach (var toolCallId in parsedMessage.LinkedToolCallIds)
            {
                if (toolCallTimestamps.TryGetValue(toolCallId, out var toolCallTimestamp))
                {
                    parsedMessage.Message.Timestamp = toolCallTimestamp;
                    break;
                }
            }
        }
    }

    private static bool IsTimestampedMessageRole(string role)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTimestampMatch(TimestampTarget target, TimestampCandidate candidate)
    {
        return string.Equals(target.ParsedMessage.Message.Role, candidate.Role, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(target.ContentKey, candidate.ContentKey, StringComparison.Ordinal);
    }

    private static string NormalizeTimestampContent(string role, string content)
    {
        // 必须与 TryExtractMessage 消费的 NormalizeMessageContent 保持同一套规则，否则 LCS 会静默全部失配。
        // chat_history 侧多段内容用 Environment.NewLine 拼接，updates.jsonl 的流式 chunk 是裸 \n，需归一化换行。
        return NormalizeMessageContent(role, content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static int? TryGetPromptIndex(JsonElement root)
    {
        if (!TryGetProperty(root, "prompt_index", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static IReadOnlyList<string> GetToolCallIds(JsonElement root)
    {
        var messageRoot = SelectMessageRoot(root);
        if (!TryGetProperty(messageRoot, "tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var toolCallIds = new List<string>();
        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (toolCall.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var toolCallId = FirstNonEmpty(
                GetTopLevelString(toolCall, "id"),
                GetTopLevelString(toolCall, "toolCallId"),
                GetTopLevelString(toolCall, "tool_call_id"));
            if (!string.IsNullOrWhiteSpace(toolCallId))
            {
                toolCallIds.Add(toolCallId);
            }
        }

        return toolCallIds;
    }

    private static bool TryExtractMetadata(JsonElement root, out SessionMeta metadata)
    {
        metadata = new SessionMeta();

        if (!TryGetString(root, "type", out var eventType))
        {
            return false;
        }

        if (!string.Equals(eventType, "session_meta", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "session", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = SelectPayload(root);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            payload = root;
        }

        metadata.SessionId = FirstNonEmpty(
            FindString(payload, "id", "sessionId", "session_id"),
            FindString(root, "sessionId", "session_id", "id")) ?? string.Empty;
        metadata.ProjectDir = FirstNonEmpty(
            FindString(payload, "cwd", "workingDirectory", "working_directory", "projectDir", "project_dir"),
            FindString(root, "cwd", "workingDirectory", "working_directory", "projectDir", "project_dir")) ?? string.Empty;
        metadata.Title = FirstNonEmpty(
            FindString(payload, "title", "summary", "customTitle", "thread_name", "name"),
            FindString(root, "title", "summary", "customTitle", "thread_name", "name")) ?? string.Empty;
        metadata.CreatedAt = FindDateTimeMultiple(payload, "timestamp", "created_at", "createdAt")
            ?? FindDateTimeMultiple(root, "timestamp", "created_at", "createdAt")
            ?? default;
        metadata.LastActiveAt = FindDateTimeMultiple(payload, "last_active_at", "lastActiveAt", "updated_at", "updatedAt", "timestamp")
            ?? FindDateTimeMultiple(root, "last_active_at", "lastActiveAt", "updated_at", "updatedAt", "timestamp")
            ?? default;

        return !string.IsNullOrWhiteSpace(metadata.SessionId) ||
               !string.IsNullOrWhiteSpace(metadata.ProjectDir) ||
               !string.IsNullOrWhiteSpace(metadata.Title) ||
               metadata.CreatedAt != default ||
               metadata.LastActiveAt != default;
    }

    private static bool TryExtractMessage(JsonElement root, out SessionMessage message)
    {
        message = new SessionMessage();

        if (JsonFieldExtractor.FindBoolean(root, "isMeta") == true)
        {
            return false;
        }

        string? eventType = null;
        if (TryGetString(root, "type", out var rootType))
        {
            eventType = rootType;
        }
        if (IsMetadataType(eventType))
        {
            return false;
        }

        var messageRoot = SelectMessageRoot(root);
        var role = FirstNonEmpty(
            FindString(messageRoot, "role"),
            FindString(root, "role"));

        var timestamp = FindDateTimeMultiple(root, "timestamp", "created_at", "createdAt", "last_active_at", "lastActiveAt", "updated_at", "updatedAt");

        var hasContent = TryExtractContent(messageRoot, out var content, out var imageDataUrls);
        if (!hasContent && messageRoot.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            role = InferRole(eventType, messageRoot);
        }

        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) && IsAllToolResults(messageRoot))
        {
            role = "tool";
        }

        if (string.IsNullOrWhiteSpace(content) && imageDataUrls.Count == 0)
        {
            return false;
        }

        var normalizedRole = NormalizeRole(role);
        content = NormalizeMessageContent(normalizedRole, content);

        message = new SessionMessage
        {
            Role = normalizedRole,
            Content = content,
            ImageDataUrls = imageDataUrls,
            Timestamp = timestamp
        };

        return !string.IsNullOrWhiteSpace(message.Content) || message.ImageDataUrls.Count > 0 || !string.IsNullOrWhiteSpace(message.Role);
    }

    private static string NormalizeMessageContent(string role, string content)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            ? StripUserQueryEnvelope(content)
            : content;
    }

    private static string StripUserQueryEnvelope(string content)
    {
        var trimmed = content.Trim();
        const string openingTag = "<user_query>";
        const string closingTag = "</user_query>";

        if (!trimmed.StartsWith(openingTag, StringComparison.OrdinalIgnoreCase) ||
            !trimmed.EndsWith(closingTag, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return trimmed[openingTag.Length..^closingTag.Length].Trim();
    }

    private static bool TryExtractContent(JsonElement source, out string content, out List<string> imageDataUrls)
    {
        content = string.Empty;
        imageDataUrls = new List<string>();

        if (TryGetProperty(source, "content", out var contentElement))
        {
            content = ExtractContent(contentElement, imageDataUrls);
            if (!string.IsNullOrWhiteSpace(content) || imageDataUrls.Count > 0)
            {
                return true;
            }
        }

        foreach (var propertyName in new[] { "text", "message", "output", "summary" })
        {
            if (!TryGetProperty(source, propertyName, out var value))
            {
                continue;
            }

            var text = JsonFieldExtractor.ExtractText(value).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                content = text;
                return true;
            }
        }

        content = JsonFieldExtractor.ExtractText(source).Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static string ExtractContent(JsonElement contentElement, List<string> imageDataUrls)
    {
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return JsonFieldExtractor.ExtractText(contentElement).Trim();
        }

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (TryExtractImageDataUrl(item, out var imageDataUrl))
            {
                imageDataUrls.Add(imageDataUrl);
                continue;
            }

            if (TryExtractToolMarker(item, out var toolMarker))
            {
                textParts.Add(toolMarker);
                continue;
            }

            var text = JsonFieldExtractor.ExtractText(item).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                textParts.Add(text);
            }
        }

        return string.Join(Environment.NewLine, textParts).Trim();
    }

    private static bool TryExtractToolMarker(JsonElement item, out string marker)
    {
        marker = string.Empty;
        if (!TryGetString(item, "type", out var itemType))
        {
            return false;
        }

        if (string.Equals(itemType, "tool_use", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemType, "function_call", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemType, "tool_call", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = FirstNonEmpty(
                FindString(item, "name"),
                FindString(item, "tool_name")) ?? "unknown";
            marker = $"[Tool: {toolName}]";
            return true;
        }

        if (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(itemType, "function_call_output", StringComparison.OrdinalIgnoreCase))
        {
            marker = JsonFieldExtractor.ExtractText(item).Trim();
            return !string.IsNullOrWhiteSpace(marker);
        }

        return false;
    }

    private static bool TryExtractImageDataUrl(JsonElement item, out string imageDataUrl)
    {
        imageDataUrl = string.Empty;
        if (!TryGetString(item, "type", out var itemType))
        {
            return false;
        }

        if (!string.Equals(itemType, "input_image", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(itemType, "image", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(itemType, "image_url", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        imageDataUrl = FirstNonEmpty(
            FindString(item, "image_url"),
            FindString(item, "url"),
            FindString(item, "data_url")) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(imageDataUrl);
    }

    private static bool IsAllToolResults(JsonElement messageRoot)
    {
        if (!TryGetProperty(messageRoot, "content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var totalStructuredItems = 0;
        var toolResultItems = 0;

        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            totalStructuredItems++;
            if (TryGetString(item, "type", out var itemType) &&
                (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(itemType, "function_call_output", StringComparison.OrdinalIgnoreCase)))
            {
                toolResultItems++;
            }
        }

        return totalStructuredItems > 0 && toolResultItems == totalStructuredItems;
    }

    private static JsonElement SelectMessageRoot(JsonElement root)
    {
        if (TryGetObject(root, "message", out var message))
        {
            return message;
        }

        if (TryGetObject(root, "payload", out var payload))
        {
            return payload;
        }

        return root;
    }

    private static JsonElement SelectPayload(JsonElement root)
    {
        if (TryGetObject(root, "payload", out var payload))
        {
            return payload;
        }

        return root;
    }

    private static void TryApplySidecarMetadata(
        string sessionDirectory,
        ref string? projectDir,
        ref string? title,
        ref DateTime? createdAt,
        ref DateTime? lastActiveAt)
    {
        // Grok 真实会话是目录结构:chat_history.jsonl 放消息,summary/prompt_context 放元数据。
        TryApplySummaryMetadata(sessionDirectory, ref projectDir, ref title, ref createdAt, ref lastActiveAt);
        TryApplyPromptContextMetadata(sessionDirectory, ref projectDir, ref createdAt);
    }

    private static void TryApplySummaryMetadata(
        string sessionDirectory,
        ref string? projectDir,
        ref string? title,
        ref DateTime? createdAt,
        ref DateTime? lastActiveAt)
    {
        var summaryPath = Path.Combine(sessionDirectory, "summary.json");
        if (!File.Exists(summaryPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(summaryPath, Encoding.UTF8));
            var root = document.RootElement;

            SetIfEmpty(ref title, FirstNonEmpty(
                GetTopLevelString(root, "session_summary"),
                GetTopLevelString(root, "title"),
                GetTopLevelString(root, "summary")));
            SetIfEmpty(ref projectDir, FirstNonEmpty(
                GetTopLevelString(root, "working_directory"),
                GetTopLevelString(root, "git_root_dir"),
                GetTopLevelString(root, "cwd")));

            createdAt ??= FindDateTimeMultiple(root, "created_at", "createdAt", "timestamp");
            lastActiveAt ??= FindDateTimeMultiple(root, "updated_at", "updatedAt", "last_active_at", "lastActiveAt", "created_at", "createdAt");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    private static void TryApplyPromptContextMetadata(string sessionDirectory, ref string? projectDir, ref DateTime? createdAt)
    {
        var promptContextPath = Path.Combine(sessionDirectory, "prompt_context.json");
        if (!File.Exists(promptContextPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(promptContextPath, Encoding.UTF8));
            var root = document.RootElement;

            SetIfPresent(ref projectDir, FirstNonEmpty(
                GetTopLevelString(root, "working_directory"),
                GetTopLevelString(root, "project_dir"),
                GetTopLevelString(root, "cwd")));
            createdAt ??= FindDateTimeMultiple(root, "build_timestamp_utc", "created_at", "createdAt");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        return JsonFieldExtractor.TryGetProperty(element, propertyName, out value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        return JsonFieldExtractor.TryGetString(element, propertyName, out value);
    }

    private static string? FindString(JsonElement root, params string[] propertyNames)
    {
        return JsonFieldExtractor.FindString(root, propertyNames);
    }

    private static bool IsMetadataType(string? eventType)
    {
        return string.Equals(eventType, "session_meta", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "custom-title", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "thread_name_updated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "session_summary", StringComparison.OrdinalIgnoreCase);
    }

    private static string InferRole(string? eventType, JsonElement messageRoot)
    {
        if (TryGetString(messageRoot, "type", out var normalizedType) &&
            !string.IsNullOrWhiteSpace(normalizedType))
        {
            eventType = normalizedType;
        }

        if (string.Equals(eventType, "tool_result", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "function_call_output", StringComparison.OrdinalIgnoreCase))
        {
            return "tool";
        }

        if (string.Equals(eventType, "tool_use", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "function_call", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "assistant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "assistant_message", StringComparison.OrdinalIgnoreCase))
        {
            return "assistant";
        }

        if (string.Equals(eventType, "user", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "human", StringComparison.OrdinalIgnoreCase))
        {
            return "user";
        }

        if (string.Equals(eventType, "developer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "developer";
        }

        if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        return "assistant";
    }

    private static string? TryGetSessionIdFromFilePath(string filePath)
    {
        var sessionDirectory = TryGetGrokSessionDirectory(filePath);
        if (!string.IsNullOrWhiteSpace(sessionDirectory))
        {
            return TryGetSessionIdFromDirectory(sessionDirectory);
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return fileName;
    }

    private static string? TryGetGrokSessionDirectory(string filePath)
    {
        return string.Equals(Path.GetFileName(filePath), "chat_history.jsonl", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(filePath)
            : null;
    }

    private static string? TryGetSessionIdFromDirectory(string sessionDirectory)
    {
        var directoryName = Path.GetFileName(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(directoryName) ? null : directoryName;
    }

    private static string? TryDecodeProjectDirFromSessionDirectory(string sessionDirectory)
    {
        var projectDirectory = Directory.GetParent(sessionDirectory)?.Name;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        try
        {
            var decoded = Uri.UnescapeDataString(projectDirectory);
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }
        catch (UriFormatException)
        {
            return projectDirectory;
        }
    }

    private static string? GetTopLevelString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static void SetIfEmpty(ref string? target, string? value)
    {
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(value))
        {
            target = value;
        }
    }

    private static void SetIfPresent(ref string? target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target = value;
        }
    }

    // Grok 特有的 BuildSessionTitle：只取最后一级目录名
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
}
