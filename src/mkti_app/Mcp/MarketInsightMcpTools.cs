using System.ComponentModel;
using mkti_app.Services;
using ModelContextProtocol.Server;

namespace mkti_app.Mcp;

[McpServerToolType]
public sealed class MarketInsightMcpTools
{
    private readonly BlobStorageService _blobStorageService;

    public MarketInsightMcpTools(BlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    [McpServerTool(Name = "store_news"), Description("Write ingested news html or pdf content to blob container news-store.")]
    public async Task<string> StoreNews(
        [Description("Target blob name, e.g. source/news-2026-01-01.html")] string blobName,
        [Description("Raw html or extracted text content")] string content)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            return "Error: blobName is required.";

        await _blobStorageService.WriteTextAsync("news-store", blobName, content ?? string.Empty);
        return $"Stored news as {blobName}";
    }

    [McpServerTool(Name = "read_news_analysis"), Description("Read parsed article analysis from blob container news-analysis.")]
    public async Task<string> ReadNewsAnalysis(
        [Description("Optional blob name. If omitted, reads latest item.")] string? blobName = null)
    {
        var value = string.IsNullOrWhiteSpace(blobName)
            ? await _blobStorageService.ReadLatestTextAsync("news-analysis")
            : await _blobStorageService.ReadTextAsync("news-analysis", blobName);

        return value ?? string.Empty;
    }

    [McpServerTool(Name = "store_insight"), Description("Write daily market insight markdown into blob container market-insight.")]
    public async Task<string> StoreInsight(
        [Description("Markdown content for the insight document")] string markdown,
        [Description("Optional blob name. Defaults to UTC-date markdown name.")] string? blobName = null)
    {
        var finalName = string.IsNullOrWhiteSpace(blobName)
            ? $"insight-{DateTime.UtcNow:yyyy-MM-dd}.md"
            : blobName;

        await _blobStorageService.WriteTextAsync("market-insight", finalName, markdown ?? string.Empty);
        return $"Stored insight as {finalName}";
    }

    [McpServerTool(Name = "read_latest_insight"), Description("Read the latest stored insight markdown from blob container market-insight.")]
    public async Task<string> ReadLatestInsight()
    {
        return await _blobStorageService.ReadLatestTextAsync("market-insight") ?? string.Empty;
    }
}
