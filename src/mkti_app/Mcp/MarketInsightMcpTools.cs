using System.ComponentModel;
using System.Text.Json;
using mkti_app.Services;
using ModelContextProtocol.Server;

namespace mkti_app.Mcp;

[McpServerToolType]
public sealed class MarketInsightMcpTools
{
    private const string NewsStoreContainer = "news-store";
    private const string NewsAnalysisContainer = "news-analysis";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly BlobStorageService _blobStorageService;
    private readonly DocIntelligenceService _docIntelligenceService;
    private readonly FabricLakehouseService _fabricLakehouseService;

    public MarketInsightMcpTools(
        BlobStorageService blobStorageService,
        DocIntelligenceService docIntelligenceService,
        FabricLakehouseService fabricLakehouseService)
    {
        _blobStorageService = blobStorageService;
        _docIntelligenceService = docIntelligenceService;
        _fabricLakehouseService = fabricLakehouseService;
    }

    [McpServerTool(Name = "store_news"), Description("Write ingested news html or pdf content to blob container news-store.")]
    public async Task<string> StoreNews(
        [Description("Target blob name, e.g. source/news-2026-01-01.html")] string blobName,
        [Description("Raw html or extracted text content")] string content)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            return "Error: blobName is required.";

        await _blobStorageService.WriteTextAsync(NewsStoreContainer, blobName, content ?? string.Empty);
        return $"Stored news as {blobName}";
    }

    [McpServerTool(Name = "list_unprocessed_news"), Description("List raw news articles in the news-store container that have not yet been analysed (no matching JSON in news-analysis). Returns JSON array of { filename, blobUrl }.")]
    public async Task<string> ListUnprocessedNews()
    {
        var sourceNames = await _blobStorageService.ListBlobNamesAsync(NewsStoreContainer);
        var analysedNames = await _blobStorageService.ListBlobNamesAsync(NewsAnalysisContainer);
        var analysedSet = new HashSet<string>(analysedNames, StringComparer.OrdinalIgnoreCase);

        var unprocessed = sourceNames
            .Where(name => !analysedSet.Contains($"{name}.json"))
            .Select(name => new
            {
                filename = name,
                blobUrl = _blobStorageService.GetBlobUrl(NewsStoreContainer, name)
            })
            .ToArray();

        return JsonSerializer.Serialize(unprocessed, JsonOptions);
    }

    [McpServerTool(Name = "parse_article_with_doc_intelligence"), Description("Download a raw news article from news-store and use Azure Document Intelligence (prebuilt-read) to extract its content as markdown. Returns JSON { title, date, source, markdownContent, wordCount }.")]
    public async Task<string> ParseArticleWithDocIntelligence(
        [Description("Blob name (filename) of the article in the news-store container")] string filename,
        [Description("Optional blob URL of the article (informational).")] string? blobUrl = null)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "Error: filename is required.";

        if (!_docIntelligenceService.IsConfigured)
            return "Error: Document Intelligence is not configured (set AZURE_DOC_INTELLIGENCE_ENDPOINT).";

        var bytes = await _blobStorageService.ReadBytesAsync(NewsStoreContainer, filename);
        if (bytes is null)
            return $"Error: article '{filename}' not found in {NewsStoreContainer}.";

        string markdown;
        try
        {
            var result = await _docIntelligenceService.AnalyzeFromBytesAsync(BinaryData.FromBytes(bytes));
            markdown = result.Markdown;
        }
        catch (Exception ex)
        {
            return $"Error: failed to analyse '{filename}' with Document Intelligence: {ex.Message}";
        }

        var wordCount = markdown
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;

        var parsed = new
        {
            title = Path.GetFileNameWithoutExtension(filename),
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            source = filename,
            markdownContent = markdown,
            wordCount
        };

        return JsonSerializer.Serialize(parsed, JsonOptions);
    }

    [McpServerTool(Name = "store_news_analysis"), Description("Store the structured news analysis JSON to the news-analysis blob container and the Fabric news_analysis folder. The blob name is {filename}.json.")]
    public async Task<string> StoreNewsAnalysis(
        [Description("Original article filename, e.g. source/news-2026-01-01.html")] string filename,
        [Description("Analysis content as a JSON string with title, date, source and markdownContent fields")] string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "Error: filename is required.";
        if (string.IsNullOrWhiteSpace(analysisJson))
            return "Error: analysisJson is required.";

        var blobName = $"{filename}.json";
        await _blobStorageService.WriteTextAsync(NewsAnalysisContainer, blobName, analysisJson);
        var fabricStored = await _fabricLakehouseService.WriteFileAsync($"news_analysis/{blobName}", analysisJson);

        return $"Stored analysis as {blobName} (blob: yes, fabric: {(fabricStored ? "yes" : "skipped")}).";
    }

    [McpServerTool(Name = "list_news_analysis"), Description("List analysed articles from the news-analysis container with metadata. Returns JSON array of { filename, title, date, source, wordCount }.")]
    public async Task<string> ListNewsAnalysis()
    {
        var names = await _blobStorageService.ListBlobNamesAsync(NewsAnalysisContainer);
        var items = new List<object>();

        foreach (var name in names)
        {
            var content = await _blobStorageService.ReadTextAsync(NewsAnalysisContainer, name);
            string? title = null, date = null, source = null;
            int? wordCount = null;

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("title", out var t)) title = t.GetString();
                    if (root.TryGetProperty("date", out var d)) date = d.GetString();
                    if (root.TryGetProperty("source", out var s)) source = s.GetString();
                    if (root.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc)) wordCount = wc;
                }
                catch (JsonException)
                {
                    // Ignore malformed analysis files when building the summary list.
                }
            }

            items.Add(new { filename = name, title, date, source, wordCount });
        }

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    [McpServerTool(Name = "read_news_analysis"), Description("Read parsed article analysis from blob container news-analysis.")]
    public async Task<string> ReadNewsAnalysis(
        [Description("Optional blob name. If omitted, reads latest item.")] string? blobName = null)
    {
        var value = string.IsNullOrWhiteSpace(blobName)
            ? await _blobStorageService.ReadLatestTextAsync(NewsAnalysisContainer)
            : await _blobStorageService.ReadTextAsync(NewsAnalysisContainer, blobName);

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
