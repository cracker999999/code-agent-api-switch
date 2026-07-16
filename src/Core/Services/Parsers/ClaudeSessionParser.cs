using System.Globalization;
using System.Text.Json;
using APISwitch.Models;

namespace APISwitch.Services.Parsers;

/// <summary>
/// Claude 会话解析器 - 封装所有 Claude JSONL 解析逻辑
/// </summary>
public class ClaudeSessionParser : ISessionParser
{
    private const int HeadLineCount = 10;
    private const int TailLineCount = 30;

    public SessionMeta? ParseSession(string filePath)
    {
        var (headLines, tailLines) = SessionFileUtils.ReadHeadAndTailLines(filePath, HeadLineCount, TailLineCount);

        string? sessionId = null;
        string? projectDir = null;
        string? customTitle = null;
        DateTime? createdAt = null;
        DateTime? lastActiveAt = null;
        var hasMessage = false;

        // 从头部行提取基本信息
        foreach (var line in headLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            sessionId ??= JsonFieldExtractor.FindString(root, "sessionId");
            projectDir ??= JsonFieldExtractor.FindString(root, "cwd");
            createdAt ??= JsonFieldExtractor.FindDateTime(root, "timestamp");

            if (!TryExtractMessage(root, out _))
            {
                continue;
            }

            hasMessage = true;
        }

        // 从尾部行提取 customTitle 和最后活跃时间
        for (var index = tailLines.Count - 1; index >= 0; index--)
        {
            var line = tailLines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            // 查找 custom-title 事件
            if (string.IsNullOrWhiteSpace(customTitle) &&
                JsonFieldExtractor.TryGetString(root, "type", out var lineType) &&
                string.Equals(lineType, "custom-title", StringComparison.OrdinalIgnoreCase) &&
                JsonFieldExtractor.TryGetString(root, "customTitle", out var parsedCustomTitle))
            {
                customTitle = parsedCustomTitle;
            }

            customTitle ??= JsonFieldExtractor.FindString(root, "customTitle");

            // 查找最后活跃时间 (多个可能的字段名)
            lastActiveAt ??= FindDateTimeMultiple(root, "last_active_at", "lastActiveAt", "timestamp");

            if (TryExtractMessage(root, out _))
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
            ProviderId = SessionService.ProviderClaude,
            SessionId = sessionId ?? string.Empty,
            Title = title,
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

    /// <summary>
    /// 提取 Claude 消息
    /// </summary>
    private static bool TryExtractMessage(JsonElement root, out SessionMessage message)
    {
        message = new SessionMessage();

        // 跳过元数据行
        if (JsonFieldExtractor.FindBoolean(root, "isMeta") == true)
        {
            return false;
        }

        // 选择消息根节点 (可能在 root 或 root.message 下)
        var messageRoot = SelectMessageRoot(root);
        var role = JsonFieldExtractor.FindString(messageRoot, "role")
            ?? JsonFieldExtractor.FindString(root, "role");

        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var timestamp = FindDateTimeMultiple(root, "timestamp", "created_at", "createdAt", "last_active_at", "lastActiveAt")
            ?? DateTime.Now;

        if (!TryExtractContent(messageRoot, out var content, out var allToolResults))
        {
            return false;
        }

        // 如果是用户消息但全部是工具结果,则标记为 tool 角色
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

    /// <summary>
    /// 选择消息根节点 - Claude 消息可能嵌套在 message 字段下
    /// </summary>
    private static JsonElement SelectMessageRoot(JsonElement root)
    {
        if (TryGetObject(root, "message", out var message))
        {
            return message;
        }

        return root;
    }

    /// <summary>
    /// 提取 Claude 消息内容
    /// </summary>
    private static bool TryExtractContent(JsonElement messageRoot, out string content, out bool allToolResults)
    {
        content = string.Empty;
        allToolResults = false;

        if (!JsonFieldExtractor.TryGetProperty(messageRoot, "content", out var contentElement))
        {
            // 回退到 text 字段
            if (JsonFieldExtractor.TryGetString(messageRoot, "text", out var textValue) && !string.IsNullOrWhiteSpace(textValue))
            {
                content = textValue.Trim();
                return true;
            }

            return false;
        }

        // content 是字符串
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            content = (contentElement.GetString() ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(content);
        }

        // content 必须是数组
        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parts = new List<string>();
        var totalStructuredItems = 0;
        var toolResultItems = 0;

        // 遍历 content 数组
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                totalStructuredItems++;
                var itemType = JsonFieldExtractor.FindString(item, "type");
                if (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase))
                {
                    toolResultItems++;
                }
            }

            var part = ExtractText(item);
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part.Trim());
            }
        }

        allToolResults = totalStructuredItems > 0 && toolResultItems == totalStructuredItems;
        content = string.Join(Environment.NewLine, parts).Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    /// <summary>
    /// 从 JSON 元素中提取文本内容 - 处理各种嵌套结构
    /// </summary>
    private static string ExtractText(JsonElement source, string? propertyName = null)
    {
        JsonElement target = source;
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (!JsonFieldExtractor.TryGetProperty(source, propertyName, out target))
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

    /// <summary>
    /// 从数组项中提取文本
    /// </summary>
    private static string ExtractTextFromArrayItem(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            JsonFieldExtractor.TryGetString(item, "type", out var itemType))
        {
            // tool_use 类型
            if (string.Equals(itemType, "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                var toolName = JsonFieldExtractor.TryGetString(item, "name", out var parsedToolName) ? parsedToolName : "unknown";
                return $"[Tool: {toolName}]";
            }

            // tool_result 类型
            if (string.Equals(itemType, "tool_result", StringComparison.OrdinalIgnoreCase) &&
                JsonFieldExtractor.TryGetProperty(item, "content", out var toolContent))
            {
                return ExtractText(toolContent);
            }
        }

        return ExtractText(item);
    }

    /// <summary>
    /// 从对象中提取文本 - 按优先级尝试多个可能的字段
    /// </summary>
    private static string ExtractTextFromObject(JsonElement element)
    {
        // tool_use 类型特殊处理
        if (JsonFieldExtractor.TryGetString(element, "type", out var elementType) &&
            string.Equals(elementType, "tool_use", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = JsonFieldExtractor.TryGetString(element, "name", out var parsedToolName) ? parsedToolName : "unknown";
            return $"[Tool: {toolName}]";
        }

        // 按优先级尝试各个文本字段
        if (JsonFieldExtractor.TryGetString(element, "text", out var textValue))
        {
            return textValue;
        }

        if (JsonFieldExtractor.TryGetString(element, "input_text", out var inputText))
        {
            return inputText;
        }

        if (JsonFieldExtractor.TryGetString(element, "output_text", out var outputText))
        {
            return outputText;
        }

        // 尝试嵌套的 content 字段
        if (JsonFieldExtractor.TryGetProperty(element, "content", out var contentElement))
        {
            var content = ExtractText(contentElement);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        // 尝试嵌套的 output 字段
        if (JsonFieldExtractor.TryGetProperty(element, "output", out var outputElement))
        {
            var output = ExtractText(outputElement);
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }
        }

        return string.Empty;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (JsonFieldExtractor.TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 从多个可能的字段名中查找 DateTime
    /// </summary>
    private static DateTime? FindDateTimeMultiple(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var result = JsonFieldExtractor.FindDateTime(root, name);
            if (result.HasValue)
            {
                return result;
            }
        }

        return null;
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

        if (string.Equals(role, "error", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        return "assistant";
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
}
