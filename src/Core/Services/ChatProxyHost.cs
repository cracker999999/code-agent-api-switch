using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APISwitch.Models;

namespace APISwitch.Services;

internal sealed class ChatProxyHost : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _localBaseUrl;
    private readonly int _toolType;
    private readonly string _toolName;
    private readonly Func<HttpListenerContext, Provider?, Task> _requestHandler;
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Provider? _provider;

    internal ChatProxyHost(
        string localBaseUrl,
        int toolType,
        string toolName,
        Func<HttpListenerContext, Provider?, Task> requestHandler)
    {
        _localBaseUrl = localBaseUrl;
        _toolType = toolType;
        _toolName = toolName;
        _requestHandler = requestHandler;
    }

    internal HttpClient HttpClient { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    internal void Start(Provider provider)
    {
        if (provider.ToolType != _toolType)
        {
            throw new InvalidOperationException($"Chat Completions 适配只支持 {_toolName} 供应商");
        }

        HttpListener listener;
        CancellationToken cancellationToken;
        lock (_syncRoot)
        {
            _provider = provider;
            if (_listener is not null)
            {
                return;
            }

            listener = new HttpListener();
            listener.Prefixes.Add($"{_localBaseUrl.TrimEnd('/')}/");
            listener.IgnoreWriteExceptions = true;
            listener.Start();

            _listener = listener;
            _cancellation = new CancellationTokenSource();
            cancellationToken = _cancellation.Token;
        }

        _ = AcceptLoopAsync(listener, cancellationToken);
    }

    internal void Stop()
    {
        lock (_syncRoot)
        {
            _provider = null;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }
    }

    public void Dispose()
    {
        Stop();
        HttpClient.Dispose();
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => _requestHandler(context, GetProvider()), cancellationToken);
        }
    }

    private Provider? GetProvider()
    {
        lock (_syncRoot)
        {
            return _provider;
        }
    }
}

internal interface IChatProxyStreamState
{
    bool IsCompleted { get; }
    IEnumerable<string> Handle(JsonObject chunk);
    IEnumerable<string> Complete();
    IEnumerable<string> Fail(string message);
}

internal static class ChatProxyTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static string Serialize(JsonNode node)
    {
        return node.ToJsonString(JsonOptions);
    }

    private static Uri BuildChatUrl(string baseUrl)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}/chat/completions", UriKind.Absolute);
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        Provider provider,
        JsonObject body,
        bool stream)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatUrl(provider.BaseUrl))
        {
            Content = new StringContent(Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            stream ? "text/event-stream" : "application/json"));
        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);
    }

    internal static void BeginSse(HttpListenerResponse response)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.SendChunked = true;
        response.KeepAlive = false;
        response.Headers["Cache-Control"] = "no-cache";
    }

    internal static async Task WriteEventsAsync(HttpListenerResponse response, IEnumerable<string> events)
    {
        foreach (var item in events)
        {
            var bytes = Encoding.UTF8.GetBytes(item);
            await response.OutputStream.WriteAsync(bytes);
            await response.OutputStream.FlushAsync();
        }
    }

    internal static async Task WriteJsonAsync(
        HttpListenerResponse response,
        JsonObject body,
        int statusCode = 200)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString(JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    internal static async Task RelaySseAsync(
        HttpListenerResponse response,
        HttpContent content,
        IChatProxyStreamState state)
    {
        BeginSse(response);
        await foreach (var payload in ReadSsePayloadsAsync(content))
        {
            if (state.IsCompleted)
            {
                continue;
            }
            if (payload == "[DONE]")
            {
                await WriteEventsAsync(response, state.Complete());
                continue;
            }

            var chunk = ParseJsonObject(payload);
            if (chunk is null)
            {
                continue;
            }

            if (chunk["error"] is not null)
            {
                await WriteEventsAsync(response, state.Fail(ReadErrorMessage(chunk)));
                return;
            }

            await WriteEventsAsync(response, state.Handle(chunk));
        }

        if (!state.IsCompleted)
        {
            await WriteEventsAsync(response, state.Complete());
        }
    }

    private static async IAsyncEnumerable<string> ReadSsePayloadsAsync(HttpContent content)
    {
        await using var upstream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(upstream, Encoding.UTF8);
        var data = new List<string>();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    yield return string.Join("\n", data);
                    data.Clear();
                }
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line[5..].TrimStart());
            }
            else if (line.StartsWith('{'))
            {
                // 部分兼容供应商返回逐行 JSON，而不是标准 SSE data 帧。
                data.Add(line);
            }
        }

        if (data.Count > 0)
        {
            yield return string.Join("\n", data);
        }
    }

    internal static JsonObject? ParseJsonObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string ReadErrorMessage(JsonObject body)
    {
        var error = body["error"] as JsonObject;
        var message = GetString(error, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = GetString(body, "message");
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        var detail = GetString(error, "detail");
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = GetString(body, "detail");
        }
        return string.IsNullOrWhiteSpace(detail)
            ? (error ?? body).ToJsonString(JsonOptions)
            : detail;
    }

    internal static string GetString(JsonObject? value, string name)
    {
        return value?[name] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var result)
            ? result
            : string.Empty;
    }
}
