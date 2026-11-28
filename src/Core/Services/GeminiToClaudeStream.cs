using System.Text.Json;
using System.Text.Json.Nodes;
using static APISwitch.Services.ChatProxyTransport;

namespace APISwitch.Services;

internal sealed class GeminiToClaudeStream : IChatProxyStreamState
{
    private readonly string _model;
    private readonly string _scope;
    private readonly GeminiToolCallStore _store;
    private readonly List<JsonObject> _tools = new();
    private readonly JsonObject _metadata = new();
    private string _finishReason = string.Empty;
    private bool _blocked;
    private bool _started;
    private bool _textOpen;
    private int _nextIndex;

    internal GeminiToClaudeStream(string model, GeminiToolCallStore store, string scope)
    {
        _model = model;
        _store = store;
        _scope = scope;
    }

    public bool IsCompleted { get; private set; }

    public IEnumerable<string> Handle(JsonObject chunk)
    {
        if (IsCompleted)
            return [];
        try
        {
            var (candidate, blocked, finish) = ClaudeGeminiTransform.ReadResponse(chunk);
            foreach (var field in new[] { "responseId", "modelVersion", "usageMetadata" })
            {
                if (chunk[field] is { } value)
                    _metadata[field] = value.DeepClone();
            }
            var events = new List<string>();
            EnsureStarted(events);
            if (blocked.Length > 0)
            {
                _blocked = true;
                AddText(events, $"Gemini 拒绝请求：{blocked}");
            }
            if (candidate is not null)
            {
                if (finish.Length > 0 && finish != "FINISH_REASON_UNSPECIFIED")
                    _finishReason = finish;
                foreach (var part in ClaudeGeminiTransform.ReadParts(candidate))
                {
                    if (part["functionCall"] is JsonObject call)
                    {
                        var id = GetString(call, "id");
                        var previous = id.Length == 0 ? null : _tools.FirstOrDefault(p =>
                            GetString(p["functionCall"] as JsonObject, "id") == id);
                        if (previous is null)
                            _tools.Add(part.DeepClone().AsObject());
                        else
                        {
                            // 带 ID 的累计快照可更新参数，但后续缺省的签名不能覆盖已有签名。
                            foreach (var field in part)
                            {
                                if (field.Value is not null)
                                    previous[field.Key] = field.Value.DeepClone();
                            }
                        }
                    }
                    else
                        AddText(events, GetString(part, "text"));
                }
            }
            else if (!_blocked && chunk["usageMetadata"] is null)
                return Fail("Gemini 响应缺少 candidates");
            return events;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return Fail(ex.Message);
        }
    }

    public IEnumerable<string> Complete()
    {
        if (IsCompleted)
            return [];
        // EOF 不能伪装成正常完成，否则半截工具调用可能被 Claude 执行。
        if (!_blocked && _finishReason.Length == 0)
            return Fail("Gemini 流提前结束，未收到 finishReason");
        try
        {
            var stopReason = _blocked ? "refusal" : ClaudeGeminiTransform.FinishReason(_finishReason, _tools.Count > 0);
            var events = new List<string>();
            EnsureStarted(events);
            if (_textOpen)
                events.Add(BlockEvent("content_block_stop", 0));
            foreach (var part in _tools)
            {
                var tool = ClaudeGeminiTransform.ToolUse(part, _store, _scope);
                var input = tool["input"]!.ToJsonString();
                tool["input"] = new JsonObject();
                var index = _nextIndex++;
                events.Add(BlockEvent("content_block_start", index, "content_block", tool));
                events.Add(BlockEvent("content_block_delta", index, "delta", new JsonObject
                {
                    ["type"] = "input_json_delta",
                    ["partial_json"] = input
                }));
                events.Add(BlockEvent("content_block_stop", index));
            }
            events.Add(Event("message_delta", new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject { ["stop_reason"] = stopReason, ["stop_sequence"] = null },
                // Gemini 常在尾帧才返回输入/缓存计数，终态必须补全这些 usage 字段。
                ["usage"] = ClaudeGeminiTransform.Usage(_metadata["usageMetadata"] as JsonObject)
            }));
            events.Add(Event("message_stop", new JsonObject { ["type"] = "message_stop" }));
            IsCompleted = true;
            return events;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return Fail(ex.Message);
        }
    }

    public IEnumerable<string> Fail(string message)
    {
        if (IsCompleted)
            return [];
        IsCompleted = true;
        return [Event("error", new JsonObject
        {
            ["type"] = "error",
            ["error"] = new JsonObject { ["type"] = "api_error", ["message"] = message }
        })];
    }

    private void EnsureStarted(List<string> events)
    {
        if (_started)
            return;
        var message = ClaudeGeminiTransform.Message(_metadata, _model, new JsonArray(), null);
        message["usage"]!["output_tokens"] = 0;
        events.Add(Event("message_start", new JsonObject { ["type"] = "message_start", ["message"] = message }));
        _started = true;
    }

    private void AddText(List<string> events, string text)
    {
        if (text.Length == 0)
            return;
        if (!_textOpen)
        {
            _textOpen = true;
            _nextIndex++;
            events.Add(BlockEvent("content_block_start", 0, "content_block",
                new JsonObject { ["type"] = "text", ["text"] = string.Empty }));
        }
        events.Add(BlockEvent("content_block_delta", 0, "delta",
            new JsonObject { ["type"] = "text_delta", ["text"] = text }));
    }
}
