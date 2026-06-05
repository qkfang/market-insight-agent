using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using mkti_app.Services;
using ModelContextProtocol.Server;

namespace mkti_app.Mcp;

[McpServerToolType]
public sealed class MarketInsightMcpTools
{
    private const string NewsStoreContainer = "news-store";
    private const string NewsAnalysisContainer = "news-analysis";
    private const string MarketInsightContainer = "market-insight";
    private const string MarketResearchContainer = "market-research";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string[] _defaultRssFeeds;
    private readonly BlobStorageService _blobStorageService;
    private readonly DocIntelligenceService _docIntelligenceService;
    private readonly FabricLakehouseService _fabricLakehouseService;
    private readonly BingSearchService _bingSearchService;
    private readonly IHttpClientFactory _httpClientFactory;

    public MarketInsightMcpTools(
        BlobStorageService blobStorageService,
        DocIntelligenceService docIntelligenceService,
        FabricLakehouseService fabricLakehouseService,
        BingSearchService bingSearchService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _blobStorageService = blobStorageService;
        _docIntelligenceService = docIntelligenceService;
        _fabricLakehouseService = fabricLakehouseService;
        _bingSearchService = bingSearchService;
        _httpClientFactory = httpClientFactory;
        var appMcpUrl = configuration["APP_MCP_URL"] ?? "http://localhost:5001";
        _defaultRssFeeds = [$"{appMcpUrl.TrimEnd('/')}/api/mock/rss"];
    }

    [McpServerTool(Name = "fetch_rss_feed"), Description("Download and parse a copper market RSS/Atom feed. Returns a JSON array of articles with title, url, publishDate and description.")]
    public async Task<string> FetchRssFeed(
        [Description("RSS feed URL. If omitted, the default copper market feeds are used.")] string? feedUrl = null)
    {
        var feeds = string.IsNullOrWhiteSpace(feedUrl) ? _defaultRssFeeds : [feedUrl];
        var client = CreateClient();
        var articles = new List<RssArticle>();

        foreach (var feed in feeds)
        {
            try
            {
                var xml = await client.GetStringAsync(feed);
                articles.AddRange(ParseFeed(xml));
            }
            catch (Exception ex)
            {
                articles.Add(new RssArticle($"Error fetching feed {feed}: {ex.Message}", feed, string.Empty, string.Empty));
            }
        }

        return JsonSerializer.Serialize(articles);
    }

    [McpServerTool(Name = "download_article"), Description("Download the full HTML content of an article from its URL.")]
    public async Task<string> DownloadArticle(
        [Description("Article URL to download")] string url,
        [Description("Article title for reference")] string title)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "Error: url is required.";

