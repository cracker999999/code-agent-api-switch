using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

/// <summary>把 Claude Messages API 转换为供应商选择的上游协议。</summary>
public sealed class ClaudeProxyService : IDisposable
{
    public const string LocalBaseUrl = "http://127.0.0.1:24822";

    private readonly ProxyHost _host;
    private readonly GeminiToolCallStore _geminiToolCalls = new();

    public ClaudeProxyService() : this(LocalBaseUrl)
    {
    }

    internal ClaudeProxyService(string localBaseUrl)
    {
        _host = new ProxyHost(localBaseUrl, 1, "Claude", HandleRequestAsync);
    }

    public void Start(Provider provider)
    {
        if (provider.UseGeminiNative && provider.UseChatCompletions)
            throw new InvalidOperationException("Gemini Native 和 Chat Completions 适配不能同时启用");
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
            var geminiScope = string.Empty;
            try
            {
                var requestBody = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
                geminiScope = GeminiNativeApi.Scope(provider, ChatProxyTransport.GetString(requestBody, "model"));
                transformed = provider.UseGeminiNative
                    ? ClaudeGeminiTransform.ToGenerateContent(requestBody, _geminiToolCalls, geminiScope)
                    : ClaudeChatTransform.ToChatCompletions(requestBody);
            }
            catch (InvalidOperationException ex)
            {
                await WriteErrorAsync(context.Response, 400, ex.Message, "invalid_request_error");
                return;
            }

            using var geminiRequest = provider.UseGeminiNative ? GeminiNativeApi.CreateRequest(provider, transformed) : null;
            using var upstreamResponse = geminiRequest is not null
                ? await ChatProxyTransport.SendAsync(_host.HttpClient, geminiRequest)
                : await ChatProxyTransport.SendAsync(_host.HttpClient, provider, transformed.Body, transformed.Stream);

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                var errorText = await upstreamResponse.Content.ReadAsStringAsync();
                await WriteUpstreamErrorAsync(context.Response, (int)upstreamResponse.StatusCode, errorText);
                return;
            }

            if (provider.UseGeminiNative)
            {
                var state = new GeminiToClaudeStream(transformed.Model, _geminiToolCalls, geminiScope);
                if (transformed.Stream && string.Equals(
                    upstreamResponse.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await ChatProxyTransport.RelaySseAsync(context.Response, upstreamResponse.Content, state, failOnInvalidJson: true);
                    }
                    catch (Exception ex)
                    {
                        // SSE 已开始后只能发送 error 事件，不能再把 HTTP 响应改成 JSON。
                        await ChatProxyTransport.WriteEventsAsync(context.Response, state.Fail(ex.Message));
                    }
                }
                else
                {
                    var text = await upstreamResponse.Content.ReadAsStringAsync();
                    var body = ChatProxyTransport.ParseJsonObject(text)
                        ?? throw new InvalidOperationException("Gemini 响应不是有效 JSON 对象");
                    if (transformed.Stream)
                    {
                        ChatProxyTransport.BeginSse(context.Response);
                        await ChatProxyTransport.WriteEventsAsync(context.Response, state.Handle(body));
                        await ChatProxyTransport.WriteEventsAsync(context.Response, state.Complete());
                    }
                    else
                        await ChatProxyTransport.WriteJsonAsync(context.Response,
                            ClaudeGeminiTransform.ToMessages(body, transformed.Model, _geminiToolCalls, geminiScope));
                }
                return;
            }

            if (transformed.Stream && string.Equals(
                    upstreamResponse.Content.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase))
            {
                await ChatProxyTransport.RelaySseAsync(
                    context.Response,
                    upstreamResponse.Content,
                    new ChatToClaudeStream(transformed.Model));
                return;
            }

            var responseText = await upstreamResponse.Content.ReadAsStringAsync();
            var chatResponse = JsonNode.Parse(responseText)?.AsObject()
                ?? throw new InvalidOperationException("上游 Chat Completions 响应不是 JSON 对象");
            if (transformed.Stream)
                await StreamSingleResponseAsync(context.Response, chatResponse, transformed.Model);
            else
                await ChatProxyTransport.WriteJsonAsync(
                    context.Response,
                    ClaudeChatTransform.ToMessages(chatResponse, transformed.Model));
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
