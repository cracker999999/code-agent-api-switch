using System.Text.Json.Nodes;
using static APISwitch.Services.ChatProxyTransport;
using static APISwitch.Services.ClaudeChatTransform;

namespace APISwitch.Services;

internal static class ClaudeGeminiTransform
{
    internal static ClaudeChatRequest ToGenerateContent(
        JsonObject body, GeminiToolCallStore? toolCalls = null, string scope = "")
    {
        var model = GeminiNativeApi.NormalizeModel(RequireString(body, "model", "Claude 请求缺少 model"));
        if (string.IsNullOrWhiteSpace(model) || model.Contains('/') || model.Contains('\\'))
            throw new InvalidOperationException("Gemini model 必须是模型名称或 models/模型名称");
        if (body["messages"] is not JsonArray { Count: > 0 } messages)
            throw new InvalidOperationException("Claude 请求缺少 messages");

        var result = new JsonObject();
        var system = new JsonArray();
        var systemText = ReadTextContent(body["system"]);
        if (!string.IsNullOrEmpty(systemText))
            system.Add(new JsonObject { ["text"] = systemText });
        var contents = new JsonArray();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message is not JsonObject source)
                throw new InvalidOperationException("Claude message 必须是 JSON 对象");
            var role = RequireString(source, "role", "Claude 消息缺少 role");
            if (role == "system" && contents.Count == 0)
            {
                system.Add(new JsonObject { ["text"] = ReadTextContent(source["content"]) });
                continue;
            }
            if (role is not ("user" or "assistant" or "system"))
                throw new InvalidOperationException($"不支持 Claude 消息角色：{role}");
            var parts = ConvertContent(source["content"], role, model, names, toolCalls, scope);
            if (parts.Count == 0)
                continue;
            var geminiRole = role == "assistant" ? "model" : "user";
            if (contents.LastOrDefault() is JsonObject previous && GetString(previous, "role") == geminiRole)
            {
                // Claude 可能将并行工具结果拆成相邻 user 消息；Gemini 要求同轮一起回传。
                foreach (var part in parts)
                    previous["parts"]!.AsArray().Add(part!.DeepClone());
            }
            else
                contents.Add(new JsonObject { ["role"] = geminiRole, ["parts"] = parts });
        }
        if (contents.Count == 0)
            throw new InvalidOperationException("Claude 请求没有可转换的消息内容");
        result["contents"] = contents;
        if (system.Count > 0)
            result["systemInstruction"] = new JsonObject { ["parts"] = system };

        var generation = new JsonObject();
        foreach (var (source, target) in new[]
        {
            ("max_tokens", "maxOutputTokens"), ("temperature", "temperature"),
            ("top_p", "topP"), ("top_k", "topK"), ("stop_sequences", "stopSequences")
        })
        {
            if (body[source] is { } value)
                generation[target] = value.DeepClone();
        }
        if (ThinkingConfig(body, model) is { } thinking)
            generation["thinkingConfig"] = thinking;
        if (generation.Count > 0)
            result["generationConfig"] = generation;

        if (body["tools"] is JsonArray { Count: > 0 } tools)
        {
            var declarations = new JsonArray();
            foreach (var node in tools)
            {
                if (node is not JsonObject tool || tool["input_schema"] is not JsonObject schema)
                    throw new InvalidOperationException("Gemini 适配仅支持带 input_schema 的自定义工具");
                var parameters = schema.DeepClone().AsObject();
                parameters.Remove("$schema");
                parameters.Remove("$id");
                parameters["type"] ??= "object";
                if (GetString(parameters, "type") != "object")
                    throw new InvalidOperationException("工具 input_schema 必须是 object");
                parameters["properties"] ??= new JsonObject();
                // Native API 支持 JSON Schema；保留 $ref、联合类型等约束，避免降级成受限 parameters。
                var declaration = new JsonObject
                {
                    ["name"] = RequireString(tool, "name", "Claude 工具缺少 name"),
                    ["parametersJsonSchema"] = parameters
                };
                if (tool["description"] is { } description)
                    declaration["description"] = description.DeepClone();
                declarations.Add(declaration);
            }
            result["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });
        }
        if (body["tool_choice"] is JsonObject choice)
        {
            var config = new JsonObject
            {
                ["mode"] = GetString(choice, "type") switch
                {
                    "auto" => "AUTO", "none" => "NONE", "any" or "tool" => "ANY",
                    var type => throw new InvalidOperationException($"不支持 Claude tool_choice：{type}")
                }
            };
            if (GetString(choice, "type") == "tool")
                config["allowedFunctionNames"] = new JsonArray(RequireString(choice, "name", "tool_choice 缺少 name"));
            result["toolConfig"] = new JsonObject { ["functionCallingConfig"] = config };
        }
        return new ClaudeChatRequest(result, body["stream"]?.GetValue<bool>() == true, model);
    }

    private static JsonArray ConvertContent(JsonNode? content, string role, string model,
        Dictionary<string, string> names, GeminiToolCallStore? toolCalls, string scope)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
            return new JsonArray(new JsonObject { ["text"] = text });
        if (content is not JsonArray blocks)
            throw new InvalidOperationException("Claude content 必须是字符串或数组");
        var parts = new JsonArray();
        foreach (var node in blocks)
        {
            if (node is not JsonObject block)
                throw new InvalidOperationException("Claude 内容块必须是 JSON 对象");
            switch (GetString(block, "type"))
            {
                case "text":
                    parts.Add(new JsonObject { ["text"] = GetString(block, "text") });
                    break;
                case "image":
                case "document":
                    parts.Add(MediaPart(block));
                    break;
                case "tool_use" when role == "assistant":
                {
                    var id = RequireString(block, "id", "tool_use 缺少 id");
                    var name = RequireString(block, "name", "tool_use 缺少 name");
                    var input = block["input"] as JsonObject
                        ?? throw new InvalidOperationException("tool_use.input 必须是 JSON 对象");
                    names[id] = name;
                    var saved = toolCalls?.Get(scope, id);
                    var original = saved?["functionCall"]?.AsObject();
                    if (original is not null && GetString(original, "name") != name)
                        throw new InvalidOperationException("历史工具名称已改变，无法匹配原 Gemini 调用");
                    if (original is not null && JsonNode.DeepEquals(original["args"], input))
                    {
                        parts.Add(saved);
                    }
                    else
                    {
                        // Claude Code 可能补默认值或调整参数；变化后的调用必须重建，不能复用旧签名。
                        var part = new JsonObject
                        {
                            ["functionCall"] = new JsonObject { ["name"] = name, ["args"] = input.DeepClone() }
                        };
                        // 工具结果仍携带缓存的上游 ID，重建调用时保留它以保持配对。
                        if (original?["id"] is { } originalId)
                            part["functionCall"]!["id"] = originalId.DeepClone();
                        // 与 cc-switch 一致：跨供应商/重启后的无签名历史使用 Gemini 3/4 官方占位值。
                        if (model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase) ||
                            model.StartsWith("gemini-4", StringComparison.OrdinalIgnoreCase))
                            part["thoughtSignature"] = "skip_thought_signature_validator";
                        parts.Add(part);
                    }
                    break;
                }
                case "tool_result" when role == "user":
                {
                    var id = RequireString(block, "tool_use_id", "tool_result 缺少 tool_use_id");
                    var saved = toolCalls?.Get(scope, id)?["functionCall"] as JsonObject;
                    var name = names.GetValueOrDefault(id) ?? GetString(saved, "name");
                    if (string.IsNullOrEmpty(name))
                        throw new InvalidOperationException($"找不到 tool_result 对应的工具名称：{id}");
                    var resultContent = block["content"];
                    var media = new JsonArray();
                    if (resultContent is JsonArray resultBlocks)
                    {
                        var texts = new JsonArray();
                        foreach (var resultBlock in resultBlocks)
                        {
                            if (resultBlock is JsonObject item && GetString(item, "type") is "image" or "document")
                                media.Add(MediaPart(item));
                            else
                                texts.Add(resultBlock?.DeepClone());
                        }
                        resultContent = texts;
                    }
                    var response = resultContent is JsonObject obj
                        ? obj.DeepClone()
                        : new JsonObject { ["content"] = ReadTextContent(resultContent) };
                    var functionResponse = new JsonObject
                    {
                        ["name"] = name,
                        ["response"] = block["is_error"]?.GetValue<bool>() == true
                            ? new JsonObject { ["error"] = response } : response
                    };
                    if (saved?["id"] is { } originalId)
                        functionResponse["id"] = originalId.DeepClone();
                    parts.Add(new JsonObject { ["functionResponse"] = functionResponse });
                    // 媒体独立放入 user parts，兼容不支持多模态 functionResponse 的 Gemini 2.5。
                    foreach (var item in media)
                        parts.Add(item!.DeepClone());
                    break;
                }
                case "thinking" when role == "assistant":
                case "redacted_thinking" when role == "assistant":
                    // Anthropic 的思考签名不能发给 Gemini；Gemini 工具签名由本地缓存回传。
                    break;
                default:
                    throw new InvalidOperationException($"不支持 Claude {role} 内容块：{GetString(block, "type")}");
            }
        }
        return parts;
    }

    private static JsonObject MediaPart(JsonObject block)
    {
        var source = block["source"] as JsonObject
            ?? throw new InvalidOperationException("Claude 媒体内容缺少 source");
        return GetString(source, "type") switch
        {
            "base64" => new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = RequireString(source, "media_type", "媒体缺少 media_type"),
                    ["data"] = RequireString(source, "data", "媒体缺少 data")
                }
            },
            "url" => new JsonObject
            {
                ["fileData"] = new JsonObject { ["fileUri"] = RequireString(source, "url", "媒体缺少 url") }
            },
            "text" when GetString(block, "type") == "document" =>
                new JsonObject { ["text"] = RequireString(source, "data", "文档缺少 data") },
            var type => throw new InvalidOperationException($"不支持媒体 source：{type}")
        };
    }

    private static JsonObject? ThinkingConfig(JsonObject body, string model)
    {
        var type = GetString(body["thinking"] as JsonObject, "type");
        var effort = GetString(body["output_config"] as JsonObject, "effort");
        if (type.Length == 0 && effort.Length == 0)
            return null;
        var name = model.ToLowerInvariant();
        var segments = name.Split('-');
        if (segments.Any(s => s is "image" or "tts" or "live" or "audio" or "embedding") ||
            segments.Last() is "minimal" or "low" or "medium" or "high")
            return null;
        var off = type is "" or "disabled";
        if (effort.Length == 0)
        {
            var budget = body["thinking"]?["budget_tokens"]?.GetValue<long>() ?? 24576;
            effort = budget < 4000 ? "low" : budget < 16000 ? "medium" : "high";
        }
        if (name.StartsWith("gemini-2.5-", StringComparison.Ordinal))
        {
            // 2.5 Pro 无法关闭思考，最低预算为 128；3、4 系列只发送 thinkingLevel。
            var budget = off ? (name.Contains("-pro") ? 128 : 0)
                : effort == "low" ? 1024 : effort == "medium" ? 8192 : 24576;
            return new JsonObject { ["thinkingBudget"] = budget };
        }
        if (name.StartsWith("gemini-3", StringComparison.Ordinal) || name.StartsWith("gemini-4", StringComparison.Ordinal))
        {
            var minimal = name.Contains("flash-lite") || name.StartsWith("gemini-3-flash") ||
                name.StartsWith("gemini-3.6-flash");
            var level = off ? (minimal ? "minimal" : "low")
                : effort == "low" ? "low"
                : effort == "medium" && !name.StartsWith("gemini-3-pro") ? "medium" : "high";
            return new JsonObject { ["thinkingLevel"] = level };
        }
        return null;
    }

    internal static JsonObject Usage(JsonObject? usage)
    {
        var prompt = usage?["promptTokenCount"]?.GetValue<long>() ?? 0;
        var cached = usage?["cachedContentTokenCount"]?.GetValue<long>() ?? 0;
        var output = usage?["totalTokenCount"] is { } total ? total.GetValue<long>() - prompt
            : (usage?["candidatesTokenCount"]?.GetValue<long>() ?? 0) +
              (usage?["thoughtsTokenCount"]?.GetValue<long>() ?? 0);
        // Gemini prompt 包含缓存，Anthropic input_tokens 必须扣除缓存，避免重复计费。
        return new JsonObject
        {
            ["input_tokens"] = Math.Max(0, prompt - cached),
            ["cache_read_input_tokens"] = cached,
            ["cache_creation_input_tokens"] = 0,
            ["output_tokens"] = Math.Max(0, output)
        };
    }

    internal static string FinishReason(string reason, bool hasTools) => reason switch
    {
        "MAX_TOKENS" => "max_tokens",
        "SAFETY" or "RECITATION" or "SPII" or "BLOCKLIST" or "PROHIBITED_CONTENT" => "refusal",
        "MALFORMED_FUNCTION_CALL" or "UNEXPECTED_TOOL_CALL" =>
            throw new InvalidOperationException($"Gemini 工具调用失败：{reason}"),
        _ => hasTools ? "tool_use" : "end_turn"
    };

    internal static IEnumerable<JsonObject> ReadParts(JsonObject? candidate)
    {
        foreach (var node in candidate?["content"]?["parts"] as JsonArray ?? new JsonArray())
        {
            var part = node as JsonObject ?? throw new InvalidOperationException("Gemini part 必须是 JSON 对象");
            // 与 cc-switch 一致，不把 Gemini 内部思考摘要伪装成 Anthropic 签名思考块。
            if (part["thought"]?.GetValue<bool>() != true)
                yield return part;
        }
    }

    internal static (string Name, JsonObject Args) ReadToolCall(JsonObject part)
    {
        var call = part["functionCall"] as JsonObject
            ?? throw new InvalidOperationException("Gemini 响应缺少 functionCall");
        var name = RequireString(call, "name", "Gemini functionCall 缺少 name");
        var args = call["args"] as JsonObject ?? (call["args"] is null ? new JsonObject()
            : throw new InvalidOperationException("Gemini functionCall.args 必须是 JSON 对象"));
        return (name, args);
    }

    internal static JsonObject ToolUse(JsonObject part, GeminiToolCallStore store, string scope)
    {
        var (name, args) = ReadToolCall(part);
        part["functionCall"]!["args"] ??= args;
        return new JsonObject
        {
            ["id"] = store.Add(scope, part),
            ["type"] = "tool_use",
            ["name"] = name,
            ["input"] = args.DeepClone()
        };
    }

    internal static JsonObject Message(JsonObject body, string model, JsonArray content, string? stopReason)
    {
        return new JsonObject
        {
            ["id"] = MessageId(GetString(body, "responseId")),
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = string.IsNullOrEmpty(GetString(body, "modelVersion")) ? model : GetString(body, "modelVersion"),
            ["content"] = content,
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = Usage(body["usageMetadata"] as JsonObject)
        };
    }

    internal static (JsonObject? Candidate, string BlockReason, string FinishReason) ReadResponse(
        JsonObject body, bool requireComplete = false)
    {
        if (body["error"] is not null)
            throw new InvalidOperationException(ReadErrorMessage(body));
        var blocked = GetString(body["promptFeedback"] as JsonObject, "blockReason");
        var candidate = (body["candidates"] as JsonArray)?.FirstOrDefault() as JsonObject;
        var finish = GetString(candidate, "finishReason");
        if (requireComplete && blocked.Length == 0)
        {
            if (candidate is null)
                throw new InvalidOperationException("Gemini 响应缺少 candidates");
            if (finish is "" or "FINISH_REASON_UNSPECIFIED")
                throw new InvalidOperationException("Gemini 响应未完成，缺少 finishReason");
        }
        return (candidate, blocked, finish);
    }

    internal static JsonObject ToMessages(JsonObject body, string model, GeminiToolCallStore store, string scope)
    {
        var (candidate, blocked, finish) = ReadResponse(body, requireComplete: true);
        var content = new JsonArray();
        var hasTools = false;
        if (blocked.Length > 0)
            content.Add(new JsonObject { ["type"] = "text", ["text"] = $"Gemini 拒绝请求：{blocked}" });
        foreach (var part in ReadParts(candidate))
        {
            if (part["functionCall"] is not null)
            {
                content.Add(ToolUse(part.DeepClone().AsObject(), store, scope));
                hasTools = true;
            }
            else if (part["text"] is { } text)
                content.Add(new JsonObject { ["type"] = "text", ["text"] = text.DeepClone() });
        }
        return Message(body, model, content,
            blocked.Length > 0 ? "refusal" : FinishReason(finish, hasTools));
    }
}

