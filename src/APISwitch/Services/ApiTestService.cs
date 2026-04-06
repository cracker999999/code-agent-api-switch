using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
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
        const string body = "{\"model\":\"gpt-5.3-codex\",\"input\":[{\"role\":\"user\",\"content\":\"你是什么模型\"}],\"stream\":true}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");
        request.Headers.TryAddWithoutValidation("accept", "text/event-stream");
        // request.Headers.TryAddWithoutValidation("accept-encoding", "identity");
        request.Headers.TryAddWithoutValidation("user-agent", "codex-tui/0.118.0 (Windows 10.0.19045; x86_64) WindowsTerminal (codex-tui; 0.118.0)");
        request.Headers.TryAddWithoutValidation("originator", "codex_tui");

        return await SendAndReadFirstChunkAsync(request);
    }

    private async Task<ApiTestResult> TestClaudeAsync(Provider provider)
    {
        var url = $"{provider.BaseUrl.TrimEnd('/')}/v1/messages";
        const string body = "{\"model\":\"claude-opus-4-6\",\"max_tokens\":1,\"messages\":[{\"role\":\"user\",\"content\":\"你是什么模型\"}],\"stream\":true}";

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
}

