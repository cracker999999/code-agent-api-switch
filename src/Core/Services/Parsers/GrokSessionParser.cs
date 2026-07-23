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

                if (!TryExtractMessage(root, out var message))
                {
                    continue;
                }

                messages.Add(message);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SessionMessage>();
        }

        return messages;
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

        var timestamp = FindDateTimeMultiple(root, "timestamp", "created_at", "createdAt", "last_active_at", "lastActiveAt", "updated_at", "updatedAt")
            ?? DateTime.Now;

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