internal sealed class GeminiToolCallStore
{
    private readonly Dictionary<(string Scope, string Id), (JsonObject Part, int Size)> _calls = new();
    private readonly Queue<(string Scope, string Id)> _order = new();
    private readonly object _sync = new();
    private int _size;

    internal string Add(string scope, JsonObject part)
    {
        // 每次生成唯一的 Claude ID，避免不同会话的上游工具 ID 相撞；原 ID 仅回传给 Gemini。
        var id = $"toolu_{Guid.NewGuid():N}";
        var copy = part.DeepClone().AsObject();
        var size = copy.ToJsonString().Length;
        lock (_sync)
        {
            _calls.Add((scope, id), (copy, size));
            _order.Enqueue((scope, id));
            _size += size;
            // shortcut: 仅缓存最近 256 次工具调用、约 8 MiB；需跨重启原样回放时再持久化。
            while (_calls.Count > 256 || _size > 4 * 1024 * 1024)
            {
                var oldest = _order.Dequeue();
                _size -= _calls[oldest].Size;
                _calls.Remove(oldest);
            }
        }
        return id;
    }

    internal JsonObject? Get(string scope, string id)
    {
        lock (_sync)
            return _calls.TryGetValue((scope, id), out var call) ? call.Part.DeepClone().AsObject() : null;
    }
}
