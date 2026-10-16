using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

/// <summary>把 Claude Messages API 转换为上游 Chat Completions API。</summary>
public sealed class ClaudeChatProxyService : IDisposable
{
    public const string LocalBaseUrl = "http://127.0.0.1:24822";

    private readonly ChatProxyHost _host;

    public ClaudeChatProxyService()
    {
        _host = new ChatProxyHost(LocalBaseUrl, 1, "Claude", HandleRequestAsync);
    }

    public void Start(Provider provider)
    {
        _host.Start(provider);
    }

    public void Stop()
    {
        _host.Stop();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task HandleRequestAsync(HttpListenerContext context, Provider? provider)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
                !context.Request.Url!.AbsolutePath.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                await WriteErrorAsync(context.Response, 404, "仅支持 POST /v1/messages", "not_found_error");
                return;
            }

            if (provider is null)
            {
                await WriteErrorAsync(context.Response, 503, "Claude 供应商代理尚未配置", "api_error");
                return;
            }

            using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(context.Response, 400, "请求体必须是 JSON 对象", "invalid_request_error");
                return;
            }

            ClaudeChatRequest transformed;
            try
            {
                var requestBody = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
                transformed = ClaudeChatTransform.ToChatCompletions(requestBody);
            }
            catch (InvalidOperationException ex)
            {
                await WriteErrorAsync(context.Response, 400, ex.Message, "invalid_request_error");
                return;
            }

            using var upstreamResponse = await ChatProxyTransport.SendAsync(
                _host.HttpClient,
                provider,
                transformed.Body,
                transformed.Stream);

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                var errorText = await upstreamResponse.Content.ReadAsStringAsync();
                await WriteUpstreamErrorAsync(context.Response, (int)upstreamResponse.StatusCode, errorText);
                return;
            }

            if (!transformed.Stream)
            {
                var responseText = await upstreamResponse.Content.ReadAsStringAsync();
                var chatResponse = JsonNode.Parse(responseText)?.AsObject()
                    ?? throw new InvalidOperationException("上游 Chat Completions 响应不是 JSON 对象");
                await ChatProxyTransport.WriteJsonAsync(
                    context.Response,
                    ClaudeChatTransform.ToMessages(chatResponse, transformed.Model));
                return;
            }

            if (!string.Equals(
                    upstreamResponse.Content.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase))
            {
                var responseText = await upstreamResponse.Content.ReadAsStringAsync();
                var chatResponse = JsonNode.Parse(responseText)?.AsObject()
                    ?? throw new InvalidOperationException("上游 Chat Completions 响应不是 JSON 对象");
                await StreamSingleResponseAsync(context.Response, chatResponse, transformed.Model);
                return;
            }

            await ChatProxyTransport.RelaySseAsync(
                context.Response,
                upstreamResponse.Content,
                new ChatToClaudeStream(transformed.Model));
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(context.Response, 400, $"请求体 JSON 无效：{ex.Message}", "invalid_request_error");
        }
        catch (Exception ex)
        {
            if (context.Response.OutputStream.CanWrite)
            {
                await WriteErrorAsync(context.Response, 502, ex.Message, "api_error");
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static async Task StreamSingleResponseAsync(
        HttpListenerResponse response,
        JsonObject chatResponse,
        string fallbackModel)
    {
        ChatProxyTransport.BeginSse(response);
        var choices = chatResponse["choices"] as JsonArray;
        var choice = choices?.FirstOrDefault() as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 choices");
        var message = choice["message"] as JsonObject
            ?? throw new InvalidOperationException("Chat Completions 响应缺少 message");
        var chunk = new JsonObject
        {
            ["id"] = chatResponse["id"]?.DeepClone(),
            ["model"] = chatResponse["model"]?.DeepClone(),
            ["usage"] = chatResponse["usage"]?.DeepClone(),
            ["choices"] = new JsonArray(new JsonObject
            {
                ["delta"] = message.DeepClone(),
                ["finish_reason"] = choice["finish_reason"]?.DeepClone()
            })
        };

        var state = new ChatToClaudeStream(fallbackModel);
        await ChatProxyTransport.WriteEventsAsync(response, state.Handle(chunk));
        await ChatProxyTransport.WriteEventsAsync(response, state.Complete());
    }

    private static async Task WriteErrorAsync(
        HttpListenerResponse response,
        int statusCode,
        string message,
        string errorType)
    {
        var body = new JsonObject
        {
            ["type"] = "error",
            ["error"] = new JsonObject
            {
                ["type"] = errorType,
                ["message"] = message
            }
        };
        await ChatProxyTransport.WriteJsonAsync(response, body, statusCode);
    }

    private static async Task WriteUpstreamErrorAsync(HttpListenerResponse response, int statusCode, string text)
    {
        var body = ChatProxyTransport.ParseJsonObject(text);
        var error = body?["error"] as JsonObject;
        var message = body is null ? text : ChatProxyTransport.ReadErrorMessage(body);
        var errorType = ChatProxyTransport.GetString(error, "type");
        await WriteErrorAsync(
            response,
            statusCode,
            message,
            string.IsNullOrWhiteSpace(errorType) ? "api_error" : errorType);
    }
}

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

    private static string ReadTextContent(JsonNode? content)
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

    private static string RequireString(JsonObject value, string name, string error)
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

internal sealed class ChatToClaudeStream : IChatProxyStreamState
{
    private sealed class ToolState
    {
        internal required int ChatIndex { get; init; }
        internal int BlockIndex { get; set; } = -1;
        internal string Id { get; set; } = string.Empty;
        internal string Name { get; set; } = string.Empty;
        internal string PendingArguments { get; set; } = string.Empty;
        internal bool Started { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _fallbackModel;
    private readonly Dictionary<int, ToolState> _tools = new();
    private readonly List<int> _openBlocks = new();
    private string _messageId = string.Empty;
    private string _model = string.Empty;
    private string _finishReason = string.Empty;
    private long _inputTokens;
    private long _cachedInputTokens;
    private long _outputTokens;
    private int _nextBlockIndex;
    private int? _textBlockIndex;
    private bool _started;

    internal ChatToClaudeStream(string fallbackModel)
    {
        _fallbackModel = fallbackModel;
    }

    public bool IsCompleted { get; private set; }

    public IEnumerable<string> Handle(JsonObject chunk)
    {
        if (IsCompleted)
        {
            return [];
        }

        UpdateMetadata(chunk);
        var events = new List<string>();
        EnsureStarted(events);

        if (chunk["choices"] is not JsonArray choices || choices.FirstOrDefault() is not JsonObject choice)
        {
            return events;
        }

        if (choice["delta"] is JsonObject delta)
        {
            var text = DeltaText(delta["content"]);
            if (!string.IsNullOrEmpty(text))
            {
                EnsureTextBlock(events);
                events.Add(Event("content_block_delta", new JsonObject
                {
                    ["type"] = "content_block_delta",
                    ["index"] = _textBlockIndex!.Value,
                    ["delta"] = new JsonObject
                    {
                        ["type"] = "text_delta",
                        ["text"] = text
                    }
                }));
            }

            if (delta["tool_calls"] is JsonArray toolCalls)
            {
                foreach (var call in toolCalls.OfType<JsonObject>())
                {
                    HandleToolCall(call, events);
                }
            }
            else if (delta["function_call"] is JsonObject legacyCall)
            {
                legacyCall = legacyCall.DeepClone().AsObject();
                legacyCall["index"] = 0;
                HandleToolCall(new JsonObject
                {
                    ["index"] = 0,
                    ["id"] = $"toolu_{Guid.NewGuid():N}",
                    ["function"] = legacyCall
                }, events);
            }
        }

        var finishReason = GetString(choice, "finish_reason");
        if (!string.IsNullOrWhiteSpace(finishReason))
        {
            _finishReason = finishReason;
        }
        return events;
    }

    public IEnumerable<string> Complete()
    {
        if (IsCompleted)
        {
            return [];
        }

        var events = new List<string>();
        EnsureStarted(events);
        foreach (var tool in _tools.Values.OrderBy(tool => tool.ChatIndex))
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                return Fail("Chat Completions 工具调用缺少名称");
            }
            EnsureToolStarted(tool, events);
        }

        if (_openBlocks.Count == 0)
        {
            EnsureTextBlock(events);
        }
        foreach (var blockIndex in _openBlocks.Order())
        {
            events.Add(Event("content_block_stop", new JsonObject
            {
                ["type"] = "content_block_stop",
                ["index"] = blockIndex
            }));
        }

        var stopReason = ClaudeChatTransform.StopReason(
            _finishReason,
            _tools.Count == 0 ? null : new JsonArray(new JsonObject { ["type"] = "tool_use" }));
        events.Add(Event("message_delta", new JsonObject
        {
            ["type"] = "message_delta",
            ["delta"] = new JsonObject
            {
                ["stop_reason"] = stopReason,
                ["stop_sequence"] = null
            },
            ["usage"] = new JsonObject { ["output_tokens"] = _outputTokens }
        }));
        events.Add(Event("message_stop", new JsonObject { ["type"] = "message_stop" }));
        IsCompleted = true;
        return events;
    }

    public IEnumerable<string> Fail(string message)
    {
        if (IsCompleted)
        {
            return [];
        }

        IsCompleted = true;
        return
        [
            Event("error", new JsonObject
            {
                ["type"] = "error",
                ["error"] = new JsonObject
                {
                    ["type"] = "api_error",
                    ["message"] = message
                }
            })
        ];
    }

    private void UpdateMetadata(JsonObject chunk)
    {
        if (!_started)
        {
            var id = GetString(chunk, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                _messageId = ClaudeChatTransform.MessageId(id);
            }
            var model = GetString(chunk, "model");
            if (!string.IsNullOrWhiteSpace(model))
            {
                _model = model;
            }
        }

        if (chunk["usage"] is JsonObject usage)
        {
            _inputTokens = GetLong(usage, "prompt_tokens", _inputTokens);
            _outputTokens = GetLong(usage, "completion_tokens", _outputTokens);
            _cachedInputTokens = GetLong(
                usage["prompt_tokens_details"] as JsonObject,
                "cached_tokens",
                _cachedInputTokens);
        }
    }

    private void EnsureStarted(List<string> events)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _messageId = string.IsNullOrWhiteSpace(_messageId)
            ? ClaudeChatTransform.MessageId(string.Empty)
            : _messageId;
        _model = string.IsNullOrWhiteSpace(_model) ? _fallbackModel : _model;
        events.Add(Event("message_start", new JsonObject
        {
            ["type"] = "message_start",
            ["message"] = new JsonObject
            {
                ["id"] = _messageId,
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = _model,
                ["content"] = new JsonArray(),
                ["stop_reason"] = null,
                ["stop_sequence"] = null,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = _inputTokens,
                    ["cache_creation_input_tokens"] = 0,
                    ["cache_read_input_tokens"] = _cachedInputTokens,
                    ["output_tokens"] = 0
                }
            }
        }));
    }

    private void EnsureTextBlock(List<string> events)
    {
        if (_textBlockIndex is not null)
        {
            return;
        }

        _textBlockIndex = _nextBlockIndex++;
        _openBlocks.Add(_textBlockIndex.Value);
        events.Add(Event("content_block_start", new JsonObject
        {
            ["type"] = "content_block_start",
            ["index"] = _textBlockIndex.Value,
            ["content_block"] = new JsonObject
            {
                ["type"] = "text",
                ["text"] = string.Empty
            }
        }));
    }

    private void HandleToolCall(JsonObject call, List<string> events)
    {
        var chatIndex = GetInt(call, "index", _tools.Count);
        if (!_tools.TryGetValue(chatIndex, out var state))
        {
            state = new ToolState { ChatIndex = chatIndex };
            _tools.Add(chatIndex, state);
        }

        var id = GetString(call, "id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            state.Id = id;
        }
        if (call["function"] is not JsonObject function)
        {
            return;
        }

        var name = GetString(function, "name");
        if (!string.IsNullOrEmpty(name))
        {
            state.Name += name;
        }
        var arguments = GetString(function, "arguments");
        if (!state.Started)
        {
            state.PendingArguments += arguments;
            // 等首段参数再发 tool_use，避免上游分片工具名时把半截名称写进不可修改的 start 事件。
            if (!string.IsNullOrWhiteSpace(state.Name) && !string.IsNullOrEmpty(arguments))
            {
                EnsureToolStarted(state, events);
            }
            return;
        }

        if (!string.IsNullOrEmpty(arguments))
        {
            events.Add(ToolArgumentsDelta(state, arguments));
        }
    }

    private void EnsureToolStarted(ToolState state, List<string> events)
    {
        if (state.Started)
        {
            return;
        }

        state.Started = true;
        state.BlockIndex = _nextBlockIndex++;
        state.Id = string.IsNullOrWhiteSpace(state.Id) ? $"toolu_{Guid.NewGuid():N}" : state.Id;
        _openBlocks.Add(state.BlockIndex);
        events.Add(Event("content_block_start", new JsonObject
        {
            ["type"] = "content_block_start",
            ["index"] = state.BlockIndex,
            ["content_block"] = new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = state.Id,
                ["name"] = state.Name,
                ["input"] = new JsonObject()
            }
        }));
        if (!string.IsNullOrEmpty(state.PendingArguments))
        {
            events.Add(ToolArgumentsDelta(state, state.PendingArguments));
            state.PendingArguments = string.Empty;
        }
    }

    private static string ToolArgumentsDelta(ToolState state, string arguments)
    {
        return Event("content_block_delta", new JsonObject
        {
            ["type"] = "content_block_delta",
            ["index"] = state.BlockIndex,
            ["delta"] = new JsonObject
            {
                ["type"] = "input_json_delta",
                ["partial_json"] = arguments
            }
        });
    }

    private static string DeltaText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (content is not JsonArray parts)
        {
            return string.Empty;
        }
        return string.Concat(parts.OfType<JsonObject>().Select(part =>
            GetString(part, "type") == "refusal"
                ? GetString(part, "refusal")
                : GetString(part, "text")));
    }

    private static string Event(string name, JsonObject body)
    {
        return $"event: {name}\ndata: {body.ToJsonString(JsonOptions)}\n\n";
    }

    private static string GetString(JsonObject? value, string name)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var result)
            ? result
            : string.Empty;
    }

    private static int GetInt(JsonObject? value, string name, int fallback)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result)
            ? result
            : fallback;
    }

    private static long GetLong(JsonObject? value, string name, long fallback)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<long>(out var result)
            ? result
            : fallback;
    }
}
