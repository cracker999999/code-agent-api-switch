using System.Net.Http;
using System.Text.Json;
using APISwitch.Models;

namespace APISwitch.Services;

public class ModelDiscoveryService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<ModelDiscoveryResult> GetModelsAsync(Provider provider)
    {
        if (provider is null)
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = "供应商信息为空"
            };
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = "BaseUrl 或 ApiKey 为空"
            };
        }

        var requestUrl = BuildModelsUrl(provider.BaseUrl);

        using var client = new HttpClient
        {
            Timeout = Timeout
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("authorization", $"Bearer {provider.ApiKey}");

        try
        {
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new ModelDiscoveryResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {responseText}"
                };
            }

            var models = ParseModels(responseText);
            return new ModelDiscoveryResult
            {
                Success = true,
                Models = models
            };
        }
        catch (TaskCanceledException)
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = "请求超时（30 秒）"
            };
        }
        catch (HttpRequestException ex)
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = $"连接失败：{ex.Message}"
            };
        }
        catch (JsonException ex)
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = $"响应解析失败：{ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ModelDiscoveryResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
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
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
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
