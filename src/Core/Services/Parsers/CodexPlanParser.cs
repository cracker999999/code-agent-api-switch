using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APISwitch.Models;

namespace APISwitch.Services.Parsers;

internal static class CodexPlanParser
{
    // 字符串和注释必须整体跳过，避免把命令正文中的示例当成真实调用，或被正文中的括号截断。
    private static readonly Regex ScriptTokens = new(
        """
        "(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\x60(?:\\.|[^\x60\\])*\x60|(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)|(?<identifier>[A-Za-z_$][\w$]*)|[^\s]
        """,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEnumerable<SessionMessage> Extract(JsonElement payload)
    {
        if (!JsonFieldExtractor.TryGetString(payload, "type", out var type) ||
            !JsonFieldExtractor.TryGetString(payload, "name", out var name))
        {
            yield break;
        }

        if (type == "function_call" && name is "update_plan" or "tools.update_plan" or "functions.update_plan")
        {
            if (JsonFieldExtractor.TryGetString(payload, "arguments", out var arguments) &&
                ParsePlan(arguments) is { } plan)
            {
                yield return plan;
            }

            yield break;
        }

        if (type != "custom_tool_call" || name is not ("exec" or "functions.exec") ||
            !JsonFieldExtractor.TryGetString(payload, "input", out var script) ||
            !script.Contains("update_plan", StringComparison.Ordinal))
        {
            yield break;
        }

        var tokens = ScriptTokens.Matches(script).Where(token => !token.Groups["comment"].Success).ToArray();
        for (var index = 0; index + 4 < tokens.Length; index++)
        {
            if (tokens[index].Value is not ("tools" or "functions") ||
                (index > 0 && tokens[index - 1].Value == ".") ||
                tokens[index + 1].Value != "." || tokens[index + 2].Value != "update_plan" ||
                tokens[index + 3].Value != "(" || tokens[index + 4].Value != "{")
            {
                continue;
            }

            // shortcut: 只提取 JSON 字符串和字面量对象，变量或表达式参数需引入 JS 语法解析后支持。
            var arguments = new StringBuilder();
            var depth = 0;
            for (var end = index + 4; end < tokens.Length; end++)
            {
                var token = tokens[end];
                if (token.Value == "{") depth++;
                if (token.Value == "}") depth--;

                arguments.Append(token.Groups["identifier"].Success &&
                    end + 1 < tokens.Length && tokens[end + 1].Value == ":"
                        ? JsonSerializer.Serialize(token.Value)
                        : token.Value);

                if (depth != 0)
                {
                    continue;
                }

                if (end + 1 < tokens.Length && tokens[end + 1].Value == ")" &&
                    ParsePlan(arguments.ToString()) is { } plan)
                {
                    yield return plan;
                }

                index = end;
                break;
            }
        }
    }

    private static SessionMessage? ParsePlan(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            if (!JsonFieldExtractor.TryGetProperty(root, "plan", out var plan) ||
                plan.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var steps = new List<SessionPlanStep>();
            foreach (var item in plan.EnumerateArray())
            {
                if (!JsonFieldExtractor.TryGetString(item, "step", out var step) ||
                    !JsonFieldExtractor.TryGetString(item, "status", out var status) ||
                    status is not ("pending" or "in_progress" or "completed"))
                {
                    return null;
                }

                steps.Add(new SessionPlanStep(step, status));
            }

            if (JsonFieldExtractor.TryGetProperty(root, "explanation", out var explanation) &&
                explanation.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return null;
            }

            return new SessionMessage
            {
                Role = "assistant",
                Content = explanation.ValueKind == JsonValueKind.String ? explanation.GetString()! : string.Empty,
                Plan = steps
            };
        }
        catch (JsonException)
        {
            // exec 参数可以包含无法静态解析的表达式，单个计划无法提取时仍需加载后续消息。
            return null;
        }
    }
}
