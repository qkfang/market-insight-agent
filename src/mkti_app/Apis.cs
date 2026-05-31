using mkti_app.Agents;
using mkti_app.Services;

namespace mkti_app;

public static class Apis
{
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
            try
            {
                var before = await blobStorageService.ListBlobNamesAsync("news-store");
                var result = await newsIngestionAgent.RunAsync("Download latest copper market news from RSS feeds and store them");
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
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, articlesStored = 0, message = ex.Message });
            }
        });

        app.MapGet("/api/news/list", async () =>
        {
            var filenames = await blobStorageService.ListBlobNamesAsync("news-store");
            return Results.Json(new { success = true, filenames });
        });

        app.MapGet("/api/news/analyze", async () =>
        {
            try
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
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, status = "error", error = ex.Message });
            }
        });

        app.MapGet("/api/news/analysis", async () =>
        {
            try
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
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(content);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("title", out var t)) title = t.GetString();
                            if (root.TryGetProperty("date", out var d)) date = d.GetString();
                            if (root.TryGetProperty("source", out var s)) source = s.GetString();
                            if (root.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc)) wordCount = wc;
                        }
                        catch (System.Text.Json.JsonException) { }
                    }
                    articles.Add(new { filename = name, title, date, source, wordCount });
                }

                return Results.Json(new { status = "ok", articles });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message });
            }
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
            try
            {
                var result = await marketResearchAgent.RunAsync(
                    "Research the current copper market sentiment based on latest news and Bing search results");

                var (sentiment, confidence, keyDrivers, summary) = ParseSentiment(result);

                return Results.Json(new
                {
                    status = "ok",
                    sentiment,
                    confidence,
                    keyDrivers,
                    summary,
                    timestamp = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message });
            }
        });

        app.MapGet("/api/insight/generate", async () =>
        {
            try
            {
                var result = await insightGenerationAgent.RunAsync("Generate today's copper market insight report and store it in markdown.");
                return Results.Json(new { status = "ok", result });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message });
            }
        });

        app.MapGet("/api/insight/latest", async () =>
        {
            var latest = await blobStorageService.ReadLatestTextAsync("market-insight");
            return Results.Json(new { status = "ok", insight = latest ?? string.Empty });
        });

        app.MapGet("/api/subscription", async () =>
        {
            var subscription = await blobStorageService.ReadTextAsync("preferences", "subscription.json")
                ?? "{\"markets\":[\"Copper\"],\"items\":[\"DailyInsight\"]}";
            return Results.Json(new { status = "ok", subscription });
        });

        app.MapPost("/api/subscription", async (SubscriptionRequest request) =>
        {
            await blobStorageService.WriteTextAsync("preferences", "subscription.json", request.ToJson());
            return Results.Json(new { status = "saved" });
        });
    }

    private static int? TryGetWordCount(string? analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(analysisJson);
            if (doc.RootElement.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc))
                return wc;
        }
        catch (System.Text.Json.JsonException) { }
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

        try
        {
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
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall back to defaults / raw output if the agent did not return valid JSON.
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
}

public sealed record SubscriptionRequest(IReadOnlyList<string> Markets, IReadOnlyList<string> Items)
{
    public string ToJson()
    {
        var safeMarkets = Markets ?? [];
        var safeItems = Items ?? [];
        return System.Text.Json.JsonSerializer.Serialize(new { markets = safeMarkets, items = safeItems });
    }
}
