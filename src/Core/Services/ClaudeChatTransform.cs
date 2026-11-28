using System.Text.Json;
using System.Text.Json.Nodes;

namespace APISwitch.Services;

internal sealed record ClaudeChatRequest(JsonObject Body, bool Stream, string Model);

internal static class ClaudeChatTransform
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    internal static ClaudeChatRequest ToChatCompletions(JsonObject body)
    {
        var model = RequireString(body, "model", "Claude 请求缺少 model");
        if (body["messages"] is not JsonArray sourceMessages)
        {
            throw new InvalidOperationException("Claude 请求缺少 messages");
        }

        var result = new JsonObject
        {
            ["model"] = model
        };
        var messages = new JsonArray();
        var system = ReadTextContent(body["system"]);
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = system
            });
        }

        foreach (var sourceMessage in sourceMessages.OfType<JsonObject>())
        {
            AppendMessage(sourceMessage, messages);
        }
        result["messages"] = messages;

        foreach (var name in new[] { "max_tokens", "temperature", "top_p", "stream", "service_tier" })
        {
            Copy(body, result, name);
        }
        if (body["stop_sequences"] is JsonArray { Count: > 0 } stopSequences)
        {
            result["stop"] = stopSequences.DeepClone();
        }

        if (body["tools"] is JsonArray tools && tools.Count > 0)
        {
            result["tools"] = ConvertTools(tools);
            if (body["tool_choice"] is JsonObject toolChoice)
            {
                result["tool_choice"] = ConvertToolChoice(toolChoice);
                if (GetBoolean(toolChoice, "disable_parallel_tool_use"))
                {
                    result["parallel_tool_calls"] = false;
                }
            }
        }

        var stream = GetBoolean(body, "stream");
        if (stream)
        {
            result["stream_options"] = new JsonObject { ["include_usage"] = true };
        }
        return new ClaudeChatRequest(result, stream, model);
    }

    internal static JsonObject ToMessages(JsonObject body, string fallbackModel)
    {
        var choices = body["choices"] as JsonArray;
        var choice = choices?.FirstOrDefault() as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 choices");
        var message = choice["message"] as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 message");
        var content = new JsonArray();

        AppendResponseText(message["content"], content);
        if (message["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls.OfType<JsonObject>())
            {
                content.Add(ToolCallToClaude(call));
            }
        }
        else if (message["function_call"] is JsonObject legacyCall)
        {
            content.Add(ToolCallToClaude(legacyCall, legacy: true));
        }

        return new JsonObject
        {
            ["id"] = MessageId(GetString(body, "id")),
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = Coalesce(GetString(body, "model"), fallbackModel),
            ["content"] = content,
            ["stop_reason"] = StopReason(GetString(choice, "finish_reason"), content),
            ["stop_sequence"] = null,
            ["usage"] = Usage(body["usage"] as JsonObject)
        };
    }

    private static void AppendMessage(JsonObject source, JsonArray messages)
    {
        var role = RequireString(source, "role", "Claude 消息缺少 role");
        if (role == "system")
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = ReadTextContent(source["content"])
            });
            return;
        }
        if (role == "user")
        {
            AppendUserMessage(source["content"], messages);
            return;
        }
        if (role == "assistant")
        {
            AppendAssistantMessage(source["content"], messages);
            return;
        }

        throw new InvalidOperationException($"不支持 Claude 消息角色：{role}");
    }

    private static void AppendUserMessage(JsonNode? content, JsonArray messages)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            return;
        }
        if (content is not JsonArray parts)
        {
            throw new InvalidOperationException("Claude user 消息 content 必须是字符串或数组");
        }

        var chatParts = new JsonArray();
        foreach (var part in parts.OfType<JsonObject>())
        {
            switch (GetString(part, "type"))
            {
                case "tool_result":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = RequireString(part, "tool_use_id", "tool_result 缺少 tool_use_id"),
                        ["content"] = ToolResultText(part)
                    });
                    break;
                case "text":
                    chatParts.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = GetString(part, "text")
                    });
                    break;
                case "image":
                    chatParts.Add(ImageToChat(part));
                    break;
                case "document":
                    chatParts.Add(DocumentToChat(part));
                    break;
                default:
                    throw new InvalidOperationException($"不支持 Claude user 内容块：{GetString(part, "type")}");
            }
        }

        if (chatParts.Count > 0)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = SimplifyTextParts(chatParts)
            });
        }
    }

    private static void AppendAssistantMessage(JsonNode? content, JsonArray messages)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = text });
            return;
        }
        if (content is not JsonArray parts)
        {
            throw new InvalidOperationException("Claude assistant 消息 content 必须是字符串或数组");
        }

        var textParts = new List<string>();
        var toolCalls = new JsonArray();
        foreach (var part in parts.OfType<JsonObject>())
        {
            switch (GetString(part, "type"))
            {
                case "text":
                    textParts.Add(GetString(part, "text"));
                    break;
                case "tool_use":
                    var input = part["input"] as JsonObject
                        ?? throw new InvalidOperationException("tool_use.input 必须是 JSON 对象");
                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = RequireString(part, "id", "tool_use 缺少 id"),
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = RequireString(part, "name", "tool_use 缺少 name"),
                            ["arguments"] = input.ToJsonString(JsonOptions)
                        }
                    });
                    break;
                case "thinking":
                case "redacted_thinking":
                    // Chat Completions 没有可回传 Anthropic 签名思考块的标准字段。
                    break;
                default:
                    throw new InvalidOperationException($"不支持 Claude assistant 内容块：{GetString(part, "type")}");
            }
        }

        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = textParts.Count == 0 ? null : string.Concat(textParts)
        };
        if (toolCalls.Count > 0)
        {
            message["tool_calls"] = toolCalls;
        }
        messages.Add(message);
    }

    private static JsonArray ConvertTools(JsonArray tools)
    {
        var result = new JsonArray();
        foreach (var tool in tools.OfType<JsonObject>())
        {
            var inputSchema = tool["input_schema"] as JsonObject
                ?? throw new InvalidOperationException("Claude 工具缺少 input_schema");
            var function = new JsonObject
            {
                ["name"] = RequireString(tool, "name", "Claude 工具缺少 name"),
                ["parameters"] = inputSchema.DeepClone()
            };
            if (!string.IsNullOrWhiteSpace(GetString(tool, "description")))
            {
                function["description"] = GetString(tool, "description");
            }
            if (tool["strict"] is not null)
            {
                function["strict"] = tool["strict"]!.DeepClone();
            }

            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = function
            });
        }
        return result;
    }

    private static JsonNode ConvertToolChoice(JsonObject toolChoice)
    {
        return GetString(toolChoice, "type") switch
        {
            "auto" => "auto",
            "any" => "required",
            "none" => "none",
            "tool" => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = RequireString(toolChoice, "name", "tool_choice.tool 缺少 name")
                }
            },
            var type => throw new InvalidOperationException($"不支持 Claude tool_choice：{type}")
        };
    }

    private static JsonObject ImageToChat(JsonObject part)
    {
        var source = part["source"] as JsonObject
            ?? throw new InvalidOperationException("Claude image 缺少 source");
        var url = GetString(source, "type") switch
        {
            "base64" => $"data:{RequireString(source, "media_type", "base64 image 缺少 media_type")};base64,{RequireString(source, "data", "base64 image 缺少 data")}",
            "url" => RequireString(source, "url", "url image 缺少 url"),
            var type => throw new InvalidOperationException($"不支持 Claude image source：{type}")
        };
        return new JsonObject
        {
            ["type"] = "image_url",
            ["image_url"] = new JsonObject { ["url"] = url }
        };
    }

    private static JsonObject DocumentToChat(JsonObject part)
    {
        var source = part["source"] as JsonObject
            ?? throw new InvalidOperationException("Claude document 缺少 source");
        if (GetString(source, "type") != "text")
        {
            throw new InvalidOperationException("Chat Completions 适配仅支持 text 类型的 Claude document");
        }

        return new JsonObject
        {
            ["type"] = "text",
            ["text"] = RequireString(source, "data", "text document 缺少 data")
        };
    }

    private static string ToolResultText(JsonObject part)
    {
        var text = ReadTextContent(part["content"]);
        return GetBoolean(part, "is_error") ? $"[tool_error]\n{text}" : text;
    }

    private static JsonNode SimplifyTextParts(JsonArray parts)
    {
        if (parts.All(part => part is JsonObject obj && GetString(obj, "type") == "text"))
        {
            return string.Concat(parts.OfType<JsonObject>().Select(part => GetString(part, "text")));
        }
        return parts;
    }

    internal static string ReadTextContent(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (content is not JsonArray parts)
        {
            return content?.ToJsonString(JsonOptions) ?? string.Empty;
        }

        return string.Concat(parts.OfType<JsonObject>().Select(part =>
            GetString(part, "type") == "text"
                ? GetString(part, "text")
                : part.ToJsonString(JsonOptions)));
    }

    private static void AppendResponseText(JsonNode? source, JsonArray content)
    {
        if (source is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (!string.IsNullOrEmpty(text))
            {
                content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
            }
            return;
        }
        if (source is not JsonArray parts)
        {
            return;
        }

        foreach (var part in parts.OfType<JsonObject>())
        {
            var partText = GetString(part, "type") == "refusal"
                ? GetString(part, "refusal")
                : GetString(part, "text");
            if (!string.IsNullOrEmpty(partText))
            {
                content.Add(new JsonObject { ["type"] = "text", ["text"] = partText });
            }
        }
    }

    private static JsonObject ToolCallToClaude(JsonObject call, bool legacy = false)
    {
        var function = legacy ? call : call["function"] as JsonObject
            ?? throw new InvalidOperationException("Chat Completions tool_call 缺少 function");
        return new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = legacy ? $"toolu_{Guid.NewGuid():N}" : RequireString(call, "id", "tool_call 缺少 id"),
            ["name"] = RequireString(function, "name", "tool_call.function 缺少 name"),
            ["input"] = ParseArguments(function["arguments"])
        };
    }

    private static JsonObject ParseArguments(JsonNode? arguments)
    {
        if (arguments is JsonObject obj)
        {
            return obj.DeepClone().AsObject();
        }
        if (arguments is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw new InvalidOperationException("tool_call.function.arguments 必须是 JSON 字符串");
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidOperationException("tool_call.function.arguments 必须解析为 JSON 对象");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"tool_call.function.arguments 不是有效 JSON：{ex.Message}", ex);
        }
    }

    private static JsonObject Usage(JsonObject? usage)
    {
        return new JsonObject
        {
            ["input_tokens"] = GetLong(usage, "prompt_tokens"),
            ["cache_creation_input_tokens"] = 0,
            ["cache_read_input_tokens"] = GetLong(usage?["prompt_tokens_details"] as JsonObject, "cached_tokens"),
            ["output_tokens"] = GetLong(usage, "completion_tokens")
        };
    }

    internal static string StopReason(string finishReason, JsonArray? content = null)
    {
        return finishReason switch
        {
            "length" => "max_tokens",
            "tool_calls" or "function_call" => "tool_use",
            "stop" or "content_filter" => "end_turn",
            _ when content?.OfType<JsonObject>().Any(item => GetString(item, "type") == "tool_use") == true => "tool_use",
            _ => "end_turn"
        };
    }

    internal static string MessageId(string id)
    {
        if (id.StartsWith("msg_", StringComparison.Ordinal))
        {
            return id;
        }
        return $"msg_{(string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id)}";
    }

    private static string Coalesce(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static void Copy(JsonObject source, JsonObject target, string name)
    {
        if (source[name] is not null)
        {
            target[name] = source[name]!.DeepClone();
        }
    }

    internal static string RequireString(JsonObject value, string name, string error)
    {
        var result = GetString(value, name);
        return string.IsNullOrWhiteSpace(result) ? throw new InvalidOperationException(error) : result;
    }

    private static string GetString(JsonObject? value, string name)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var result)
            ? result
            : string.Empty;
    }

    private static bool GetBoolean(JsonObject? value, string name)
    {
        return value?[name] is JsonValue jsonValue &&
               jsonValue.TryGetValue<bool>(out var result) &&
               result;
    }

    private static long GetLong(JsonObject? value, string name)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<long>(out var result)
            ? result
            : 0;
    }
}
