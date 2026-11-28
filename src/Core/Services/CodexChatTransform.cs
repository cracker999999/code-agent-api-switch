using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace APISwitch.Services;

internal sealed class CodexToolContext
{
    internal enum ToolKind { Function, Namespace, Custom, ToolSearch }

    internal sealed record ToolSpec(ToolKind Kind, string Name, string? Namespace);

    private readonly HashSet<string> _seenNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolSpec> _specs = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Namespace, string Name), string> _namespaceNames = new();
    internal List<JsonObject> ChatTools { get; } = [];

    internal ToolSpec? Lookup(string name) => _specs.GetValueOrDefault(name);
    internal bool IsCustom(string name) => Lookup(name)?.Kind == ToolKind.Custom;

    internal string ChatNameForResponseFunction(string name, string? namespaceName)
    {
        if (!string.IsNullOrWhiteSpace(namespaceName) &&
            _namespaceNames.TryGetValue((namespaceName, name), out var chatName))
        {
            return chatName;
        }

        return string.IsNullOrWhiteSpace(namespaceName) ? name : CodexChatTransform.FlattenNamespaceToolName(namespaceName, name);
    }

    internal void AddResponseTool(JsonNode? tool)
    {
        if (tool is JsonValue value && value.TryGetValue<string>(out var stringName))
        {
            AddCustom(stringName, tool);
            return;
        }

        if (tool is not JsonObject obj)
        {
            return;
        }

        switch (GetString(obj, "type"))
        {
            case "function":
                AddFunction(obj, null);
                break;
            case "custom":
                AddCustom(GetString(obj, "name"), obj);
                break;
            case "tool_search":
                AddToolSearch();
                break;
            case "namespace":
                var namespaceName = GetString(obj, "name");
                var children = obj["tools"] as JsonArray ?? obj["children"] as JsonArray;
                if (children is not null)
                {
                    foreach (var child in children)
                    {
                        if (child is JsonObject childObject && GetString(childObject, "type") == "function")
                        {
                            AddFunction(childObject, namespaceName);
                        }
                    }
                }
                break;
        }
    }

    internal void AddToolsFromInput(JsonNode? value)
    {
        if (value is JsonArray array)
        {
            foreach (var item in array) AddToolsFromInput(item);
            return;
        }

        if (value is not JsonObject obj) return;
        if (GetString(obj, "type") == "tool_search_output" && obj["tools"] is JsonArray tools)
        {
            foreach (var tool in tools) AddResponseTool(tool);
        }

        foreach (var child in obj) AddToolsFromInput(child.Value);
    }

    private void AddFunction(JsonObject tool, string? namespaceName)
    {
        var originalName = GetString(tool["function"] as JsonObject ?? tool, "name").Trim();
        if (originalName.Length == 0) return;
        var chatName = namespaceName is null ? originalName : CodexChatTransform.FlattenNamespaceToolName(namespaceName, originalName);
        if (!_seenNames.Add(chatName)) return;

        var function = (tool["function"] as JsonObject)?.DeepClone()?.AsObject() ?? new JsonObject
        {
            ["name"] = chatName,
            ["description"] = Clone(tool["description"]),
            ["parameters"] = NormalizeParameters(tool["parameters"])
        };
        function["name"] = chatName;
        function["parameters"] = NormalizeParameters(function["parameters"]);
        if (tool["strict"] is not null && function["strict"] is null) function["strict"] = Clone(tool["strict"]);
        ChatTools.Add(new JsonObject { ["type"] = "function", ["function"] = function });
        var kind = namespaceName is null ? ToolKind.Function : ToolKind.Namespace;
        _specs[chatName] = new ToolSpec(kind, originalName, namespaceName);
        if (namespaceName is not null) _namespaceNames[(namespaceName, originalName)] = chatName;
    }

    private void AddCustom(string name, JsonNode? definition)
    {
        name = name.Trim();
        if (name.Length == 0 || !_seenNames.Add(name)) return;
        var description = "Original tool definition:\n```json\n" + (definition?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "{}") + "\n```";
        ChatTools.Add(new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["input"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Raw string input for the original custom tool."
                        }
                    },
                    ["required"] = new JsonArray("input")
                }
            }
        });
        _specs[name] = new ToolSpec(ToolKind.Custom, name, null);
    }

    private void AddToolSearch()
    {
        const string name = "tool_search";
        if (!_seenNames.Add(name)) return;
        ChatTools.Add(new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = "Search and load Codex tools, plugins, connectors, and MCP namespaces for the current task.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string" },
                        ["limit"] = new JsonObject { ["type"] = "integer" }
                    },
                    ["required"] = new JsonArray("query")
                }
            }
        });
        _specs[name] = new ToolSpec(ToolKind.ToolSearch, name, null);
    }

    private static JsonNode NormalizeParameters(JsonNode? value)
    {
        var parameters = value?.DeepClone() as JsonObject ?? new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        };
        if (GetString(parameters, "type") != "object") parameters["type"] = "object";
        return parameters;
    }

    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();
    private static string GetString(JsonObject? obj, string name) => obj?[name]?.GetValue<string>() ?? string.Empty;
    private static string GetString(JsonNode? node, string name) => GetString(node as JsonObject, name);
}