        try
        {
            var client = CreateClient();
            return await client.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            return $"Error downloading article '{title}' from {url}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "store_news_article"), Description("Store a news article to the Azure Blob 'news-store' container and the Fabric Lakehouse 'news-store' folder. Returns the storage path.")]
    public async Task<string> StoreNewsArticle(
        [Description("Short article description used as filename suffix, e.g. 'copper-prices-surge'. No extension needed.")] string description,
        [Description("Article content (html or text)")] string content,
        [Description("Content type, e.g. text/html or application/pdf")] string contentType)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Error: description is required.";

        var ext = (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("pdf")) ? ".pdf" : ".html";
        var safeDesc = string.Concat(description.ToLowerInvariant()
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(' ', '-');
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var filename = $"{timestamp}_{safeDesc}{ext}";

        var safeContent = content ?? string.Empty;
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "text/html" : contentType;

        await _blobStorageService.WriteTextAsync("news-store", filename, safeContent, resolvedContentType);

        var fabricPath = await _fabricLakehouseService.UploadFileAsync(
            "news-store", filename, System.Text.Encoding.UTF8.GetBytes(safeContent));

        return JsonSerializer.Serialize(new
        {
            blobPath = $"news-store/{filename}",
            fabricPath
        });
    }

    [McpServerTool(Name = "list_stored_news"), Description("List filenames already stored in the Azure Blob 'news-store' container so duplicates can be avoided.")]
    public async Task<string> ListStoredNews()
    {
        var names = await _blobStorageService.ListBlobNamesAsync("news-store");
        return JsonSerializer.Serialize(names);
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

    [McpServerTool(Name = "list_unprocessed_news"), Description("List raw news articles in the news-store container that have not yet been analyzed (no matching JSON in news-analysis). Returns JSON array of { filename, blobUrl }.")]
    public async Task<string> ListUnprocessedNews()
    {
        var sourceNames = await _blobStorageService.ListBlobNamesAsync(NewsStoreContainer);
        var analyzedNames = await _blobStorageService.ListBlobNamesAsync(NewsAnalysisContainer);
        var analyzedSet = new HashSet<string>(analyzedNames, StringComparer.OrdinalIgnoreCase);

        var unprocessed = sourceNames
            .Where(name => !analyzedSet.Contains($"{name}.json"))
            .Select(name => new
            {
                filename = name,
                blobUrl = _blobStorageService.GetBlobUrl(NewsStoreContainer, name)
            })
            .ToArray();

        return JsonSerializer.Serialize(unprocessed, JsonOptions);
    }

    [McpServerTool(Name = "parse_article_with_doc_intelligence"), Description("Retrieve a raw news article from the news-store container (blob or local fallback) and use Azure Document Intelligence (prebuilt-read) to extract its content as markdown. Returns JSON { title, date, source, markdownContent, wordCount }.")]
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
            return $"Error: failed to analyze '{filename}' with Document Intelligence: {ex.Message}";
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

    [McpServerTool(Name = "store_news_analysis"), Description("Store the structured news analysis JSON to the news-analysis blob container and the Fabric news-analysis folder. The blob name is {yyyyMMddHHmmss}_{description}.json.")]
    public async Task<string> StoreNewsAnalysis(
        [Description("Short article description used as filename suffix, e.g. 'copper-prices-surge'. No extension needed.")] string description,
        [Description("Analysis content as a JSON string with title, date, source and markdownContent fields")] string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Error: description is required.";
        if (string.IsNullOrWhiteSpace(analysisJson))
            return "Error: analysisJson is required.";

        var safeDesc = string.Concat(description.ToLowerInvariant()
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(' ', '-');
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var blobName = $"{timestamp}_{safeDesc}.json";
        await _blobStorageService.WriteTextAsync(NewsAnalysisContainer, blobName, analysisJson);
        var fabricStored = await _fabricLakehouseService.WriteFileAsync($"news-analysis/{blobName}", analysisJson);

        return $"Stored analysis as {blobName} (blob: yes, fabric: {(fabricStored ? "yes" : "skipped")}).";
    }

    [McpServerTool(Name = "list_news_analysis"), Description("List analyzed articles from the news-analysis container with metadata. Returns JSON array of { filename, title, date, source, wordCount }.")]
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

    [McpServerTool(Name = "read_latest_news_analysis"), Description("Read the N most recent news analysis JSON documents from the news-analysis blob container. Returns a JSON array of { filename, title, date, markdownContent }.")]
    public async Task<string> ReadLatestNewsAnalysis(
        [Description("Number of most recent analysis documents to read. Defaults to 10.")] int count = 10)
    {
        var names = await _blobStorageService.ListRecentBlobNamesAsync(NewsAnalysisContainer, count);
        var items = new List<object>();

        foreach (var name in names)
        {
            var content = await _blobStorageService.ReadTextAsync(NewsAnalysisContainer, name);
            string? title = null, date = null, markdownContent = null;

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("title", out var t)) title = t.GetString();
                    if (root.TryGetProperty("date", out var d)) date = d.GetString();
                    if (root.TryGetProperty("markdownContent", out var m)) markdownContent = m.GetString();
                }
                catch (JsonException)
                {
                    markdownContent = content;
                }
            }

            items.Add(new { filename = name, title, date, markdownContent });
        }

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    [McpServerTool(Name = "bing_search_copper_market"), Description("Search Bing for current copper price and market news. Returns a JSON array of up to 5 results with { title, snippet, url }.")]
    public async Task<string> BingSearchCopperMarket(
        [Description("Search query, e.g. 'copper price today LME market sentiment'.")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            query = "copper price today LME market sentiment";

        if (!_bingSearchService.IsConfigured)
            return "Error: Bing Search is not configured (set BING_SEARCH_API_KEY and BING_SEARCH_ENDPOINT).";

        var results = await _bingSearchService.SearchAsync(query, 5);
        var items = results
            .Select(r => new { title = r.Title, snippet = r.Snippet, url = r.Url })
            .ToArray();

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    [McpServerTool(Name = "get_copper_sentiment_summary"), Description("Format a JSON array of article summaries into a single prompt-friendly text block for sentiment analysis. Returns the formatted text.")]
    public string GetCopperSentimentSummary(
        [Description("JSON array of article summaries.")] string articles)
    {
        if (string.IsNullOrWhiteSpace(articles))
            return "No articles provided.";

        var builder = new System.Text.StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(articles);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var index = 1;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string? title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    string? date = item.TryGetProperty("date", out var d) ? d.GetString() : null;
                    string? body = null;
                    if (item.TryGetProperty("markdownContent", out var m)) body = m.GetString();
                    else if (item.TryGetProperty("snippet", out var s)) body = s.GetString();

                    builder.AppendLine($"## Article {index}: {title ?? "(untitled)"}");
                    if (!string.IsNullOrWhiteSpace(date)) builder.AppendLine($"Date: {date}");
                    if (!string.IsNullOrWhiteSpace(body)) builder.AppendLine(body);
                    builder.AppendLine();
                    index++;
                }
            }
            else
            {
                builder.Append(articles);
            }
        }
        catch (JsonException)
        {
            builder.Append(articles);
        }

        return builder.ToString().Trim();
    }

