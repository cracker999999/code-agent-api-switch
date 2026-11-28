using System.Text.Json;
using System.Text.Json.Nodes;

namespace APISwitch.Services;

internal sealed class ChatToCodexStream : IChatProxyStreamState
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

    public ChatToCodexStream(CodexToolContext context) => _context = context;

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