internal sealed record CodexChatRequest(JsonObject Body, CodexToolContext Context, bool Stream);

internal static class CodexChatTransform
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    internal static CodexChatRequest ToChatCompletions(JsonObject body)
    {
        var context = new CodexToolContext();
        if (body["tools"] is JsonArray tools)
        {
            foreach (var tool in tools) context.AddResponseTool(tool);
        }
        context.AddToolsFromInput(body["input"]);

        var result = new JsonObject();
        Copy(body, result, "model");
        var messages = new JsonArray();
        var instructions = InstructionText(body["instructions"]);
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = instructions });
        }

        AppendInput(body["input"], messages, context);
        result["messages"] = CollapseSystemMessages(messages);

        var model = GetString(body, "model");
        if (body["max_output_tokens"] is not null)
        {
            result[IsReasoningModel(model) ? "max_completion_tokens" : "max_tokens"] = Clone(body["max_output_tokens"]);
        }
        foreach (var key in new[] { "max_tokens", "max_completion_tokens", "temperature", "top_p", "stream" }) Copy(body, result, key);
        if (body["reasoning"] is JsonObject reasoning && reasoning["effort"] is not null)
        {
            result["reasoning_effort"] = Clone(reasoning["effort"]);
        }
        if (context.ChatTools.Count > 0)
        {
            var chatTools = new JsonArray();
            foreach (var tool in context.ChatTools) chatTools.Add(tool.DeepClone());
            result["tools"] = chatTools;
        }
        if (body["tool_choice"] is not null)
        {
            result["tool_choice"] = ConvertToolChoice(body["tool_choice"]!, context);
        }
        foreach (var key in new[] { "frequency_penalty", "logit_bias", "logprobs", "metadata", "n", "parallel_tool_calls", "presence_penalty", "response_format", "seed", "service_tier", "stop", "stream_options", "top_logprobs", "user" }) Copy(body, result, key);
        if (result["tools"] is not JsonArray { Count: > 0 })
        {
            result.Remove("tool_choice");
            result.Remove("parallel_tool_calls");
        }
        if (result["stream"]?.GetValue<bool>() == true)
        {
            var streamOptions = result["stream_options"] as JsonObject ?? new JsonObject();
            streamOptions["include_usage"] = true;
            result["stream_options"] = streamOptions;
        }

        return new CodexChatRequest(result, context, result["stream"]?.GetValue<bool>() == true);
    }

    internal static JsonObject ToResponses(JsonObject body, CodexToolContext context)
    {
        var choices = body["choices"] as JsonArray;
        var choice = choices?.FirstOrDefault() as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 choices");
        var message = choice["message"] as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 message");
        var responseId = ResponseId(GetString(body, "id"));
        var finishReason = GetString(choice, "finish_reason");
        var output = new JsonArray();
        var reasoning = ChatReasoningText(message);
        if (!string.IsNullOrEmpty(reasoning))
        {
            output.Add(new JsonObject
            {
                ["id"] = $"rs_{responseId}",
                ["type"] = "reasoning",
                ["summary"] = new JsonArray(new JsonObject { ["type"] = "summary_text", ["text"] = reasoning })
            });
        }
        var messageItem = ChatMessageToResponse(message, responseId);
        if (messageItem is not null) output.Add(messageItem);
        if (message["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var (toolCall, index) in toolCalls.Select((value, index) => (value, index)))
            {
                if (toolCall is JsonObject call && ToolCallToResponse(call, index, reasoning, context) is { } item) output.Add(item);
            }
        }
        else if (message["function_call"] is JsonObject legacy && ToolCallToResponse(legacy, 0, reasoning, context, true) is { } item)
        {
            output.Add(item);
        }

        var response = new JsonObject
        {
            ["id"] = responseId,
            ["object"] = "response",
            ["created_at"] = GetLong(body, "created"),
            ["status"] = finishReason == "length" ? "incomplete" : "completed",
            ["model"] = GetString(body, "model"),
            ["output"] = output,
            ["usage"] = Usage(body["usage"] as JsonObject)
        };
        if (finishReason == "length") response["incomplete_details"] = new JsonObject { ["reason"] = "max_output_tokens" };
        return response;
    }

    internal static string FlattenNamespaceToolName(string namespaceName, string name)
    {
        var full = $"{namespaceName}__{name}";
        if (full.Length <= 64) return full;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).ToLowerInvariant()[..12];
        var suffix = $"__{hash}";
        return full[..Math.Min(full.Length, 64 - suffix.Length)] + suffix;
    }

    internal static string ResponseId(string id) => id.StartsWith("resp_", StringComparison.Ordinal) ? id : $"resp_{(string.IsNullOrWhiteSpace(id) ? "ccswitch" : id)}";

    private static void AppendInput(JsonNode? input, JsonArray messages, CodexToolContext context)
    {
        var items = input as JsonArray;
        if (input is JsonValue value && value.TryGetValue<string>(out var text))
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            return;
        }
        if (items is null)
        {
            if (input is JsonObject obj) AppendItem(obj, messages, context);
            return;
        }
        var pendingReasoning = string.Empty;
        foreach (var item in items)
        {
            if (item is not JsonObject obj) continue;
            if (GetString(obj, "type") == "reasoning")
            {
                pendingReasoning = ExtractReasoning(obj);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pendingReasoning))
            {
                obj = obj.DeepClone().AsObject();
                obj["reasoning_content"] = pendingReasoning;
                pendingReasoning = string.Empty;
            }
            AppendItem(obj, messages, context);
        }

        if (!string.IsNullOrWhiteSpace(pendingReasoning) && messages.LastOrDefault() is JsonObject last && GetString(last, "role") == "assistant")
        {
            last["reasoning_content"] = $"{GetString(last, "reasoning_content")}\n\n{pendingReasoning}".Trim();
        }
    }

    private static void AppendItem(JsonObject item, JsonArray messages, CodexToolContext context)
    {
        var type = GetString(item, "type");
        if (type is "function_call" or "custom_tool_call" or "tool_search_call")
        {
            var callId = GetString(item, "call_id");
            var name = GetString(item, "name");
            if (type == "tool_search_call") name = "tool_search";
            var arguments = type == "custom_tool_call"
                ? Compact(new JsonObject { ["input"] = Clone(item["input"]) })
                : Compact(item["arguments"] ?? new JsonObject());
            var call = new JsonObject
            {
                ["id"] = callId,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = context.ChatNameForResponseFunction(name, GetString(item, "namespace")), ["arguments"] = arguments }
            };
            var assistant = messages.LastOrDefault() as JsonObject;
            if (assistant is null || GetString(assistant, "role") != "assistant")
            {
                assistant = new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = new JsonArray() };
                messages.Add(assistant);
            }
            else if (assistant["tool_calls"] is not JsonArray)
            {
                assistant["tool_calls"] = new JsonArray();
            }
            (assistant["tool_calls"] as JsonArray)!.Add(call);
            if (assistant["reasoning_content"] is null && !string.IsNullOrWhiteSpace(GetString(item, "reasoning_content")))
            {
                assistant["reasoning_content"] = GetString(item, "reasoning_content");
            }
            EnsureToolReasoning(assistant, item);
            return;
        }

        if (type is "function_call_output" or "custom_tool_call_output" or "tool_search_output")
        {
            messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = GetString(item, "call_id"),
                ["content"] = Compact(item["output"] ?? "")
            });
            return;
        }

        if (type == "reasoning") return;
        if (type != "message" && item["role"] is null) return;
        var role = GetString(item, "role") switch
        {
            "system" or "developer" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user"
        };
        var message = new JsonObject { ["role"] = role, ["content"] = ContentToChat(item["content"]) };
        if (role == "assistant" && !string.IsNullOrWhiteSpace(GetString(item, "reasoning_content"))) message["reasoning_content"] = GetString(item, "reasoning_content");
        messages.Add(message);
    }

    private static void EnsureToolReasoning(JsonObject message, JsonObject item)
    {
        if (string.IsNullOrWhiteSpace(GetString(message, "reasoning_content"))) message["reasoning_content"] = GetString(item, "reasoning_content");
        if (string.IsNullOrWhiteSpace(GetString(message, "reasoning_content"))) message["reasoning_content"] = "tool call";
    }

    private static string ExtractReasoning(JsonObject item)
    {
        var direct = GetString(item, "reasoning_content");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        if (item["summary"] is JsonArray summary)
        {
            return string.Join("\n\n", summary.OfType<JsonObject>().Select(part => GetString(part, "text")).Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        return GetString(item, "text");
    }

    private static JsonNode ContentToChat(JsonNode? content)
    {
        if (content is null || content is JsonValue) return Clone(content) ?? null!;
        if (content is not JsonArray parts) return Clone(content)!;
        var output = new JsonArray();
        var hasNonText = false;
        foreach (var part in parts.OfType<JsonObject>())
        {
            switch (GetString(part, "type"))
            {
                case "input_text":
                case "output_text":
                case "text":
                    if (!string.IsNullOrEmpty(GetString(part, "text"))) output.Add(new JsonObject { ["type"] = "text", ["text"] = GetString(part, "text") });
                    break;
                case "refusal":
                    if (!string.IsNullOrEmpty(GetString(part, "refusal"))) output.Add(new JsonObject { ["type"] = "text", ["text"] = GetString(part, "refusal") });
                    break;
                case "input_image":
                    var imageUrl = Clone(part["image_url"]);
                    if (imageUrl is JsonValue imageValue && imageValue.TryGetValue<string>(out var imageText)) imageUrl = new JsonObject { ["url"] = imageText };
                    output.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = imageUrl ?? new JsonObject() });
                    hasNonText = true;
                    break;
                case "input_file":
                    if (part["file"] is JsonObject file) output.Add(new JsonObject { ["type"] = "file", ["file"] = file.DeepClone() });
                    hasNonText = true;
                    break;
                case "input_audio":
                    output.Add(new JsonObject { ["type"] = "input_audio", ["input_audio"] = Clone(part["input_audio"]) });
                    hasNonText = true;
                    break;
            }
        }
        if (!hasNonText) return string.Join("\n", output.OfType<JsonObject>().Select(item => GetString(item, "text")));
        return output;
    }

    private static JsonArray CollapseSystemMessages(JsonArray input)
    {
        var system = new List<string>();
        var rest = new JsonArray();
        foreach (var node in input)
        {
            if (node is JsonObject obj && GetString(obj, "role") == "system" && obj["content"] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                if (!string.IsNullOrWhiteSpace(text)) system.Add(text);
                continue;
            }
            rest.Add(node?.DeepClone());
        }
        var output = new JsonArray();
        if (system.Count > 0) output.Add(new JsonObject { ["role"] = "system", ["content"] = string.Join("\n\n", system) });
        foreach (var node in rest) output.Add(node?.DeepClone());
        return output;
    }

    private static JsonNode ConvertToolChoice(JsonNode node, CodexToolContext context)
    {
        if (node is JsonObject obj)
        {
            var type = GetString(obj, "type");
            if (type == "function") return new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = context.ChatNameForResponseFunction(GetString(obj, "name"), GetString(obj, "namespace")) } };
            if (type == "custom") return new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = GetString(obj, "name") } };
            if (type == "tool_search") return new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "tool_search" } };
        }
        return Clone(node)!;
    }

    private static JsonObject? ChatMessageToResponse(JsonObject message, string responseId)
    {
        var content = new JsonArray();
        if (message["content"] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var answer = SplitThink(text).Answer;
            if (!string.IsNullOrEmpty(answer)) content.Add(new JsonObject { ["type"] = "output_text", ["text"] = answer, ["annotations"] = new JsonArray() });
        }
        else if (message["content"] is JsonArray parts)
        {
            foreach (var part in parts.OfType<JsonObject>())
            {
                if (GetString(part, "type") is "text" or "output_text" && !string.IsNullOrEmpty(GetString(part, "text")))
                    content.Add(new JsonObject { ["type"] = "output_text", ["text"] = GetString(part, "text"), ["annotations"] = new JsonArray() });
                if (GetString(part, "type") == "refusal" && !string.IsNullOrEmpty(GetString(part, "refusal")))
                    content.Add(new JsonObject { ["type"] = "refusal", ["refusal"] = GetString(part, "refusal") });
            }
        }
        if (!string.IsNullOrEmpty(GetString(message, "refusal"))) content.Add(new JsonObject { ["type"] = "refusal", ["refusal"] = GetString(message, "refusal") });
        return content.Count == 0 ? null : new JsonObject
        {
            ["id"] = $"{responseId}_msg",
            ["type"] = "message",
            ["status"] = "completed",
            ["role"] = "assistant",
            ["content"] = content
        };
    }

    private static JsonObject? ToolCallToResponse(JsonObject call, int index, string? reasoning, CodexToolContext context, bool legacy = false)
    {
        var function = legacy ? call : call["function"] as JsonObject;
        var name = GetString(function, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var callId = GetString(call, "id");
        if (string.IsNullOrWhiteSpace(callId)) callId = $"call_{index}";
        var arguments = Compact(function?["arguments"] ?? "{}");
        var itemId = context.IsCustom(name) ? $"ctc_{callId}" : $"fc_{callId}";
        var spec = context.Lookup(name);
        JsonObject item;
        if (spec?.Kind == CodexToolContext.ToolKind.Custom)
        {
            item = new JsonObject { ["id"] = itemId, ["type"] = "custom_tool_call", ["status"] = "completed", ["call_id"] = callId, ["name"] = spec.Name, ["input"] = CustomInput(arguments) };
        }
        else if (spec?.Kind == CodexToolContext.ToolKind.ToolSearch)
        {
            item = new JsonObject { ["type"] = "tool_search_call", ["call_id"] = callId, ["status"] = "completed", ["execution"] = "client", ["arguments"] = ParseObject(arguments) };
        }
        else
        {
            item = new JsonObject { ["id"] = itemId, ["type"] = "function_call", ["status"] = "completed", ["call_id"] = callId, ["name"] = spec?.Name ?? name, ["arguments"] = arguments };
            if (!string.IsNullOrWhiteSpace(spec?.Namespace)) item["namespace"] = spec.Namespace;
        }
        if (!string.IsNullOrWhiteSpace(reasoning)) item["reasoning_content"] = reasoning;
        return item;
    }

    private static JsonObject Usage(JsonObject? usage)
    {
        var input = GetLong(usage, "prompt_tokens", "input_tokens");
        var output = GetLong(usage, "completion_tokens", "output_tokens");
        var total = GetLong(usage, "total_tokens");
        if (total == 0) total = input + output;
        var cached = usage?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? usage?["input_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? usage?["cache_read_input_tokens"]?.GetValue<long>() ?? 0;
        return new JsonObject
        {
            ["input_tokens"] = input,
            ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = cached },
            ["output_tokens"] = output,
            ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = usage?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<long>() ?? 0 },
            ["total_tokens"] = total
        };
    }

    internal static string Compact(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString(JsonOptions);
    internal static string CustomInput(string arguments)
    {
        try
        {
            var obj = JsonNode.Parse(arguments)?.AsObject();
            return obj?["input"]?.GetValue<string>() ?? arguments;
        }
        catch (JsonException) { return arguments; }
    }
    internal static JsonNode ParseObject(string value)
    {
        try { return JsonNode.Parse(value) as JsonObject ?? new JsonObject { ["query"] = value }; }
        catch (JsonException) { return new JsonObject { ["query"] = value }; }
    }
    private static string ChatReasoningText(JsonObject message)
    {
        var direct = GetString(message, "reasoning_content");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        return SplitThink(GetString(message, "content")).Reasoning;
    }
    internal static (string Reasoning, string Answer) SplitThink(string text)
    {
        var start = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return (string.Empty, text);
        var end = text.IndexOf("</think>", start + 7, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return (text[(start + 7)..].Trim(), string.Empty);
        return (text[(start + 7)..end].Trim(), text[(end + 8)..].TrimStart());
    }
    private static string InstructionText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node is not JsonArray array) return string.Empty;
        return string.Join(
            "\n\n",
            array.Select(item => item is JsonValue itemValue && itemValue.TryGetValue<string>(out var itemText)
                ? itemText
                : GetString(item as JsonObject, "text"))
                .Where(item => !string.IsNullOrEmpty(item)));
    }
    private static bool IsReasoningModel(string model) => model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
    private static void Copy(JsonObject source, JsonObject target, string key) { if (source[key] is not null) target[key] = Clone(source[key]); }
    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();
    private static string GetString(JsonObject? obj, string key) => obj?[key]?.GetValue<string>() ?? string.Empty;
    private static long GetLong(JsonObject? obj, params string[] keys) { foreach (var key in keys) if (obj?[key] is JsonValue value && value.TryGetValue<long>(out var result)) return result; return 0; }
}