    [McpServerTool(Name = "get_latest_research"), Description("Read the most recent copper market research result produced by the Market Research Agent from the market-research blob container. Returns JSON with sentiment, confidence, keyDrivers, summary and timestamp when available, otherwise the raw research markdown as summary.")]
    public async Task<string> GetLatestResearch()
    {
        var names = await _blobStorageService.ListBlobNamesAsync(MarketResearchContainer);
        var latest = names.OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null)
        {
            return JsonSerializer.Serialize(new
            {
                sentiment = (string?)null,
                confidence = (double?)null,
                keyDrivers = Array.Empty<string>(),
                summary = string.Empty,
                timestamp = (string?)null
            }, JsonOptions);
        }

        var content = await _blobStorageService.ReadTextAsync(MarketResearchContainer, latest) ?? string.Empty;

        // If the stored research is already structured JSON, return it untouched.
        if (LooksLikeJsonObject(content))
            return content;

        return JsonSerializer.Serialize(new
        {
            sentiment = (string?)null,
            confidence = (double?)null,
            keyDrivers = Array.Empty<string>(),
            summary = content,
            timestamp = ExtractDateFromName(latest)
        }, JsonOptions);
    }

    [McpServerTool(Name = "store_market_insight"), Description("Store the daily copper market insight markdown to the market-insight blob container as {date}_copper_insight.md and to the Fabric Lakehouse market-insight/ folder. Returns JSON with the blob URL, filename and date.")]
    public async Task<string> StoreMarketInsight(
        [Description("Insight report content as markdown")] string content,
        [Description("Report date in yyyy-MM-dd format. Defaults to today's UTC date when omitted.")] string? date = null)
    {
        var resolvedDate = string.IsNullOrWhiteSpace(date)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd")
            : date.Trim();
        var filename = $"{resolvedDate}_copper_insight.md";
        var safeContent = content ?? string.Empty;

        await _blobStorageService.WriteTextAsync(MarketInsightContainer, filename, safeContent, "text/markdown");
        await _fabricLakehouseService.WriteFileAsync($"market-insight/{filename}", safeContent);

        return JsonSerializer.Serialize(new
        {
            blobUrl = _blobStorageService.GetBlobUrl(MarketInsightContainer, filename),
            filename,
            date = resolvedDate
        }, JsonOptions);
    }

    [McpServerTool(Name = "get_latest_insight"), Description("Read the most recent insight markdown from the market-insight blob container. Returns JSON with date, content and filename.")]
    public async Task<string> GetLatestInsight()
    {
        var names = await _blobStorageService.ListBlobNamesAsync(MarketInsightContainer);
        var latest = names.OrderByDescending(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null)
            return JsonSerializer.Serialize(new { date = string.Empty, content = string.Empty, filename = string.Empty }, JsonOptions);

        var content = await _blobStorageService.ReadTextAsync(MarketInsightContainer, latest) ?? string.Empty;
        return JsonSerializer.Serialize(new
        {
            date = ExtractDateFromName(latest) ?? string.Empty,
            content,
            filename = latest
        }, JsonOptions);
    }

    [McpServerTool(Name = "get_insight_by_date"), Description("Read the copper market insight for a specific date from the market-insight blob container. Returns JSON with date, content and filename.")]
    public async Task<string> GetInsightByDate(
        [Description("Report date in yyyy-MM-dd format")] string date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return "Error: date is required.";

        var filename = $"{date.Trim()}_copper_insight.md";
        var content = await _blobStorageService.ReadTextAsync(MarketInsightContainer, filename);

        if (content is null)
        {
            // Fall back to any blob that starts with the requested date.
            var names = await _blobStorageService.ListBlobNamesAsync(MarketInsightContainer);
            var match = names.FirstOrDefault(n => n.StartsWith(date.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                filename = match;
                content = await _blobStorageService.ReadTextAsync(MarketInsightContainer, match);
            }
        }

        return JsonSerializer.Serialize(new
        {
            date = date.Trim(),
            content = content ?? string.Empty,
            filename = content is null ? string.Empty : filename
        }, JsonOptions);
    }

    private static bool LooksLikeJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string? ExtractDateFromName(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;
        var name = Path.GetFileName(filename);
        var token = name.Split('_', '.').FirstOrDefault();
        return DateTime.TryParseExact(token, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _) ? token : null;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("mkti-news-ingestion", "1.0"));
        }
        return client;
    }

    private static List<RssArticle> ParseFeed(string xml)
    {
        var articles = new List<RssArticle>();
        if (string.IsNullOrWhiteSpace(xml))
            return articles;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            return articles;
        }

        // RSS 2.0 items.
        foreach (var item in doc.Descendants("item"))
        {
            articles.Add(new RssArticle(
                ((string?)item.Element("title"))?.Trim() ?? string.Empty,
                ((string?)item.Element("link"))?.Trim() ?? string.Empty,
                ((string?)item.Element("pubDate"))?.Trim() ?? string.Empty,
                ((string?)item.Element("description"))?.Trim() ?? string.Empty));
        }

        // Atom entries.
        XNamespace atom = "http://www.w3.org/2005/Atom";
        foreach (var entry in doc.Descendants(atom + "entry"))
        {
            var link = entry.Elements(atom + "link")
                .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate");
            articles.Add(new RssArticle(
                ((string?)entry.Element(atom + "title"))?.Trim() ?? string.Empty,
                link?.Attribute("href")?.Value?.Trim() ?? string.Empty,
                ((string?)entry.Element(atom + "updated"))?.Trim()
                    ?? ((string?)entry.Element(atom + "published"))?.Trim() ?? string.Empty,
                ((string?)entry.Element(atom + "summary"))?.Trim() ?? string.Empty));
        }

        return articles;
    }

    private sealed record RssArticle(string Title, string Url, string PublishDate, string Description);
}
