using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using APISwitch.Models;

namespace APISwitch.Services;

public class ModelDiscoveryService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ApiTestService _apiTestService;

    private static ModelDiscoveryResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };

    public ModelDiscoveryService(ApiTestService apiTestService)
    {
        _apiTestService = apiTestService;
    }

    public async IAsyncEnumerable<ModelSearchResult> FindProvidersByModelAsync(
        IEnumerable<Provider> providers,
        string modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        modelId = modelId.Trim();
        cancellationToken.ThrowIfCancellationRequested();

        // 每批并发查询 10 个供应商，等待整批完成再继续；失败只计入该供应商的结果。
        foreach (var batch in providers.Chunk(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = batch.Select(async provider =>
                (Provider: provider, Result: await GetModelsAsync(provider, cancellationToken))).ToList();

            // 先返回已完成的结果，避免慢请求挡住同批其他供应商的展示。
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var (provider, result) = await completed;
                cancellationToken.ThrowIfCancellationRequested();
                // 与已有模型列表搜索一致：忽略大小写，模型 ID 包含输入即可命中。
                var matchingModels = result.Models
                    .Where(model => model.Contains(modelId, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                yield return new ModelSearchResult(provider, matchingModels, result.ErrorMessage);
            }
        }
    }

    public async Task<ModelDiscoveryResult> GetModelsAsync(
        Provider provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
        {
            return Fail("供应商信息为空");
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return Fail("BaseUrl 或 ApiKey 为空");
        }

        try
        {
            // 地址错误也属于单个供应商的失败，不能中断整批查询。
            var requestUrl = BuildModelsUrl(provider.BaseUrl);
            using var client = new HttpClient
            {
                Timeout = Timeout
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            // 复用 API 测试的客户端指纹，避免模型请求与实际调用被中转站区别处理。
            _apiTestService.AddRequestHeaders(request, provider);
            using var response = await client.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"HTTP {(int)response.StatusCode}: {responseText}");
            }

            var models = ParseModels(responseText);
            return new ModelDiscoveryResult
            {
                Success = true,
                Models = models
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户停止查询时向上传播取消，不能记为供应商请求失败。
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fail("请求超时（30 秒）");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"连接失败：{ex.Message}");
        }
        catch (JsonException ex)
        {
            return Fail($"响应解析失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static string BuildModelsUrl(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (normalized.Contains("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalized}/models";
        }

        return $"{normalized}/v1/models";
    }

    private static List<string> ParseModels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            // 异常响应不能当成空模型列表，否则会被误判为不提供目标模型。
            throw new JsonException("模型列表缺少 data 数组");
        }

        var models = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idElement))
            {
                var modelId = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(modelId))
                {
                    models.Add(modelId);
                }
            }
        }

        return models;
    }
}
