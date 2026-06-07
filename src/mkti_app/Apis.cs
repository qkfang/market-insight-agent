using mkti_app.Agents;
using mkti_app.Services;

namespace mkti_app;

public static class Apis
{
    private const string DataFolderName = "data";
    private const string KnowledgeArticlesFileName = "articles.json";
    private const string MockRssArticlesFileName = "articles-june.json";
    private const string MockRssFallbackFileName = "articles.json";
    private const int KnowledgeTopArticleCount = 3;
    private const int InsightPreviewMaxLength = 500;

    public static void MapAllEndpoints(
        this WebApplication app,
        NewsIngestionAgent newsIngestionAgent,
        NewsAnalysisAgent newsAnalysisAgent,
        MarketResearchAgent marketResearchAgent,
        InsightGenerationAgent insightGenerationAgent,
        BlobStorageService blobStorageService,
        FabricLakehouseService fabricLakehouseService)
    {
        app.MapGet("/api/news/ingest", async () =>
        {
            var before = await blobStorageService.ListBlobNamesAsync("news-store");
            var result = await newsIngestionAgent.RunAsync("Ingest local articles-xx.json and store each full article JSON object in news-store using {yyyyMMddHHmmssfff}_{guid}.json blob names.");
            var after = await blobStorageService.ListBlobNamesAsync("news-store");

            var delta = after.Count - before.Count;
            var articlesStored = Math.Max(0, delta);

            return Results.Json(new
            {
                success = true,
                articlesStored,
                filenames = after,
                message = result
            });
        });

        app.MapGet("/api/news/list", async () =>
        {
            var filenames = await blobStorageService.ListBlobNamesAsync("news-store");
            return Results.Json(new { success = true, filenames });
        });

        app.MapGet("/api/knowledge/run", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            // Step 1: Ingest (same as Ingest tab)
            var ingestResponse = await client.GetAsync($"{baseUrl}/api/news/ingest");
            ingestResponse.EnsureSuccessStatusCode();
            var ingestJson = await ingestResponse.Content.ReadAsStringAsync();
            var ingestResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(ingestJson);

            // Step 2: Analyze (same as Analyze tab)
            var analyzeResponse = await client.GetAsync($"{baseUrl}/api/news/analyze");
            analyzeResponse.EnsureSuccessStatusCode();
            var analyzeJson = await analyzeResponse.Content.ReadAsStringAsync();
            var analyzeResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(analyzeJson);

            // Step 3: Research (same as Research tab)
            var researchResponse = await client.GetAsync($"{baseUrl}/api/market/research");
            researchResponse.EnsureSuccessStatusCode();
            var researchJson = await researchResponse.Content.ReadAsStringAsync();
            var researchResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(researchJson);

            // Step 4: Generate insight (same as Generate tab)
            var generateResponse = await client.GetAsync($"{baseUrl}/api/insight/generate");
            generateResponse.EnsureSuccessStatusCode();
            var generateJson = await generateResponse.Content.ReadAsStringAsync();
            var generateResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(generateJson);

            return Results.Json(new
            {
                success = true,
                ingest = ingestResult,
                analysis = analyzeResult,
                research = researchResult,
                insight = generateResult
            });
        });

        app.MapGet("/api/news/analyze", async () =>
        {
            var before = new HashSet<string>(
                await blobStorageService.ListBlobNamesAsync("news-analysis"),
                StringComparer.OrdinalIgnoreCase);

            var result = await newsAnalysisAgent.RunAsync(
                "Analyze all unprocessed news articles and extract structured content.");

            var afterNames = await blobStorageService.ListBlobNamesAsync("news-analysis");
            var results = new List<object>();
            foreach (var name in afterNames.Where(n => !before.Contains(n)))
            {
                var content = await blobStorageService.ReadTextAsync("news-analysis", name);
                int? wordCount = TryGetWordCount(content);
                results.Add(new { filename = name, wordCount });
            }

            return Results.Json(new
            {
                success = true,
                articlesAnalyzed = results.Count,
                results,
                result
            });
        });

