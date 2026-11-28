using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

/// Local Responses -> Chat Completions bridge for Codex providers that expose only /chat/completions.
public sealed class CodexProxyService : IDisposable
{
    public const string LocalBaseUrl = "http://127.0.0.1:24821";

    private readonly ProxyHost _host;

    public CodexProxyService()
    {
        _host = new ProxyHost(LocalBaseUrl, 0, "Codex", HandleRequestAsync);
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
                new ChatToCodexStream(transformed.Context));
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
        var state = new ChatToCodexStream(context);
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
