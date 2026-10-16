using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

/// Local Responses -> Chat Completions bridge for Codex providers that expose only /chat/completions.
public sealed class CodexChatProxyService : IDisposable
{
    public const string LocalBaseUrl = "http://127.0.0.1:24821";

    private readonly ChatProxyHost _host;

    public CodexChatProxyService()
    {
        _host = new ChatProxyHost(LocalBaseUrl, 0, "Codex", HandleRequestAsync);
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
                !context.Request.Url!.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                await WriteErrorAsync(context.Response, 404, "仅支持 POST /responses");
                return;
            }

            if (provider is null)
            {
                await WriteErrorAsync(context.Response, 503, "Codex 供应商代理尚未配置");
                return;
            }

            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: CancellationToken.None);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(context.Response, 400, "请求体必须是 JSON 对象");
                return;
            }

            var requestBody = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
            var transformed = CodexChatTransform.ToChatCompletions(requestBody);
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
                var response = CodexChatTransform.ToResponses(chatResponse, transformed.Context);
                await ChatProxyTransport.WriteJsonAsync(context.Response, response);
                return;
            }

            if (!string.Equals(upstreamResponse.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var responseText = await upstreamResponse.Content.ReadAsStringAsync();
                var chatResponse = JsonNode.Parse(responseText)?.AsObject()
                    ?? throw new InvalidOperationException("上游 Chat Completions 响应不是 JSON 对象");
                await StreamSingleResponseAsync(context.Response, chatResponse, transformed.Context);
                return;
            }

            await ChatProxyTransport.RelaySseAsync(
                context.Response,
                upstreamResponse.Content,
                new ChatToResponsesStream(transformed.Context));
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(context.Response, 400, $"请求体 JSON 无效：{ex.Message}");
        }
        catch (Exception ex)
        {
            if (!context.Response.OutputStream.CanWrite)
            {
                return;
            }

            await WriteErrorAsync(context.Response, 502, ex.Message);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static async Task StreamSingleResponseAsync(HttpListenerResponse response, JsonObject chatResponse, CodexToolContext context)
    {
        ChatProxyTransport.BeginSse(response);
        var state = new ChatToResponsesStream(context);
        var choices = chatResponse["choices"] as JsonArray;
        var choice = choices?.FirstOrDefault() as JsonObject;
        var message = choice?["message"] as JsonObject;
        var delta = message?.DeepClone()?.AsObject() ?? new JsonObject();
        var chunk = new JsonObject
        {
            ["id"] = chatResponse["id"]?.DeepClone(),
            ["model"] = chatResponse["model"]?.DeepClone(),
            ["created"] = chatResponse["created"]?.DeepClone(),
            ["usage"] = chatResponse["usage"]?.DeepClone(),
            ["choices"] = new JsonArray(new JsonObject
            {
                ["delta"] = delta,
                ["finish_reason"] = choice?["finish_reason"]?.DeepClone()
            })
        };
        await ChatProxyTransport.WriteEventsAsync(response, state.Handle(chunk));
        await ChatProxyTransport.WriteEventsAsync(response, state.Complete());
    }

    private static async Task WriteErrorAsync(HttpListenerResponse response, int statusCode, string message)
    {
        var body = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = message,
                ["type"] = "proxy_error"
            }
        };
        await ChatProxyTransport.WriteJsonAsync(response, body, statusCode);
    }

    private static async Task WriteUpstreamErrorAsync(HttpListenerResponse response, int statusCode, string text)
    {
        var body = ChatProxyTransport.ParseJsonObject(text);
        var error = body?["error"] as JsonObject;
        var errorType = ChatProxyTransport.GetString(error, "type");
        var normalized = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = body is null ? text : ChatProxyTransport.ReadErrorMessage(body),
                ["type"] = string.IsNullOrWhiteSpace(errorType) ? "upstream_error" : errorType
            }
        };
        await ChatProxyTransport.WriteJsonAsync(response, normalized, statusCode);
    }
}

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

