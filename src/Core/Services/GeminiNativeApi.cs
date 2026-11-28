using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using APISwitch.Models;

namespace APISwitch.Services;

internal static partial class GeminiNativeApi
{
    [GeneratedRegex(@"/v\d+(?:(?:beta|alpha)\d*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    internal static Uri BuildUrl(string baseUrl, string resource, string? queryName = null, string? queryValue = null)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || string.IsNullOrEmpty(address.Host))
            throw new InvalidOperationException("Gemini BaseUrl 必须是 HTTP 或 HTTPS 地址");
        var uri = new UriBuilder(address);

        var path = uri.Path.TrimEnd('/');
        // 支持根地址、带网关前缀的地址和已包含 API 版本的地址，避免重复拼接版本。
        if (!VersionSuffix().IsMatch(path))
            path += "/v1beta";
        uri.Path = $"{path}/{resource}";
        if (queryName is not null)
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            query[queryName] = queryValue;
            uri.Query = query.ToString();
        }
        return uri.Uri;
    }

    internal static HttpRequestMessage CreateRequest(Provider provider, ClaudeChatRequest transformed)
    {
        var method = transformed.Stream ? "streamGenerateContent" : "generateContent";
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(
            provider.BaseUrl,
            $"models/{Uri.EscapeDataString(transformed.Model)}:{method}",
            transformed.Stream ? "alt" : null, "sse"))
        {
            Content = new StringContent(transformed.Body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        AddHeaders(request, provider);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            transformed.Stream ? "text/event-stream" : "application/json"));
        return request;
    }

    internal static void AddHeaders(HttpRequestMessage request, Provider provider)
    {
        request.Headers.Add("x-goog-api-key", provider.ApiKey);
    }

    internal static string NormalizeModel(string model)
    {
        model = model.Trim();
        return model.StartsWith("models/", StringComparison.Ordinal) ? model[7..] : model;
    }

    internal static string Scope(Provider provider, string model) =>
        $"{provider.Id}\n{provider.BaseUrl}\n{NormalizeModel(model)}";
}