        app.MapGet("/api/news/analysis", async () =>
        {
            var names = await blobStorageService.ListBlobNamesAsync("news-analysis");
            var articles = new List<object>();
            foreach (var name in names)
            {
                var content = await blobStorageService.ReadTextAsync("news-analysis", name);
                string? title = null, date = null, source = null;
                int? wordCount = null;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("title", out var t)) title = t.GetString();
                    if (root.TryGetProperty("date", out var d)) date = d.GetString();
                    if (root.TryGetProperty("source", out var s)) source = s.GetString();
                    if (root.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc)) wordCount = wc;
                }
                articles.Add(new { filename = name, title, date, source, wordCount });
            }

            return Results.Json(new { status = "ok", articles });
        });

        app.MapGet("/api/news/analysis/content", async (string name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.Json(new { status = "error", error = "name is required." });

            var content = await blobStorageService.ReadTextAsync("news-analysis", name);
            if (content is null)
                return Results.Json(new { status = "error", error = "Analysis not found." });

            return Results.Json(new { status = "ok", content });
        });

        app.MapGet("/api/market/research", async () =>
        {
            var result = await marketResearchAgent.RunAsync(
                "Research the current copper market sentiment based on latest news and Bing search results");

            var (sentiment, confidence, keyDrivers, summary) = ParseSentiment(result);
            var timestamp = DateTime.UtcNow.ToString("o");

            var researchDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var researchFilename = $"{researchDate}_copper_research.json";
            var researchJson = System.Text.Json.JsonSerializer.Serialize(
                new { sentiment, confidence, keyDrivers, summary, timestamp });
            await blobStorageService.WriteTextAsync(
                "market-research", researchFilename, researchJson, "application/json");
            await fabricLakehouseService.WriteFileAsync(
                $"market-research/{researchFilename}", researchJson);

            return Results.Json(new
            {
                status = "ok",
                sentiment,
                confidence,
                keyDrivers,
                summary,
                timestamp
            });
        });

        app.MapGet("/api/insight/generate", async () =>
        {
            await insightGenerationAgent.RunAsync("Generate today's copper market insight report and store it in markdown.");
            var latest = await ReadLatestInsightAsync(blobStorageService);
            var preview = latest.Content.Length > InsightPreviewMaxLength
                ? latest.Content[..InsightPreviewMaxLength]
                : latest.Content;
            return Results.Json(new
            {
                success = true,
                date = latest.Date,
                filename = latest.Filename,
                preview
            });
        });

        app.MapGet("/api/insight/latest", async () =>
        {
            var latest = await ReadLatestInsightAsync(blobStorageService);
            return Results.Json(new { date = latest.Date, content = latest.Content, filename = latest.Filename });
        });

        app.MapGet("/api/insight/list", async () =>
        {
            var names = await blobStorageService.ListBlobNamesAsync("market-insight");
            var reports = names
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .Select(n => new { filename = n, date = ExtractInsightDate(n) })
                .ToArray();
            return Results.Json(new { reports });
        });

        app.MapGet("/api/insight/byDate", async (string date) =>
        {
            if (string.IsNullOrWhiteSpace(date))
                return Results.Json(new { date = string.Empty, content = string.Empty, filename = string.Empty });

            var filename = $"{date.Trim()}_copper_insight.md";
            var content = await blobStorageService.ReadTextAsync("market-insight", filename);
            return Results.Json(new
            {
                date = date.Trim(),
                content = content ?? string.Empty,
                filename = content is null ? string.Empty : filename
            });
        });

        app.MapGet("/api/mock/rss", async (HttpContext context, IWebHostEnvironment env) =>
        {
            var (jsonPath, articles) = await LoadMockArticlesAsync(env, MockRssArticlesFileName, MockRssFallbackFileName);
            if (string.IsNullOrWhiteSpace(jsonPath))
                return Results.NotFound($"{MockRssArticlesFileName} not found");

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

            if (articles.Count == 0)
                return Results.Content("<rss version=\"2.0\"><channel><title>Copper Market News</title></channel></rss>", "application/rss+xml");

            var itemsXml = string.Join("\n", articles.Select(a =>
                $"""
                <item>
                  <title><![CDATA[{a.Title}]]></title>
                  <link>{baseUrl}/api/mock/article/{a.Id}</link>
                  <pubDate>{a.PublishDate}</pubDate>
                  <description><![CDATA[{a.Description}]]></description>
                </item>
                """));

            var rss = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0">
                  <channel>
                    <title>Copper Market News (Mock)</title>
                    <link>{baseUrl}/api/mock/rss</link>
                    <description>Mock copper market news articles for development and testing</description>
                    <language>en-us</language>
                    {itemsXml}
                  </channel>
                </rss>
                """;

            return Results.Content(rss, "application/rss+xml");
        });

        app.MapGet("/api/mock/article/{id}", async (string id, IWebHostEnvironment env) =>
        {
            var (jsonPath, articles) = await LoadMockArticlesAsync(env, MockRssArticlesFileName, MockRssFallbackFileName);
            if (string.IsNullOrWhiteSpace(jsonPath))
                return Results.NotFound($"{MockRssArticlesFileName} not found");

            var article = articles?.FirstOrDefault(a => a.Id == id);
            if (article is null)
                return Results.NotFound($"Article '{id}' not found");

            return Results.Content(article.HtmlContent, "text/html");
        });

        app.MapGet("/api/subscription", async (HttpContext context) =>
        {
            var userId = ResolveUserId(context);
            var raw = await blobStorageService.ReadTextAsync("subscriptions", $"{userId}.json");
            var subscription = SubscriptionRequest.FromJson(raw);
            return Results.Json(new { markets = subscription.Markets, items = subscription.Items });
        });

        app.MapPost("/api/subscription", async (HttpContext context, SubscriptionRequest request) =>
        {
            var userId = ResolveUserId(context);
            await blobStorageService.WriteTextAsync("subscriptions", $"{userId}.json", request.ToJson());
            return Results.Json(new { success = true });
        });
    }

    private static string ResolveUserId(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var userId = string.IsNullOrWhiteSpace(ip) ? "default" : ip;
        return new string(userId.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    private static string? ExtractInsightDate(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;
        var token = Path.GetFileName(filename).Split('_', '.').FirstOrDefault();
        return DateTime.TryParseExact(token, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _) ? token : null;
    }

    private static async Task<(string Date, string Content, string Filename)> ReadLatestInsightAsync(BlobStorageService blobStorageService)
    {
        var names = await blobStorageService.ListBlobNamesAsync("market-insight");
        var latest = names.OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null)
            return (string.Empty, string.Empty, string.Empty);

        var content = await blobStorageService.ReadTextAsync("market-insight", latest) ?? string.Empty;
        return (ExtractInsightDate(latest) ?? string.Empty, content, latest);
    }

    private static int? TryGetWordCount(string? analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
            return null;
        using var doc = System.Text.Json.JsonDocument.Parse(analysisJson);
        if (doc.RootElement.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc))
            return wc;
        return null;
    }
    private static (string sentiment, double confidence, string[] keyDrivers, string summary) ParseSentiment(string? agentOutput)
    {
        var sentiment = "neutral";
        var confidence = 0.0;
        var keyDrivers = Array.Empty<string>();
        var summary = agentOutput ?? string.Empty;

        var json = ExtractJsonObject(agentOutput);
        if (json is null)
            return (sentiment, confidence, keyDrivers, summary);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("sentiment", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = s.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                sentiment = value.Trim().ToLowerInvariant();
        }

        if (root.TryGetProperty("confidence", out var c))
        {
            if (c.ValueKind == System.Text.Json.JsonValueKind.Number && c.TryGetDouble(out var cv))
                confidence = cv;
            else if (c.ValueKind == System.Text.Json.JsonValueKind.String
                && double.TryParse(c.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cs))
                confidence = cs;
        }

        if (root.TryGetProperty("keyDrivers", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            keyDrivers = k.EnumerateArray()
                .Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray();
        }

        if (root.TryGetProperty("summary", out var sum) && sum.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = sum.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                summary = value;
        }

        return (sentiment, confidence, keyDrivers, summary);
    }

    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return text.Substring(start, end - start + 1);
    }

    private static async Task<(string? Path, List<MockArticle> Articles)> LoadMockArticlesAsync(
        IWebHostEnvironment env,
        string primaryFileName,
        string? fallbackFileName = null)
    {
        string? resolvedPath = null;
        var dataRootPath = Path.Combine(env.ContentRootPath, DataFolderName);
        var primaryPath = Path.Combine(dataRootPath, primaryFileName);
        if (File.Exists(primaryPath))
            resolvedPath = primaryPath;
        else if (!string.IsNullOrWhiteSpace(fallbackFileName))
        {
            var fallbackPath = Path.Combine(dataRootPath, fallbackFileName);
            if (File.Exists(fallbackPath))
                resolvedPath = fallbackPath;
        }

        if (string.IsNullOrWhiteSpace(resolvedPath))
            return (null, []);

        var json = await File.ReadAllTextAsync(resolvedPath);
        try
        {
            var articles = System.Text.Json.JsonSerializer.Deserialize<List<MockArticle>>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (resolvedPath, articles ?? []);
        }
        catch (System.Text.Json.JsonException)
        {
            return (resolvedPath, []);
        }
    }

    private static string BuildKnowledgeFilename(MockArticle article)
    {
        var id = string.IsNullOrWhiteSpace(article.Id) ? Guid.NewGuid().ToString("N")[..8] : article.Id.Trim();
        var title = string.IsNullOrWhiteSpace(article.Title) ? "article" : article.Title;
        var slug = new string(title
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "article";
        return $"{id}-{slug}.html";
    }
}

public sealed record MockArticle(string Id, string Title, string PublishDate, string Description, string HtmlContent);

public sealed record SubscriptionRequest(IReadOnlyList<string> Markets, IReadOnlyList<string> Items)
{
    public string ToJson()
    {
        var safeMarkets = Markets ?? [];
        var safeItems = Items ?? [];
        return System.Text.Json.JsonSerializer.Serialize(new { markets = safeMarkets, items = safeItems });
    }

    public static SubscriptionRequest FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SubscriptionRequest([], []);

        var parsed = System.Text.Json.JsonSerializer.Deserialize<SubscriptionRequest>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return new SubscriptionRequest(parsed?.Markets ?? [], parsed?.Items ?? []);
    }
}