internal sealed class ChatToResponsesStream : IChatProxyStreamState
{
    private sealed class ToolState
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Arguments = string.Empty;
        public string ItemId = string.Empty;
        public int OutputIndex;
        public bool Added;
        public string Reasoning = string.Empty;
    }

    private readonly CodexToolContext _context;
    private readonly Dictionary<int, ToolState> _tools = new();
    private readonly List<(int Index, JsonObject Item)> _output = [];
    private string _responseId = "resp_ccswitch";
    private string _model = string.Empty;
    private long _created;
    private long _nextOutputIndex;
    private string _text = string.Empty;
    private string _reasoning = string.Empty;
    private string _inlineBuffer = string.Empty;
    private bool _inlineMode;
    private bool _textAdded;
    private bool _reasoningAdded;
    private bool _textDone;
    private bool _reasoningDone;
    private bool _responseStarted;
    private JsonObject _usage = new();
    private string? _finishReason;
    public bool IsCompleted { get; private set; }

    public ChatToResponsesStream(CodexToolContext context) => _context = context;

    public IEnumerable<string> Handle(JsonObject chunk)
    {
        var events = new List<string>();
        var id = GetString(chunk, "id");
        if (!string.IsNullOrEmpty(id)) _responseId = CodexChatTransform.ResponseId(id);
        var model = GetString(chunk, "model");
        if (!string.IsNullOrEmpty(model)) _model = model;
        _created = GetLong(chunk, "created", _created);
        if (chunk["usage"] is JsonObject usage) _usage = Usage(usage);
        events.AddRange(EnsureStarted());
        var choice = (chunk["choices"] as JsonArray)?.FirstOrDefault() as JsonObject;
        if (choice is null) return events;
        var delta = choice["delta"] as JsonObject;
        if (delta is not null)
        {
            var reasoning = GetString(delta, "reasoning_content");
            if (string.IsNullOrEmpty(reasoning)) reasoning = GetString(delta, "reasoning");
            if (!string.IsNullOrEmpty(reasoning)) events.AddRange(PushReasoning(reasoning));
            var content = GetString(delta, "content");
            if (!string.IsNullOrEmpty(content)) events.AddRange(PushContent(content));
            if (delta["tool_calls"] is JsonArray calls)
            {
                foreach (var call in calls.OfType<JsonObject>()) events.AddRange(PushTool(call));
            }
        }
        var finish = GetString(choice, "finish_reason");
        if (!string.IsNullOrEmpty(finish)) _finishReason = finish;
        return events;
    }

    public IEnumerable<string> Complete()
    {
        if (IsCompleted) return [];
        var events = new List<string>();
        events.AddRange(EnsureStarted());
        if (!_inlineMode && _inlineBuffer.Length > 0) { events.AddRange(PushText(_inlineBuffer)); _inlineBuffer = string.Empty; }
        events.AddRange(FinalizeReasoning());
        events.AddRange(FinalizeText());
        foreach (var pair in _tools.OrderBy(pair => pair.Key)) events.AddRange(FinalizeTool(pair.Value));
        var output = new JsonArray();
        foreach (var item in _output.OrderBy(item => item.Index)) output.Add(item.Item.DeepClone());
        var response = BaseResponse(_finishReason == "length" ? "incomplete" : "completed", output);
        if (_finishReason == "length") response["incomplete_details"] = new JsonObject { ["reason"] = "max_output_tokens" };
        events.Add(Event("response.completed", new JsonObject { ["type"] = "response.completed", ["response"] = response }));
        IsCompleted = true;
        return events;
    }

    public IEnumerable<string> Fail(string message)
    {
        if (IsCompleted) return [];
        var response = BaseResponse("failed", new JsonArray());
        response["error"] = new JsonObject { ["message"] = message, ["type"] = "upstream_error" };
        IsCompleted = true;
        return [Event("response.failed", new JsonObject { ["type"] = "response.failed", ["response"] = response })];
    }

    private IEnumerable<string> EnsureStarted()
    {
        if (_responseStarted) yield break;
        _responseStarted = true;
        var response = BaseResponse("in_progress", new JsonArray());
        yield return Event("response.created", new JsonObject { ["type"] = "response.created", ["response"] = response.DeepClone() });
        yield return Event("response.in_progress", new JsonObject { ["type"] = "response.in_progress", ["response"] = response });
    }

    private IEnumerable<string> PushContent(string content)
    {
        if (_inlineMode) return PushInlineContent(content);
        _inlineBuffer += content;
        var trimmed = _inlineBuffer.TrimStart();
        if (trimmed.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
        {
            _inlineMode = true;
            var start = _inlineBuffer.IndexOf("<think>", StringComparison.OrdinalIgnoreCase) + 7;
            var reasoning = _inlineBuffer[start..];
            _inlineBuffer = string.Empty;
            return PushInlineContent(reasoning);
        }
        if ("<think>".StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return [];
        var text = _inlineBuffer;
        _inlineBuffer = string.Empty;
        return PushText(text);
    }

    private IEnumerable<string> PushInlineContent(string content)
    {
        var end = content.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (end >= 0)
        {
            var reasoning = content[..end];
            var answer = content[(end + 8)..];
            var events = PushReasoning(reasoning).ToList();
            events.AddRange(FinalizeReasoning());
            events.AddRange(PushText(answer));
            _inlineMode = false;
            return events;
        }
        return PushReasoning(content);
    }

    private IEnumerable<string> PushReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var events = new List<string>();
        if (!_reasoningAdded)
        {
            _reasoningAdded = true;
            var index = _nextOutputIndex++;
            _reasoningIndex = checked((int)index);
            _reasoningId = $"rs_{_responseId}";
            events.Add(Event("response.output_item.added", new JsonObject { ["type"] = "response.output_item.added", ["output_index"] = index, ["item"] = new JsonObject { ["id"] = _reasoningId, ["type"] = "reasoning", ["summary"] = new JsonArray() } }));
            events.Add(Event("response.reasoning_summary_part.added", new JsonObject { ["type"] = "response.reasoning_summary_part.added", ["output_index"] = index, ["item_id"] = _reasoningId, ["summary_index"] = 0, ["part"] = new JsonObject { ["type"] = "summary_text", ["text"] = string.Empty } }));
        }
        _reasoning += text;
        foreach (var state in _tools.Values.Where(state => state.Added == false))
        {
            state.Reasoning += text;
        }
        events.Add(Event("response.reasoning_summary_text.delta", new JsonObject { ["type"] = "response.reasoning_summary_text.delta", ["item_id"] = _reasoningId, ["output_index"] = _reasoningIndex, ["summary_index"] = 0, ["delta"] = text }));
        return events;
    }

    private int _reasoningIndex;
    private string _reasoningId = string.Empty;
    private IEnumerable<string> PushText(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var events = new List<string>();
        if (!_textAdded)
        {
            _textAdded = true;
            _textIndex = checked((int)_nextOutputIndex++);
            _textId = $"{_responseId}_msg";
            events.Add(Event("response.output_item.added", new JsonObject { ["type"] = "response.output_item.added", ["output_index"] = _textIndex, ["item"] = new JsonObject { ["id"] = _textId, ["type"] = "message", ["status"] = "in_progress", ["role"] = "assistant", ["content"] = new JsonArray() } }));
            events.Add(Event("response.content_part.added", new JsonObject { ["type"] = "response.content_part.added", ["item_id"] = _textId, ["output_index"] = _textIndex, ["content_index"] = 0, ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = string.Empty, ["annotations"] = new JsonArray() } }));
        }
        _text += text;
        events.Add(Event("response.output_text.delta", new JsonObject { ["type"] = "response.output_text.delta", ["item_id"] = _textId, ["output_index"] = _textIndex, ["content_index"] = 0, ["delta"] = text }));
        return events;
    }

    private int _textIndex;
    private string _textId = string.Empty;
    private IEnumerable<string> PushTool(JsonObject call)
    {
        var index = (int)GetLong(call, "index", 0);
        if (!_tools.TryGetValue(index, out var state)) _tools[index] = state = new ToolState();
        var id = GetString(call, "id");
        if (!string.IsNullOrEmpty(id)) state.Id = id;
        var function = call["function"] as JsonObject;
        var name = GetString(function, "name");
        if (!string.IsNullOrEmpty(name)) state.Name = name;
        var arguments = GetString(function, "arguments");
        if (!string.IsNullOrEmpty(arguments)) state.Arguments += arguments;
        if (state.Reasoning.Length == 0 && _reasoning.Length > 0) state.Reasoning = _reasoning;
        if (!state.Added && state.Id.Length > 0 && state.Name.Length > 0)
        {
            state.Added = true;
            state.OutputIndex = (int)_nextOutputIndex++;
            state.ItemId = _context.IsCustom(state.Name) ? $"ctc_{state.Id}" : $"fc_{state.Id}";
            var item = ToolItem(state, "in_progress", string.Empty);
            var events = new List<string> { Event("response.output_item.added", new JsonObject { ["type"] = "response.output_item.added", ["output_index"] = state.OutputIndex, ["item"] = item }) };
            if (arguments.Length > 0 && !_context.IsCustom(state.Name)) events.Add(Event("response.function_call_arguments.delta", new JsonObject { ["type"] = "response.function_call_arguments.delta", ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["delta"] = arguments }));
            return events;
        }
        if (state.Added && arguments.Length > 0 && !_context.IsCustom(state.Name))
        {
            return [Event("response.function_call_arguments.delta", new JsonObject { ["type"] = "response.function_call_arguments.delta", ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["delta"] = arguments })];
        }
        return [];
    }

    private IEnumerable<string> FinalizeReasoning()
    {
        if (!_reasoningAdded || _reasoningDone) return [];
        _reasoningDone = true;
        var item = new JsonObject { ["id"] = _reasoningId, ["type"] = "reasoning", ["summary"] = new JsonArray(new JsonObject { ["type"] = "summary_text", ["text"] = _reasoning }) };
        _output.Add((_reasoningIndex, item));
        return [Event("response.reasoning_summary_text.done", new JsonObject { ["type"] = "response.reasoning_summary_text.done", ["item_id"] = _reasoningId, ["output_index"] = _reasoningIndex, ["summary_index"] = 0, ["text"] = _reasoning }), Event("response.output_item.done", new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = _reasoningIndex, ["item"] = item.DeepClone() })];
    }

    private IEnumerable<string> FinalizeText()
    {
        if (!_textAdded || _textDone) return [];
        _textDone = true;
        var item = new JsonObject { ["id"] = _textId, ["type"] = "message", ["status"] = "completed", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = _text, ["annotations"] = new JsonArray() }) };
        _output.Add((_textIndex, item));
        return [Event("response.output_text.done", new JsonObject { ["type"] = "response.output_text.done", ["item_id"] = _textId, ["output_index"] = _textIndex, ["content_index"] = 0, ["text"] = _text }), Event("response.content_part.done", new JsonObject { ["type"] = "response.content_part.done", ["item_id"] = _textId, ["output_index"] = _textIndex, ["content_index"] = 0, ["part"] = item["content"]![0]!.DeepClone() }), Event("response.output_item.done", new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = _textIndex, ["item"] = item.DeepClone() })];
    }

    private IEnumerable<string> FinalizeTool(ToolState state)
    {
        if (!state.Added || state.ItemId.Length == 0) return [];
        var arguments = state.Arguments.Length == 0 ? "{}" : state.Arguments;
        var item = ToolItem(state, "completed", arguments);
        _output.Add((state.OutputIndex, item));
        var events = new List<string>();
        if (_context.IsCustom(state.Name))
        {
            var input = CodexChatTransform.CustomInput(arguments);
            events.Add(Event("response.custom_tool_call_input.delta", new JsonObject { ["type"] = "response.custom_tool_call_input.delta", ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["delta"] = input }));
            events.Add(Event("response.custom_tool_call_input.done", new JsonObject { ["type"] = "response.custom_tool_call_input.done", ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["input"] = input }));
        }
        else events.Add(Event("response.function_call_arguments.done", new JsonObject { ["type"] = "response.function_call_arguments.done", ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["arguments"] = arguments }));
        events.Add(Event("response.output_item.done", new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = state.OutputIndex, ["item"] = item.DeepClone() }));
        return events;
    }

    private JsonObject ToolItem(ToolState state, string status, string arguments)
    {
        var spec = _context.Lookup(state.Name);
        if (spec?.Kind == CodexToolContext.ToolKind.Custom)
            return new JsonObject { ["id"] = state.ItemId, ["type"] = "custom_tool_call", ["status"] = status, ["call_id"] = state.Id, ["name"] = spec.Name, ["input"] = CodexChatTransform.CustomInput(arguments) };
        if (spec?.Kind == CodexToolContext.ToolKind.ToolSearch)
            return new JsonObject { ["type"] = "tool_search_call", ["call_id"] = state.Id, ["status"] = status, ["execution"] = "client", ["arguments"] = CodexChatTransform.ParseObject(arguments) };
        var item = new JsonObject { ["id"] = state.ItemId, ["type"] = "function_call", ["status"] = status, ["call_id"] = state.Id, ["name"] = spec?.Name ?? state.Name, ["arguments"] = arguments };
        if (!string.IsNullOrEmpty(spec?.Namespace)) item["namespace"] = spec.Namespace;
        if (!string.IsNullOrWhiteSpace(state.Reasoning)) item["reasoning_content"] = state.Reasoning;
        return item;
    }

    private JsonObject BaseResponse(string status, JsonArray output) => new()
    {
        ["id"] = _responseId,
        ["object"] = "response",
        ["created_at"] = _created,
        ["status"] = status,
        ["model"] = _model,
        ["output"] = output,
        ["usage"] = _usage.Count == 0 ? new JsonObject { ["input_tokens"] = 0, ["output_tokens"] = 0, ["total_tokens"] = 0, ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 0 }, ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 0 } } : _usage.DeepClone()
    };

    private static JsonObject Usage(JsonObject usage) => new()
    {
        ["input_tokens"] = GetLong(usage, "prompt_tokens"),
        ["output_tokens"] = GetLong(usage, "completion_tokens"),
        ["total_tokens"] = GetLong(usage, "total_tokens") == 0 ? GetLong(usage, "prompt_tokens") + GetLong(usage, "completion_tokens") : GetLong(usage, "total_tokens"),
        ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = usage["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0 },
        ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = usage["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<long>() ?? 0 }
    };
    private static string Event(string name, JsonObject body) => $"event: {name}\ndata: {body.ToJsonString(new JsonSerializerOptions { WriteIndented = false })}\n\n";
    private static string GetString(JsonObject? obj, string key) => obj?[key]?.GetValue<string>() ?? string.Empty;
    private static long GetLong(JsonObject? obj, string key, long fallback = 0) => obj?[key] is JsonValue value && value.TryGetValue<long>(out var result) ? result : fallback;
}
