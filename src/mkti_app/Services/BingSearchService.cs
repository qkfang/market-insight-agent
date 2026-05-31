using System.Text.Json;

namespace mkti_app.Services;

public record BingSearchResult(string Title, string Snippet, string Url);

public sealed class BingSearchService
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BingSearchService> _logger;

    public BingSearchService(
        string apiKey,
        string endpoint,
        IHttpClientFactory httpClientFactory,
        ILogger<BingSearchService> logger)
    {
        _apiKey = apiKey ?? string.Empty;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "https://api.bing.microsoft.com/" : endpoint;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<BingSearchResult>> SearchAsync(string query, int count = 5)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("BING_SEARCH_API_KEY is not configured; Bing Search is disabled.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var requestUri = $"{_endpoint.TrimEnd('/')}/v7.0/search?q={Uri.EscapeDataString(query)}&count={count}&responseFilter=Webpages";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bing Search request failed with status {Status}", (int)response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseResults(json, count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bing Search request failed for query.");
            return [];
        }
    }

    private static IReadOnlyList<BingSearchResult> ParseResults(string json, int count)
    {
        var results = new List<BingSearchResult>();
        if (string.IsNullOrWhiteSpace(json))
            return results;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("webPages", out var webPages)
                && webPages.TryGetProperty("value", out var values)
                && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    var title = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    results.Add(new BingSearchResult(title, snippet, url));
                    if (results.Count >= count)
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed Bing responses.
        }

        return results;
    }
}
