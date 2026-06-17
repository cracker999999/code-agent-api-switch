using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

public class ApiTestService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

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
            _ => new ApiTestResult
            {
                Success = false,
                Message = "未知的工具类型"
            }
        };
    }

    private async Task<ApiTestResult> TestCodexAsync(Provider provider)
    {
        var url = $"{provider.BaseUrl.TrimEnd('/')}/responses";
        var model = string.IsNullOrWhiteSpace(provider.TestModel)
            ? "gpt-5.3-codex"
            : provider.TestModel.Trim();

        // 每次请求生成新的会话标识,避免硬编码值被服务端识别为"重放/伪造"
        var sessionId = Guid.NewGuid().ToString();
        var turnId = Guid.NewGuid().ToString();
        var body = BuildCodexRequestBody(model, sessionId);

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
        request.Headers.TryAddWithoutValidation("user-agent", "codex-tui/0.120.0 (Windows 10.0.19045; x86_64) WindowsTerminal (codex-tui; 0.120.0)");
        request.Headers.TryAddWithoutValidation("originator", "codex_tui");

        return await SendAndReadFirstChunkAsync(request);
    }

    private async Task<ApiTestResult> TestClaudeAsync(Provider provider)
    {
        var url = $"{provider.BaseUrl.TrimEnd('/')}/v1/messages";
        var model = string.IsNullOrWhiteSpace(provider.TestModel)
            ? "claude-opus-4-6"
            : provider.TestModel.Trim();
        var body = BuildClaudeRequestBody(model);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");
        request.Headers.TryAddWithoutValidation("x-api-key", provider.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Headers.TryAddWithoutValidation("anthropic-beta", "claude-code-20250219,interleaved-thinking-2025-05-14");
        request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("accept-encoding", "identity");
        request.Headers.TryAddWithoutValidation("accept-language", "*");
        request.Headers.TryAddWithoutValidation("user-agent", "claude-cli/2.1.77 (external, cli)");
        request.Headers.TryAddWithoutValidation("x-app", "cli");

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
            var buffer = new byte[1];
            var readCount = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (readCount > 0)
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

    private static string BuildCodexRequestBody(string model, string sessionId)
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
                            ["text"] = "你是什么模型"
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
            ["reasoning"] = new JsonObject { ["effort"] = "medium" },
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

    private static string BuildClaudeRequestBody(string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 1,
            ["stream"] = true,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "你是什么模型"
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
