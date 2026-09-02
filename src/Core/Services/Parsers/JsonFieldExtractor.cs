using System.Globalization;
using System.Text.Json;

namespace APISwitch.Services.Parsers;

/// <summary>
/// JSON 字段提取工具 - 封装 JSON 解析的常用操作
/// </summary>
public static class JsonFieldExtractor
{
    /// <summary>
    /// 尝试获取对象属性 (大小写不敏感)
    /// </summary>
    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
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

    /// <summary>
    /// 尝试获取字符串属性
    /// </summary>
    public static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (TryGetProperty(element, propertyName, out var candidate) && candidate.ValueKind == JsonValueKind.String)
        {
            value = candidate.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// 深度优先遍历整个 JSON 树,依次产出所有命中给定属性名的值。
    /// 惰性求值:调用方拿到想要的值就停,不会遍历剩余节点。
    /// </summary>
    private static IEnumerable<JsonElement> FindValues(JsonElement root, string[] propertyNames)
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
                        yield return property.Value;
                    }

                    // 命中的值本身可能是容器,仍需继续下钻找同名的嵌套字段。
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
    }

    /// <summary>
    /// 递归查找字符串字段 - 在整个 JSON 树中搜索
    /// </summary>
    public static string? FindString(JsonElement root, params string[] propertyNames)
    {
        foreach (var value in FindValues(root, propertyNames))
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>
    /// 递归查找布尔字段
    /// </summary>
    public static bool? FindBoolean(JsonElement root, params string[] propertyNames)
    {
        foreach (var value in FindValues(root, propertyNames))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }

        return null;
    }

    /// <summary>
    /// 递归查找 DateTime 字段
    /// </summary>
    public static DateTime? FindDateTime(JsonElement root, params string[] propertyNames)
    {
        foreach (var value in FindValues(root, propertyNames))
        {
            var timestamp = ParseDateTime(value);
            if (timestamp.HasValue)
            {
                return timestamp;
            }
        }

        return null;
    }

    /// <summary>
    /// 从 JSON 元素中提取纯文本 - 递归处理各种结构
    /// </summary>
    public static string ExtractText(JsonElement source, string? propertyName = null)
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
                    .Select(item => ExtractText(item))
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Object => ExtractTextFromObject(target),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => target.ToString(),
            _ => string.Empty
        };
    }

    private static string ExtractTextFromObject(JsonElement element)
    {
        // 优先尝试常见的文本字段
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

        // 递归提取嵌套内容
        if (TryGetProperty(element, "content", out var contentElement))
        {
            var content = ExtractText(contentElement);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static DateTime? ParseDateTime(JsonElement value)
    {
        // 字符串格式时间
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // ISO 8601 格式
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                return dto.LocalDateTime;
            }

            // Unix 时间戳字符串
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixFromString))
            {
                return ParseUnixTime(unixFromString);
            }

            return null;
        }

        // 数字格式时间 (Unix 时间戳)
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
            // 根据数值大小判断是秒还是毫秒
            return unix > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix).LocalDateTime
                : DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
