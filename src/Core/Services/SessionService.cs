using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using APISwitch.Models;

namespace APISwitch.Services;

public class SessionService
{
    public const string ProviderCodex = "codex";
    public const string ProviderClaude = "claude";

    private const int HeadLineCount = 10;
    private const int TailLineCount = 30;

    private static readonly Regex UuidPattern = new(
        @"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
        RegexOptions.Compiled);
    private static readonly Regex CodexImageOpenTagPattern = new(
        @"^\s*<image\b[^>]*>\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodexImageCloseTagPattern = new(
        @"^\s*</image>\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _codexSessionsDirectory;
    private readonly string _claudeProjectsDirectory;

    public SessionService(string? codexSessionsDirectory = null, string? claudeProjectsDirectory = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _codexSessionsDirectory = codexSessionsDirectory ?? Path.Combine(userProfile, ".codex", "sessions");
        _claudeProjectsDirectory = claudeProjectsDirectory ?? Path.Combine(userProfile, ".claude", "projects");
    }

    public List<SessionMeta> ScanCodexSessions()
    {
        if (!Directory.Exists(_codexSessionsDirectory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(_codexSessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var session = ParseCodexSession(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMeta> ScanClaudeSessions()
    {
        if (!Directory.Exists(_claudeProjectsDirectory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(_claudeProjectsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var session = ParseClaudeSession(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMessage> LoadMessages(string providerId, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return new List<SessionMessage>();
        }

        if (string.Equals(providerId, ProviderCodex, StringComparison.OrdinalIgnoreCase))
        {
            return ParseCodexMessages(sourcePath);
        }

        if (string.Equals(providerId, ProviderClaude, StringComparison.OrdinalIgnoreCase))
        {
            return ParseClaudeMessages(sourcePath);
        }

        return new List<SessionMessage>();
    }

    public void DeleteSession(string providerId, string sessionId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }

        if (!string.Equals(providerId, ProviderClaude, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return;
        }

        var sidecarDirectory = Path.Combine(sourceDirectory, Path.GetFileNameWithoutExtension(sourcePath));
        if (Directory.Exists(sidecarDirectory))
        {
            Directory.Delete(sidecarDirectory, recursive: true);
        }
    }

    private SessionMeta? ParseCodexSession(string filePath)
    {
        var (headLines, tailLines) = SessionFileUtils.ReadHeadAndTailLines(filePath, HeadLineCount, TailLineCount);

        string? sessionId = null;
        string? projectDir = null;
        string? customTitle = null;
        DateTime? createdAt = null;
        DateTime? lastActiveAt = null;
        var hasMessage = false;

        foreach (var line in headLines.Concat(tailLines))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!TryGetString(root, "type", out var eventType))
            {
                continue;
            }

            if (string.Equals(eventType, "session_meta", StringComparison.OrdinalIgnoreCase) &&
                TryGetObject(root, "payload", out var metaPayload))
            {
                if (TryGetString(metaPayload, "id", out var parsedSessionId))
                {
                    sessionId = parsedSessionId;
                }

                if (TryGetString(metaPayload, "cwd", out var parsedProjectDir))
                {
                    projectDir = parsedProjectDir;
                }

                createdAt ??= TryGetDateTime(metaPayload, "timestamp") ?? TryGetDateTime(root, "timestamp");
            }
            else if (string.Equals(eventType, "event_msg", StringComparison.OrdinalIgnoreCase) &&
                     TryGetObject(root, "payload", out var eventPayload) &&
                     TryGetString(eventPayload, "type", out var payloadType) &&
                     string.Equals(payloadType, "thread_name_updated", StringComparison.OrdinalIgnoreCase) &&
                     TryGetString(eventPayload, "thread_name", out var threadName))
            {
                customTitle = threadName;
            }
            else if (string.Equals(eventType, "response_item", StringComparison.OrdinalIgnoreCase) &&
                     TryGetObject(root, "payload", out var responsePayload) &&
                     TryExtractCodexMessage(responsePayload, out _, out _))
            {
                hasMessage = true;
            }

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

        projectDir ??= string.Empty;
        var fallbackTime = File.GetLastWriteTime(filePath);
        var resolvedCreatedAt = createdAt ?? lastActiveAt ?? fallbackTime;
        var resolvedLastActiveAt = lastActiveAt ?? resolvedCreatedAt;

        return new SessionMeta
        {
            ProviderId = ProviderCodex,
            SessionId = sessionId ?? string.Empty,
            Title = FirstNonEmpty(
                NormalizeTitleText(customTitle),
                BuildSessionTitle(projectDir, string.Empty)),
            ProjectDir = projectDir,
            CreatedAt = resolvedCreatedAt,
            LastActiveAt = resolvedLastActiveAt,
            SourcePath = Path.GetFullPath(filePath)
        };
    }

    private SessionMeta? ParseClaudeSession(string filePath)
    {
        var (headLines, tailLines) = SessionFileUtils.ReadHeadAndTailLines(filePath, HeadLineCount, TailLineCount);

        string? sessionId = null;
        string? projectDir = null;
        string? customTitle = null;
        DateTime? createdAt = null;
        DateTime? lastActiveAt = null;
        var hasMessage = false;

        foreach (var line in headLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            sessionId ??= FindStringRecursive(root, "sessionId");
            projectDir ??= FindStringRecursive(root, "cwd");
            createdAt ??= FindDateTimeRecursive(root, "timestamp");

            if (!TryExtractClaudeMessage(root, out _))
            {
                continue;
            }

            hasMessage = true;
        }

        for (var index = tailLines.Count - 1; index >= 0; index--)
        {
            var line = tailLines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (string.IsNullOrWhiteSpace(customTitle) &&
                TryGetString(root, "type", out var lineType) &&
                string.Equals(lineType, "custom-title", StringComparison.OrdinalIgnoreCase) &&
                TryGetString(root, "customTitle", out var parsedCustomTitle))
            {
                customTitle = parsedCustomTitle;
            }

            customTitle ??= FindStringRecursive(root, "customTitle");
            lastActiveAt ??= FindDateTimeRecursive(root, "last_active_at", "lastActiveAt", "timestamp");

            if (TryExtractClaudeMessage(root, out _))
            {
                hasMessage = true;
            }
        }

        if (!hasMessage)
        {
            return null;
        }

        projectDir ??= string.Empty;
        var fallbackTime = File.GetLastWriteTime(filePath);
        var resolvedCreatedAt = createdAt ?? lastActiveAt ?? fallbackTime;
        var resolvedLastActiveAt = lastActiveAt ?? resolvedCreatedAt;
        var title = FirstNonEmpty(
            NormalizeTitleText(customTitle),
            BuildSessionTitle(projectDir, string.Empty));

        return new SessionMeta
        {
            ProviderId = ProviderClaude,
            SessionId = sessionId ?? string.Empty,
            Title = title,
            ProjectDir = projectDir,
            CreatedAt = resolvedCreatedAt,
            LastActiveAt = resolvedLastActiveAt,
            SourcePath = Path.GetFullPath(filePath)
        };
    }

    private List<SessionMessage> ParseCodexMessages(string sourcePath)
    {
        var messages = new List<SessionMessage>();

        try
        {
            foreach (var line in SessionFileUtils.ReadAllLinesShared(sourcePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetString(root, "type", out var eventType) ||
                    !string.Equals(eventType, "response_item", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetObject(root, "payload", out var payload) ||
                    !TryExtractCodexMessage(payload, out var role, out var content, out var imageDataUrls))
                {
                    continue;
                }

                var timestamp = TryGetDateTime(payload, "timestamp")
                    ?? TryGetDateTime(root, "timestamp")
                    ?? File.GetLastWriteTime(sourcePath);

                messages.Add(new SessionMessage
                {
                    Role = NormalizeRole(role),
                    Content = content,
                    ImageDataUrls = imageDataUrls,
                    Timestamp = timestamp
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SessionMessage>();
        }

        return messages;
    }

    private List<SessionMessage> ParseClaudeMessages(string sourcePath)
    {
        var messages = new List<SessionMessage>();

        try
        {
            foreach (var line in SessionFileUtils.ReadAllLinesShared(sourcePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (TryExtractClaudeMessage(document.RootElement, out var message))
                {
                    messages.Add(message);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SessionMessage>();
        }

        return messages;
    }

    private static bool TryExtractCodexMessage(JsonElement payload, out string role, out string content)
    {
        return TryExtractCodexMessage(payload, out role, out content, out _);
    }

    private static bool TryExtractCodexMessage(
        JsonElement payload,
        out string role,
        out string content,
        out List<string> imageDataUrls)
    {
        role = string.Empty;
        content = string.Empty;
        imageDataUrls = new List<string>();

        if (!TryGetString(payload, "type", out var payloadType))
        {
            return false;
        }

        if (string.Equals(payloadType, "message", StringComparison.OrdinalIgnoreCase))
        {
            role = TryGetString(payload, "role", out var parsedRole) ? parsedRole : "assistant";
            if (TryExtractCodexMessageContent(payload, out content, out imageDataUrls))
            {
                return !string.IsNullOrWhiteSpace(content) || imageDataUrls.Count > 0;
            }

            imageDataUrls = ExtractCodexInputImageDataUrls(payload);
            content = ExtractJsonText(payload, "content");
            return !string.IsNullOrWhiteSpace(content) || imageDataUrls.Count > 0;
        }

        if (string.Equals(payloadType, "function_call", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = TryGetString(payload, "name", out var parsedToolName) ? parsedToolName : "unknown";
            role = "assistant";
            content = $"[Tool: {toolName}]";
            return true;
        }

        if (string.Equals(payloadType, "function_call_output", StringComparison.OrdinalIgnoreCase))
        {
            role = "tool";
            content = ExtractJsonText(payload, "output");
            return !string.IsNullOrWhiteSpace(content);
        }

        return false;
    }

    private static bool TryExtractCodexMessageContent(JsonElement payload, out string content, out List<string> imageDataUrls)
    {
        content = string.Empty;
        imageDataUrls = new List<string>();

        if (!TryGetProperty(payload, "content", out var contentElement))
        {
            return false;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            content = ExtractJsonText(payload, "content");
            imageDataUrls = ExtractCodexInputImageDataUrls(payload);
            return true;
        }

        var items = contentElement.EnumerateArray().ToList();
        var textParts = new List<string>();
        for (var index = 0; index < items.Count; index++)
        {
            if (TryMatchCodexWrappedInputImage(items, index, out var wrappedImageUrl))
            {
                imageDataUrls.Add(wrappedImageUrl);
                index += 2;
                continue;
            }

            if (TryExtractCodexInputImageDataUrl(items[index], out var standaloneImageUrl))
            {
                imageDataUrls.Add(standaloneImageUrl);
                continue;
            }

            var part = ExtractJsonText(items[index]);
            if (!string.IsNullOrWhiteSpace(part))
            {
                textParts.Add(part.Trim());
            }
        }

        content = string.Join(Environment.NewLine, textParts).Trim();
        return true;
    }

    private static bool TryMatchCodexWrappedInputImage(IReadOnlyList<JsonElement> items, int startIndex, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (startIndex + 2 >= items.Count)
        {
            return false;
        }

        if (!TryExtractCodexInputText(items[startIndex], out var openTagText) ||
            !CodexImageOpenTagPattern.IsMatch(openTagText))
        {
            return false;
        }

        if (!TryExtractCodexInputImageDataUrl(items[startIndex + 1], out imageUrl))
        {
            return false;
        }

        if (!TryExtractCodexInputText(items[startIndex + 2], out var closeTagText) ||
            !CodexImageCloseTagPattern.IsMatch(closeTagText))
        {
            imageUrl = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryExtractCodexInputText(JsonElement item, out string text)
    {
        text = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
               TryGetString(item, "type", out var itemType) &&
               string.Equals(itemType, "input_text", StringComparison.OrdinalIgnoreCase) &&
               TryGetString(item, "text", out text);
    }

    private static bool TryExtractCodexInputImageDataUrl(JsonElement item, out string imageUrl)
    {
        imageUrl = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
               TryGetString(item, "type", out var itemType) &&
               string.Equals(itemType, "input_image", StringComparison.OrdinalIgnoreCase) &&
               TryGetString(item, "image_url", out imageUrl);
    }

    private static List<string> ExtractCodexInputImageDataUrls(JsonElement payload)
    {
        var imageDataUrls = new List<string>();

        if (!TryGetProperty(payload, "content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.Array)
        {
            return imageDataUrls;
        }

        foreach (var item in contentElement.EnumerateArray())
        {
            if (!TryExtractCodexInputImageDataUrl(item, out var imageUrl))
            {
                continue;
            }

            imageDataUrls.Add(imageUrl);
        }

        return imageDataUrls;
    }

    private static bool TryExtractClaudeMessage(JsonElement root, out SessionMessage message)
    {
        message = new SessionMessage();

        if (FindBooleanRecursive(root, "isMeta") == true)
        {
            return false;
        }

        var messageRoot = SelectClaudeMessageRoot(root);
        var role = FindStringRecursive(messageRoot, "role")
            ?? FindStringRecursive(root, "role");

        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var timestamp = FindDateTimeRecursive(root, "timestamp", "created_at", "createdAt", "last_active_at", "lastActiveAt")
            ?? DateTime.Now;

        if (!TryExtractClaudeContent(messageRoot, out var content, out var allToolResults))
        {
            return false;
        }

        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) && allToolResults)
        {
            role = "tool";
        }

        message = new SessionMessage
        {
            Role = NormalizeRole(role),
            Content = content,
            Timestamp = timestamp
        };

        return true;
    }

    private static JsonElement SelectClaudeMessageRoot(JsonElement root)
    {
        if (TryGetObject(root, "message", out var message))
        {
            return message;
        }

        return root;
    }

    private static bool TryExtractClaudeContent(JsonElement messageRoot, out string content, out bool allToolResults)
    {
        content = string.Empty;
        allToolResults = false;

        if (!TryGetProperty(messageRoot, "content", out var contentElement))
        {
            if (TryGetString(messageRoot, "text", out var textValue) && !string.IsNullOrWhiteSpace(textValue))
            {
                content = textValue.Trim();
                return true;
            }

            return false;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            content = (contentElement.GetString() ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(content);
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parts = new List<string>();
        var totalStructuredItems = 0;
        var toolResultItems = 0;

        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                totalStructuredItems++;
                var itemType = FindStringRecursive(item, "type");
                if (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase))
                {
                    toolResultItems++;
                }
            }

            var part = ExtractJsonText(item);
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part.Trim());
            }
        }

        allToolResults = totalStructuredItems > 0 && toolResultItems == totalStructuredItems;
        content = string.Join(Environment.NewLine, parts).Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static string ExtractJsonText(JsonElement source, string? propertyName = null)
    {
        JsonElement target = source;
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (!TryGetProperty(source, propertyName, out target))
            {
                return string.Empty;
            }
        }

        return target.ValueKind switch
        {
            JsonValueKind.String => target.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                target.EnumerateArray()
                    .Select(ExtractTextFromArrayItem)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Object => ExtractTextFromObject(target),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => target.ToString(),
            _ => string.Empty
        };
    }

    private static string ExtractTextFromArrayItem(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            TryGetString(item, "type", out var itemType))
        {
            if (string.Equals(itemType, "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                var toolName = TryGetString(item, "name", out var parsedToolName) ? parsedToolName : "unknown";
                return $"[Tool: {toolName}]";
            }

            if (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase) &&
                TryGetProperty(item, "content", out var toolContent))
            {
                return ExtractJsonText(toolContent);
            }
        }

        return ExtractJsonText(item);
    }

    private static string ExtractTextFromObject(JsonElement element)
    {
        if (TryGetString(element, "type", out var elementType) &&
            string.Equals(elementType, "tool_use", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = TryGetString(element, "name", out var parsedToolName) ? parsedToolName : "unknown";
            return $"[Tool: {toolName}]";
        }

        if (TryGetString(element, "text", out var textValue))
        {
            return textValue;
        }

        if (TryGetString(element, "input_text", out var inputText))
        {
            return inputText;
        }

        if (TryGetString(element, "output_text", out var outputText))
        {
            return outputText;
        }

        if (TryGetProperty(element, "content", out var contentElement))
        {
            var content = ExtractJsonText(contentElement);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        if (TryGetProperty(element, "output", out var outputElement))
        {
            var output = ExtractJsonText(outputElement);
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }
        }

        return string.Empty;
    }

    private static bool ShouldSkipAsClaudeTitle(string content)
    {
        var trimmed = content.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return true;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmed.Contains("<local-command-caveat>", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.StartsWith("<command-name>", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

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

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string? ExtractUuidFromFileName(string filePath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var match = UuidPattern.Match(fileNameWithoutExtension);
        return match.Success ? match.Value : null;
    }

    private static DateTime? TryGetDateTime(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return ParseDateTime(value);
    }

    private static DateTime? FindDateTimeRecursive(JsonElement root, params string[] propertyNames)
    {
        var names = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<JsonElement>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    if (names.Contains(property.Name))
                    {
                        var timestamp = ParseDateTime(property.Value);
                        if (timestamp.HasValue)
                        {
                            return timestamp;
                        }
                    }

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(property.Value);
                    }
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(item);
                    }
                }
            }
        }

        return null;
    }

    private static bool? FindBooleanRecursive(JsonElement root, params string[] propertyNames)
    {
        var names = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<JsonElement>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    if (names.Contains(property.Name) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        return property.Value.GetBoolean();
                    }

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(property.Value);
                    }
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(item);
                    }
                }
            }
        }

        return null;
    }

    private static string? FindStringRecursive(JsonElement root, params string[] propertyNames)
    {
        var names = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<JsonElement>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    if (names.Contains(property.Name) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(property.Value);
                    }
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        stack.Push(item);
                    }
                }
            }
        }

        return null;
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

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (TryGetProperty(element, propertyName, out var candidate) && candidate.ValueKind == JsonValueKind.String)
        {
            value = candidate.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRole(string role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return "user";
        }

        if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
        {
            return "developer";
        }

        if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return "tool";
        }

        return "assistant";
    }
}
