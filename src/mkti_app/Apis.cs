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
                var articlesStored = delta >= 0 ? delta : after.Count;

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
                var result = await newsAnalysisAgent.RunAsync("Analyze the latest ingested news and provide structured article summaries as JSON.");
                var parsed = await blobStorageService.ReadLatestTextAsync("news-analysis");
                return Results.Json(new { status = "ok", result, parsedArticles = parsed });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", error = ex.Message });
            }
        });

        app.MapGet("/api/market/research", async () =>
        {
            try
            {
                var result = await marketResearchAgent.RunAsync("Research the current copper market and summarize sentiment, drivers, and risks.");
                var lakehouse = await fabricLakehouseService.QueryStatusAsync();
                return Results.Json(new { status = "ok", result, lakehouse });
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
