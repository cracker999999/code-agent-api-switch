using System.Text.Json.Nodes;
using static APISwitch.Services.ChatProxyTransport;

namespace APISwitch.Services;

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
                events.Add(BlockEvent("content_block_delta", _textBlockIndex!.Value, "delta", new JsonObject
                {
                    ["type"] = "text_delta",
                    ["text"] = text
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
            events.Add(BlockEvent("content_block_stop", blockIndex));
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
        events.Add(BlockEvent("content_block_start", _textBlockIndex.Value, "content_block", new JsonObject
        {
            ["type"] = "text",
            ["text"] = string.Empty
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
        events.Add(BlockEvent("content_block_start", state.BlockIndex, "content_block", new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = state.Id,
            ["name"] = state.Name,
            ["input"] = new JsonObject()
        }));
        if (!string.IsNullOrEmpty(state.PendingArguments))
        {
            events.Add(ToolArgumentsDelta(state, state.PendingArguments));
            state.PendingArguments = string.Empty;
        }
    }

    private static string ToolArgumentsDelta(ToolState state, string arguments)
    {
        return BlockEvent("content_block_delta", state.BlockIndex, "delta", new JsonObject
        {
            ["type"] = "input_json_delta",
            ["partial_json"] = arguments
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
