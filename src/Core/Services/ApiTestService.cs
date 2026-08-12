using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Extensions;
using APISwitch.Models;

namespace APISwitch.Services;

public class ApiTestService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly AppSettingsService _settingsService;

    public ApiTestService(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<ApiTestResult> TestProviderAsync(Provider provider)
    {
        if (provider is null)
        {
            return new ApiTestResult
            {
                Success = false,
                Message = "供应商信息为空"
            };
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return new ApiTestResult
            {
                Success = false,
                Message = "BaseUrl 或 ApiKey 为空"
            };
        }

        return provider.ToolType switch
        {
            0 => await TestCodexAsync(provider),
            1 => await TestClaudeAsync(provider),
            2 => await TestGrokAsync(provider),
            _ => new ApiTestResult
            {
                Success = false,
                Message = "未知的工具类型"
            }
        };
    }

    private async Task<ApiTestResult> TestCodexAsync(Provider provider)
    {
        var settings = _settingsService.Load();
        var url = $"{provider.BaseUrl.TrimEnd('/')}{settings.CodexEndpointPath}";
        var model = provider.GetEffectiveTestModel(settings);

        // 每次请求生成新的会话标识,避免硬编码值被服务端识别为"重放/伪造"
        var sessionId = Guid.NewGuid().ToString();
        var turnId = Guid.NewGuid().ToString();
        var body = BuildCodexRequestBody(model, sessionId, settings.CodexPromptText);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var turnMetadata = $@"{{""session_id"":""{sessionId}"",""turn_id"":""{turnId}"",""sandbox"":""none""}}";
        request.Headers.TryAddWithoutValidation("x-codex-turn-metadata", turnMetadata);
        request.Headers.TryAddWithoutValidation("x-codex-window-id", $"{sessionId}:0");
        request.Headers.TryAddWithoutValidation("x-client-request-id", sessionId);
        request.Headers.TryAddWithoutValidation("session_id", sessionId);
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");
        request.Headers.TryAddWithoutValidation("accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation("user-agent", $"codex-tui/{settings.CodexVersion} (Windows 10.0.19045; x86_64) WindowsTerminal (codex-tui; {settings.CodexVersion})");
        request.Headers.TryAddWithoutValidation("originator", "codex_tui");

        return await SendAndReadFirstChunkAsync(request);
    }

    private async Task<ApiTestResult> TestClaudeAsync(Provider provider)
    {
        var settings = _settingsService.Load();
        // 真实 Claude Code 调用的是 /v1/messages?beta=true,部分中转站会校验该查询参数
        var url = $"{provider.BaseUrl.TrimEnd('/')}{settings.ClaudeEndpointPath}";
        var model = provider.GetEffectiveTestModel(settings);
        var sessionId = Guid.NewGuid().ToString();
        var body = BuildClaudeRequestBody(model, sessionId, settings.ClaudePromptText);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        // 与真实 Claude Code 2.1.145 完全一致的 beta 列表(顺序也保持一致)
        request.Headers.TryAddWithoutValidation("anthropic-beta", "claude-code-20250219,interleaved-thinking-2025-05-14,context-1m-2025-08-07,effort-2025-11-24");
        request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("user-agent", $"claude-cli/{settings.ClaudeVersion} (external, cli)");
        request.Headers.TryAddWithoutValidation("x-app", "cli");
        request.Headers.TryAddWithoutValidation("x-claude-code-session-id", sessionId);
        // Stainless SDK 系列指纹 header(Claude Code 走官方 @anthropic-ai/sdk 时自动注入)
        request.Headers.TryAddWithoutValidation("x-stainless-lang", "js");
        request.Headers.TryAddWithoutValidation("x-stainless-package-version", "0.94.0");
        request.Headers.TryAddWithoutValidation("x-stainless-os", "Windows");
        request.Headers.TryAddWithoutValidation("x-stainless-arch", "x64");
        request.Headers.TryAddWithoutValidation("x-stainless-runtime", "node");
        request.Headers.TryAddWithoutValidation("x-stainless-runtime-version", "v24.3.0");
        request.Headers.TryAddWithoutValidation("x-stainless-retry-count", "0");
        request.Headers.TryAddWithoutValidation("x-stainless-timeout", "60");

        return await SendAndReadFirstChunkAsync(request);
    }

    private async Task<ApiTestResult> TestGrokAsync(Provider provider)
    {
        var settings = _settingsService.Load();
        var url = $"{provider.BaseUrl.TrimEnd('/')}{settings.GrokEndpointPath}";
        var model = provider.GetEffectiveTestModel(settings);
        // 与真实 grok-shell 一致: conv/session 同一 UUID, req/agent 各独立生成
        var sessionId = Guid.NewGuid().ToString();
        var reqId = Guid.NewGuid().ToString();
        var agentId = Guid.NewGuid().ToString();
        var body = BuildGrokRequestBody(model, settings.GrokPromptText);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // 请求头对齐 grok-shell/0.2.111 真实抓包
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");
        request.Headers.TryAddWithoutValidation("accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation("user-agent", $"grok-shell/{settings.GrokVersion} (windows; x86_64)");
        request.Headers.TryAddWithoutValidation("x-xai-token-auth", "xai-grok-cli");
        request.Headers.TryAddWithoutValidation("x-authenticateresponse", "authenticate-response");
        request.Headers.TryAddWithoutValidation("x-grok-client-mode", "headless");
        request.Headers.TryAddWithoutValidation("x-grok-client-version", settings.GrokVersion);
        request.Headers.TryAddWithoutValidation("x-grok-client-identifier", "grok-shell");
        request.Headers.TryAddWithoutValidation("x-grok-conv-id", sessionId);
        request.Headers.TryAddWithoutValidation("x-grok-req-id", reqId);
        request.Headers.TryAddWithoutValidation("x-grok-model-override", model);
        request.Headers.TryAddWithoutValidation("x-grok-session-id", sessionId);
        request.Headers.TryAddWithoutValidation("x-grok-agent-id", agentId);
        request.Headers.TryAddWithoutValidation("x-grok-turn-idx", "1");

        return await SendAndReadFirstChunkAsync(request);
    }

    private static async Task<ApiTestResult> SendAndReadFirstChunkAsync(HttpRequestMessage request)
    {
        using var client = new HttpClient
        {
            Timeout = Timeout
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorMessage = string.IsNullOrWhiteSpace(errorContent)
                    ? response.ReasonPhrase ?? "请求失败"
                    : errorContent;

                return new ApiTestResult
                {
                    Success = false,
                    Message = $"HTTP {(int)response.StatusCode}: {errorMessage}"
                };
            }

            // 成功的流式响应应当是 text/event-stream;有的中转站会返回 200 + application/json
            // 但 body 实际是错误对象(例如 {"error":{"message":"Unsafe upstream URL"}}),
            // 此时不能算成功,需要解析 JSON 把错误信息透出来。
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var jsonBody = await response.Content.ReadAsStringAsync();
                var extracted = TryExtractErrorMessage(jsonBody);
                return new ApiTestResult
                {
                    Success = false,
                    Message = extracted ?? (string.IsNullOrWhiteSpace(jsonBody) ? "未收到有效流式数据" : jsonBody)
                };
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            return await InspectSseHeadAsync(stream, stopwatch);
        }
        catch (TaskCanceledException)
        {
            return new ApiTestResult
            {
                Success = false,
                Message = "请求超时（30 秒）"
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiTestResult
            {
                Success = false,
                Message = $"连接失败：{ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ApiTestResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    // 读取 SSE 流头部若干行用于判定真实结果。
    // 有的中转站会返回 200 + text/event-stream,但实际内容是:
    //   {"id":"chatcmpl-dummy",...,"choices":[{"delta":{"content":""}}]}  ← 占位空 chunk
    //   {"type":"error","error":{...},"status_code":429,...}              ← 真正的错误
    // 仅凭"读到第一个字节"判成功会被这种伪流欺骗,因此需要解析前 N 行 JSON,
    // 命中 error 才能返回失败。
    private static async Task<ApiTestResult> InspectSseHeadAsync(Stream stream, Stopwatch stopwatch)
    {
        const int MaxBytes = 16 * 1024;
        const int MaxLines = 50;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var totalBytes = 0;
        var linesRead = 0;
        var hasAnyPayload = false;

        while (linesRead < MaxLines && totalBytes < MaxBytes)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                // 流提前结束
                break;
            }

            linesRead++;
            totalBytes += line.Length + 1;

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            // SSE 注释行
            if (trimmed.StartsWith(':')) continue;
            // event: / id: / retry: 这些字段行不携带 JSON 负载
            if (trimmed.StartsWith("event:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("retry:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 兼容标准 SSE 的 "data: {...}" 和裸 JSON 行两种情况
            var payload = trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(5).TrimStart()
                : trimmed;

            if (payload.Length == 0 || payload == "[DONE]") continue;
            if (payload[0] != '{' && payload[0] != '[') continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(payload);
            }
            catch (JsonException)
            {
                continue;
            }
            if (node is null) continue;

            hasAnyPayload = true;

            var errorMessage = TryExtractSseErrorMessage(node);
            if (errorMessage is not null)
            {
                return new ApiTestResult
                {
                    Success = false,
                    Message = errorMessage
                };
            }
        }

        if (hasAnyPayload)
        {
            stopwatch.Stop();
            return new ApiTestResult
            {
                Success = true,
                Message = string.Empty,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            };
        }

        return new ApiTestResult
        {
            Success = false,
            Message = "未收到有效流式数据"
        };
    }

    // 识别 SSE 中的错误事件。命中规则:
    // 1) 顶层 type == "error"
    // 2) 顶层存在 error 对象,且含 message / type 字段
    // 3) 顶层存在 status_code 且为非 2xx
    private static string? TryExtractSseErrorMessage(JsonNode node)
    {
        var typeValue = node["type"]?.GetValue<string>();
        var isErrorType = string.Equals(typeValue, "error", StringComparison.OrdinalIgnoreCase);

        var errorNode = node["error"];
        var hasErrorObject = errorNode is JsonObject;

        int? statusCode = null;
        var statusNode = node["status_code"];
        if (statusNode is not null)
        {
            try { statusCode = statusNode.GetValue<int>(); }
            catch { /* 容错:status_code 可能是字符串或缺失 */ }
        }
        var hasBadStatus = statusCode.HasValue && (statusCode.Value < 200 || statusCode.Value >= 300);

        if (!isErrorType && !hasErrorObject && !hasBadStatus) return null;

        // 优先取 error.message
        var message = errorNode?["message"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            message = node["message"]?.GetValue<string>();
        }

        var errorSubType = errorNode?["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(errorSubType))
        {
            message = errorSubType;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            // 兜底返回原始 JSON 片段,避免吞掉服务端信息
            message = node.ToJsonString();
        }

        if (statusCode.HasValue)
        {
            message = $"HTTP {statusCode.Value}: {message}";
        }
        else if (!string.IsNullOrWhiteSpace(errorSubType) &&
                 message!.IndexOf(errorSubType, StringComparison.OrdinalIgnoreCase) < 0)
        {
            message = $"{errorSubType}: {message}";
        }

        return message;
    }

    private static string BuildCodexRequestBody(string model, string sessionId, string promptText)
    {
        // 模拟真实 codex 请求体的关键顶层字段。中转站常以"是否包含 instructions / tools / reasoning 等
        // codex 特有字段"作为指纹判定客户端,缺一就被拒。这里给出最小够用的形态:
        // - instructions / tools / input 内容尽量短,够通过校验即可
        // - 其余顶层字段(reasoning/text/include/store/tool_choice/parallel_tool_calls)都补齐
        var payload = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = "You are Codex, a coding agent based on GPT-5.",
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = promptText
                        }
                    }
                }
            },
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "function",
                    ["name"] = "shell_command",
                    ["description"] = "Execute a shell command.",
                    ["strict"] = false,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["command"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        },
                        ["required"] = new JsonArray { "command" },
                        ["additionalProperties"] = false
                    }
                }
            },
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = true,
            ["reasoning"] = new JsonObject { ["effort"] = "xhigh" },
            ["store"] = false,
            ["stream"] = true,
            ["include"] = new JsonArray { "reasoning.encrypted_content" },
            ["prompt_cache_key"] = sessionId,
            ["text"] = new JsonObject { ["verbosity"] = "low" },
            ["client_metadata"] = new JsonObject
            {
                ["x-codex-installation-id"] = "017b29c7-8457-4137-803e-bc6df3830f11"
            }
        };

        return payload.ToJsonString();
    }

    private static string BuildClaudeRequestBody(string model, string sessionId, string promptText)
    {
        // 中转站普遍以 system[0].text 中是否含 Claude Code 客户端特征字符串作为指纹判定。
        // 以下精确字符串与真实 Claude Code 2.1.145 发出的内容一致,改动会触发"only allows Claude Code clients"。
        const string ClaudeCodeIdentitySystem = "You are a Claude agent, built on Anthropic's Claude Agent SDK.";
        // metadata.user_id 是 Claude Code 的设备/会话标识,值是 JSON 字符串而非裸 ID
        var userIdJson = new JsonObject
        {
            ["device_id"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ["account_uuid"] = string.Empty,
            ["session_id"] = sessionId
        }.ToJsonString();

        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 1,
            ["stream"] = true,
            ["system"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = ClaudeCodeIdentitySystem,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = promptText
                        }
                    }
                }
            },
            ["metadata"] = new JsonObject
            {
                ["user_id"] = userIdJson
            }
        };

        return payload.ToJsonString();
    }

    private static string BuildGrokRequestBody(string model, string promptText)
    {
        // 对齐真实 grok-shell 主对话 POST /v1/responses 请求体
        // 不含 metadata(中转站会 400)、不含 tools(连通性测试不需要)
        var payload = new JsonObject
        {
            ["model"] = model,
            ["stream"] = true,
            ["store"] = false,
            ["include"] = new JsonArray { "reasoning.encrypted_content" },
            ["reasoning"] = new JsonObject
            {
                ["effort"] = "high",
                ["summary"] = "concise"
            },
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = promptText
                }
            }
        };

        return payload.ToJsonString();
    }

    // 从中转站常见的错误 JSON 中提取人类可读的 message。
    // 兼容 {"error":{"message":"..."}} 和 {"message":"..."} 两种结构。
    private static string? TryExtractErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            var errorMessage = node["error"]?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(errorMessage)) return errorMessage;

            var topMessage = node["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(topMessage)) return topMessage;
        }
        catch (JsonException)
        {
            // 不是合法 JSON,交给上层用原文兜底
        }
        return null;
    }
}
