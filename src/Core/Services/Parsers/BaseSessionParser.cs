using System.Text.Json;

namespace APISwitch.Services.Parsers;

/// <summary>
/// Session Parser 基类 - 提供通用的解析工具方法
/// </summary>
public abstract class BaseSessionParser
{
    /// <summary>
    /// 标准化标题文本：trim + 截断到 80 字符
    /// </summary>
    protected static string NormalizeTitleText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    /// <summary>
    /// 返回第一个非空字符串
    /// </summary>
    protected static string FirstNonEmpty(params string?[] values)
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

    /// <summary>
    /// 尝试获取 JsonElement 中的对象类型属性
    /// </summary>
    protected static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (JsonFieldExtractor.TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 标准化 role 字段：统一转换为 user/developer/tool/error/assistant
    /// </summary>
    protected static string NormalizeRole(string? role)
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

    /// <summary>
    /// 从多个可能的字段名中查找第一个有效的 DateTime
    /// </summary>
    protected static DateTime? FindDateTimeMultiple(JsonElement root, params string[] propertyNames)
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
}
