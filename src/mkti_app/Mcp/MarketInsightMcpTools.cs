using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using mkti_app.Services;
using ModelContextProtocol.Server;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace mkti_app.Mcp;

[McpServerToolType]
public sealed class MarketInsightMcpTools
{
    private const string DataFolderName = "data";
    private const string ArticlesFolderName = "articles";
    private const string ArticlesFilePattern = "articles-*.json";
    private const string NewsStoreContainer = "news-store";
    private const string NewsAnalysisContainer = "news-analysis";
    private const string MarketInsightContainer = "market-insight";
    private const string MarketResearchContainer = "market-research";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string[] _defaultRssFeeds;
    private readonly IWebHostEnvironment _environment;
    private readonly BlobStorageService _blobStorageService;
    private readonly DocIntelligenceService _docIntelligenceService;
    private readonly FabricLakehouseService _fabricLakehouseService;
    private readonly BingSearchService _bingSearchService;
    private readonly IHttpClientFactory _httpClientFactory;

    public MarketInsightMcpTools(
        IWebHostEnvironment environment,
        BlobStorageService blobStorageService,
        DocIntelligenceService docIntelligenceService,
        FabricLakehouseService fabricLakehouseService,
        BingSearchService bingSearchService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _environment = environment;
        _blobStorageService = blobStorageService;
        _docIntelligenceService = docIntelligenceService;
        _fabricLakehouseService = fabricLakehouseService;
        _bingSearchService = bingSearchService;
        _httpClientFactory = httpClientFactory;
        var appMcpUrl = configuration["APP_MCP_URL"] ?? "http://localhost:5001";
        _defaultRssFeeds = [$"{appMcpUrl.TrimEnd('/')}/api/mock/rss"];
    }

    [McpServerTool(Name = "ingest_articles_json_to_news_store"), Description("Load individual article JSON files from the data/articles/ folder and store each one to the news-store container and Fabric Lakehouse news-store folder. Files are named yyyy-MM-dd_<guid>.json and can be filtered by date range. Blob name format is {yyyyMMddHHmmssfff}_{guid}.json.")]
    public async Task<string> IngestArticlesJsonToNewsStore(
        [Description("Optional inclusive start date filter in yyyy-MM-dd format, e.g. 2026-06-01. Only articles whose filename date is on or after this date are ingested.")] string? dateFrom = null,
        [Description("Optional inclusive end date filter in yyyy-MM-dd format, e.g. 2026-06-07. Only articles whose filename date is on or before this date are ingested.")] string? dateTo = null)
    {
        var articlesDir = Path.Combine(_environment.ContentRootPath, DataFolderName, ArticlesFolderName);
        if (!Directory.Exists(articlesDir))
            return $"Error: articles directory not found at '{articlesDir}'.";

        DateOnly? fromDate = null, toDate = null;
        if (!string.IsNullOrWhiteSpace(dateFrom) && DateOnly.TryParse(dateFrom, out var fd)) fromDate = fd;
        if (!string.IsNullOrWhiteSpace(dateTo) && DateOnly.TryParse(dateTo, out var td)) toDate = td;

        var files = Directory.GetFiles(articlesDir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var prefix = Path.GetFileNameWithoutExtension(f);
                if (prefix.Length < 10) return false;
                if (!DateOnly.TryParse(prefix[..10], out var fileDate)) return false;
                if (fromDate.HasValue && fileDate < fromDate.Value) return false;
                if (toDate.HasValue && fileDate > toDate.Value) return false;
                return true;
            })
            .OrderBy(f => f)
            .ToArray();

        if (files.Length == 0)
            return JsonSerializer.Serialize(new { storedCount = 0, skippedCount = 0, filenames = Array.Empty<string>(), skipped = Array.Empty<object>() }, JsonOptions);

        var stored = new List<string>();
        var skipped = new List<object>();

        foreach (var filePath in files)
        {
            var fileLabel = Path.GetFileName(filePath);
            string payload;
            try { payload = await File.ReadAllTextAsync(filePath); }
            catch (Exception ex) { skipped.Add(new { file = fileLabel, reason = $"read error: {ex.Message}" }); continue; }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(payload); }
            catch (JsonException ex) { skipped.Add(new { file = fileLabel, reason = $"invalid JSON: {ex.Message}" }); continue; }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    skipped.Add(new { file = fileLabel, reason = "not a JSON object" });
                    continue;
                }

                var article = doc.RootElement;

                if (!TryGetGuidFromArticle(article, out var articleGuid))
                {
                    var id = TryGetString(article, "id") ?? "(unknown)";
                    skipped.Add(new { file = fileLabel, id, reason = "missing or invalid guid" });
                    continue;
                }

                var articleDateTime = GetArticleDateTime(article);
                var blobName = $"{articleDateTime:yyyyMMddHHmmssfff}_{articleGuid:D}.json";

                if (await _blobStorageService.ExistsAsync(NewsStoreContainer, blobName))
                {
                    skipped.Add(new { file = fileLabel, guid = articleGuid.ToString("D"), reason = "already exists", blobName });
                    continue;
                }

                await _blobStorageService.WriteTextAsync(NewsStoreContainer, blobName, payload, "application/json");
                await _fabricLakehouseService.WriteFileAsync($"news-store/{blobName}", payload);
                stored.Add(blobName);
            }
        }

        return JsonSerializer.Serialize(new
        {
            sourceFolder = ArticlesFolderName,
            dateFrom = dateFrom ?? "(none)",
            dateTo = dateTo ?? "(none)",
            storedCount = stored.Count,
            skippedCount = skipped.Count,
            filenames = stored,
            skipped
        }, JsonOptions);
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

    [McpServerTool(Name = "list_unprocessed_news"), Description("List raw news articles in the news-store container that have not yet been analyzed (no matching entry in news-analysis). Returns JSON array of { filename, blobUrl }.")]
    public async Task<string> ListUnprocessedNews()
    {
        var sourceNames = await _blobStorageService.ListBlobNamesAsync(NewsStoreContainer);
        var analyzedNames = await _blobStorageService.ListBlobNamesAsync(NewsAnalysisContainer);
        var analyzedSet = new HashSet<string>(analyzedNames, StringComparer.OrdinalIgnoreCase);

        var unprocessed = sourceNames
            .Where(name => !analyzedSet.Contains(name) && !analyzedSet.Contains($"{name}.json"))
            .Select(name => new
            {
                filename = name,
                blobUrl = _blobStorageService.GetBlobUrl(NewsStoreContainer, name)
            })
            .ToArray();

        return JsonSerializer.Serialize(unprocessed, JsonOptions);
    }

    [McpServerTool(Name = "analyze_news_json_to_analysis"), Description("Read unprocessed news JSON articles from the news-store container, extract structured fields, and store analysis JSON in news-analysis. Blob name matches the source: {yyyyMMddHHmmssfff}_{guid}.json. Returns JSON with processedCount, skippedCount, filenames and skipped.")]
    public async Task<string> AnalyzeNewsJsonToAnalysis()
    {
        var sourceNames = await _blobStorageService.ListBlobNamesAsync(NewsStoreContainer);
        var analyzedNames = await _blobStorageService.ListBlobNamesAsync(NewsAnalysisContainer);
        var analyzedSet = new HashSet<string>(analyzedNames, StringComparer.OrdinalIgnoreCase);

        var unprocessed = sourceNames
            .Where(name => !analyzedSet.Contains(name) && !analyzedSet.Contains($"{name}.json"))
            .ToList();

        var stored = new List<string>();
        var skipped = new List<object>();

        foreach (var blobName in unprocessed)
        {
            var content = await _blobStorageService.ReadTextAsync(NewsStoreContainer, blobName);
            if (string.IsNullOrWhiteSpace(content))
            {
                skipped.Add(new { blobName, reason = "empty content" });
                continue;
            }

            try
            {
                using var articleDoc = JsonDocument.Parse(content);
                _ = articleDoc.RootElement; // validate JSON
            }
            catch (JsonException ex)
            {
                skipped.Add(new { blobName, reason = $"invalid JSON: {ex.Message}" });
                continue;
            }

            var analysisJson = content;

            await _blobStorageService.WriteTextAsync(NewsAnalysisContainer, blobName, analysisJson, "application/json");
            await _fabricLakehouseService.WriteFileAsync($"news-analysis/{blobName}", analysisJson);
            stored.Add(blobName);
        }

        return JsonSerializer.Serialize(new
        {
            processedCount = stored.Count,
            skippedCount = skipped.Count,
            filenames = stored,
            skipped
        }, JsonOptions);
    }

    [McpServerTool(Name = "extract_html_to_text_content"), Description("For each news article JSON in the news-store container, convert the htmlContent field to markdown and populate the textContent field in-place. Skips articles whose textContent is already populated. Returns JSON with updatedCount, skippedCount, filenames and skipped.")]
    public async Task<string> ExtractHtmlToTextContent()
    {
        var names = await _blobStorageService.ListBlobNamesAsync(NewsStoreContainer);
        var updated = new List<string>();
        var skipped = new List<object>();

        foreach (var blobName in names)
        {
            var content = await _blobStorageService.ReadTextAsync(NewsStoreContainer, blobName);
            if (string.IsNullOrWhiteSpace(content))
            {
                skipped.Add(new { blobName, reason = "empty content" });
                continue;
            }

            JsonElement article;
            try
            {
                using var articleDoc = JsonDocument.Parse(content);
                article = articleDoc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                skipped.Add(new { blobName, reason = $"invalid JSON: {ex.Message}" });
                continue;
            }

            var htmlContent = TryGetString(article, "htmlContent") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                skipped.Add(new { blobName, reason = "no htmlContent" });
                continue;
            }

            var existingText = TryGetString(article, "textContent");
            if (!string.IsNullOrWhiteSpace(existingText)
                && !existingText.StartsWith("{extracted", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new { blobName, reason = "textContent already populated" });
                continue;
            }

            var markdownText = HtmlToMarkdown(htmlContent);

            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            var textContentWritten = false;
            foreach (var prop in article.EnumerateObject())
            {
                if (prop.NameEquals("textContent"))
                {
                    writer.WriteString("textContent", markdownText);
                    textContentWritten = true;
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            if (!textContentWritten)
                writer.WriteString("textContent", markdownText);
            writer.WriteEndObject();
            writer.Flush();

            var updatedJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            await _blobStorageService.WriteTextAsync(NewsStoreContainer, blobName, updatedJson, "application/json");
            await _fabricLakehouseService.WriteFileAsync($"news-store/{blobName}", updatedJson);
            updated.Add(blobName);
        }

        return JsonSerializer.Serialize(new
        {
            updatedCount = updated.Count,
            skippedCount = skipped.Count,
            filenames = updated,
            skipped
        }, JsonOptions);
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
                    else if (root.TryGetProperty("publishDateIso", out var di)) date = di.GetString();
                    else if (root.TryGetProperty("publishDate", out var dp)) date = dp.GetString();
                    if (root.TryGetProperty("source", out var s)) source = s.GetString();
                    else if (root.TryGetProperty("domain", out var dom)) source = dom.GetString();
                    if (root.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc)) wordCount = wc;
                    else if (root.TryGetProperty("textContent", out var tc) && tc.GetString() is { } tcStr)
                        wordCount = tcStr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
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
                    else if (root.TryGetProperty("publishDateIso", out var di)) date = di.GetString();
                    else if (root.TryGetProperty("publishDate", out var dp)) date = dp.GetString();
                    if (root.TryGetProperty("markdownContent", out var m)) markdownContent = m.GetString();
                    else if (root.TryGetProperty("textContent", out var tc)) markdownContent = tc.GetString();
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

    [McpServerTool(Name = "bing_search_market"), Description("Search Bing for current price and market news for a given commodity market within a date range. Returns a JSON array of up to 5 results with { title, snippet, url }.")]
    public async Task<string> BingSearchMarket(
        [Description("Market name, e.g. 'copper', 'gold', 'silver', 'oil'.")] string market,
        [Description("Inclusive start date of the week in yyyy-MM-dd format, e.g. '2026-06-02'.")] string? weekStart = null,
        [Description("Inclusive end date of the week in yyyy-MM-dd format, e.g. '2026-06-08'.")] string? weekEnd = null)
    {
        if (string.IsNullOrWhiteSpace(market))
            return "Error: market is required.";

        if (!_bingSearchService.IsConfigured)
            return "Error: Bing Search is not configured (set BING_SEARCH_API_KEY and BING_SEARCH_ENDPOINT).";

        var weekRange = (!string.IsNullOrWhiteSpace(weekStart) && !string.IsNullOrWhiteSpace(weekEnd))
            ? $"{weekStart} to {weekEnd}"
            : null;

        var query = string.IsNullOrWhiteSpace(weekRange)
            ? $"{market} price today market sentiment this week"
            : $"{market} price market news {weekRange}";

        var results = await _bingSearchService.SearchAsync(query, 5);
        var items = results
            .Select(r => new { title = r.Title, snippet = r.Snippet, url = r.Url })
            .ToArray();

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    [McpServerTool(Name = "read_news_analysis_by_market"), Description("Read the N most recent news-analysis documents that mention the specified market keyword, optionally filtered to a date range. Returns a JSON array of { filename, title, date, source, markdownContent }.")]
    public async Task<string> ReadNewsAnalysisByMarket(
        [Description("Market keyword to filter by, e.g. 'copper', 'gold', 'silver', 'oil'.")] string market,
        [Description("Optional inclusive start date filter in yyyy-MM-dd format, e.g. '2026-06-02'. Only articles on or after this date are returned.")] string? weekStart = null,
        [Description("Optional inclusive end date filter in yyyy-MM-dd format, e.g. '2026-06-08'. Only articles on or before this date are returned.")] string? weekEnd = null,
        [Description("Maximum number of matching documents to return. Defaults to 10.")] int count = 10)
    {
        if (string.IsNullOrWhiteSpace(market))
            return "Error: market is required.";

        DateOnly? fromDate = null, toDate = null;
        if (!string.IsNullOrWhiteSpace(weekStart) && DateOnly.TryParse(weekStart, out var fd)) fromDate = fd;
        if (!string.IsNullOrWhiteSpace(weekEnd) && DateOnly.TryParse(weekEnd, out var td)) toDate = td;

        var names = await _blobStorageService.ListRecentBlobNamesAsync(NewsAnalysisContainer, 100);
        var items = new List<object>();

        foreach (var name in names)
        {
            if (items.Count >= count) break;

            var content = await _blobStorageService.ReadTextAsync(NewsAnalysisContainer, name);
            if (string.IsNullOrWhiteSpace(content)) continue;

            string? title = null, date = null, source = null, markdownContent = null;
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("title", out var t)) title = t.GetString();
                if (root.TryGetProperty("date", out var d)) date = d.GetString();
                else if (root.TryGetProperty("publishDateIso", out var di)) date = di.GetString();
                else if (root.TryGetProperty("publishDate", out var dp)) date = dp.GetString();
                if (root.TryGetProperty("source", out var s)) source = s.GetString();
                else if (root.TryGetProperty("domain", out var dom)) source = dom.GetString();
                if (root.TryGetProperty("markdownContent", out var m)) markdownContent = m.GetString();
                else if (root.TryGetProperty("textContent", out var tc)) markdownContent = tc.GetString();
            }
            catch (JsonException)
            {
                markdownContent = content;
            }

            // Filter by date range if specified.
            if ((fromDate.HasValue || toDate.HasValue) && !string.IsNullOrWhiteSpace(date))
            {
                if (DateOnly.TryParse(date[..Math.Min(10, date.Length)], out var articleDate))
                {
                    if (fromDate.HasValue && articleDate < fromDate.Value) continue;
                    if (toDate.HasValue && articleDate > toDate.Value) continue;
                }
            }

            // Include article only if it mentions the market keyword.
            var searchText = $"{title} {markdownContent}";
            if (searchText.Contains(market, StringComparison.OrdinalIgnoreCase))
                items.Add(new { filename = name, title, date, source, markdownContent });
        }

        return JsonSerializer.Serialize(items, JsonOptions);
    }

    [McpServerTool(Name = "store_weekly_market_research"), Description("Write or update a weekly market research JSON file in the market-research blob container and Fabric Lakehouse. File name: {weekStart}-{market}_research.json.")]
    public async Task<string> StoreWeeklyMarketResearch(
        [Description("Market name, e.g. 'copper', 'gold', 'silver', 'oil'.")] string market,
        [Description("Week start date (Monday) in yyyy-MM-dd format.")] string weekStart,
        [Description("Full research JSON content as a string.")] string researchJson)
    {
        if (string.IsNullOrWhiteSpace(market))
            return "Error: market is required.";
        if (string.IsNullOrWhiteSpace(weekStart))
            return "Error: weekStart is required.";
        if (string.IsNullOrWhiteSpace(researchJson))
            return "Error: researchJson is required.";

        var safeMarket = market.ToLowerInvariant().Trim();
        var filename = $"{weekStart.Trim()}-{safeMarket}_research.json";

        await _blobStorageService.WriteTextAsync(MarketResearchContainer, filename, researchJson, "application/json");
        await _fabricLakehouseService.WriteFileAsync($"market-research/{filename}", researchJson);

        return JsonSerializer.Serialize(new
        {
            filename,
            blobUrl = _blobStorageService.GetBlobUrl(MarketResearchContainer, filename)
        }, JsonOptions);
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

    [McpServerTool(Name = "list_market_research_history"), Description("List market-research JSON files from the market-research blob container filtered by an optional date range, ordered from oldest to newest. Returns a JSON array of { filename, weekStart, weekEnd, market, sentiment, confidence, keyDrivers, summary, bingNews, newsAnalysisArticles }. Use this to build a historical timeline of market view changes.")]
    public async Task<string> ListMarketResearchHistory(
        [Description("Market name to filter by, e.g. 'copper'. Leave empty to include all markets.")] string? market = null,
        [Description("Cutoff date (inclusive) in yyyy-MM-dd format. Only files with a weekStart on or before this date are included. Defaults to today's UTC date when omitted.")] string? upToDate = null,
        [Description("Start date (inclusive) in yyyy-MM-dd format. Only files with a weekStart on or after this date are included. Use to limit the lookback window, e.g. set to 6 months ago.")] string? fromDate = null)
    {
        var cutoff = string.IsNullOrWhiteSpace(upToDate)
            ? DateTime.UtcNow.Date
            : DateTime.TryParseExact(upToDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d.Date : DateTime.UtcNow.Date;

        var from = string.IsNullOrWhiteSpace(fromDate)
            ? (DateTime?)null
            : DateTime.TryParseExact(fromDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fd) ? fd.Date : (DateTime?)null;

        var names = await _blobStorageService.ListBlobNamesAsync(MarketResearchContainer);
        var items = new List<object>();

        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Filename format: {weekStart}-{market}_research.json
            var stem = Path.GetFileNameWithoutExtension(name);
            var dashIdx = stem.IndexOf('-', 8); // skip past yyyy-MM-dd
            var weekStartStr = dashIdx > 0 ? stem[..dashIdx] : stem[..Math.Min(10, stem.Length)];

            if (!DateTime.TryParseExact(weekStartStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var weekStartDate))
                continue;

            if (weekStartDate.Date > cutoff)
                continue;

            if (from.HasValue && weekStartDate.Date < from.Value)
                continue;

            var content = await _blobStorageService.ReadTextAsync(MarketResearchContainer, name);
            if (string.IsNullOrWhiteSpace(content)) continue;

            object entry;
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement.Clone();

                string? fileMarket = root.TryGetProperty("market", out var mProp) ? mProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(market) &&
                    !string.Equals(fileMarket, market, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? weekEnd = root.TryGetProperty("weekEnd", out var weProp) ? weProp.GetString() : null;
                string? sentiment = root.TryGetProperty("sentiment", out var sProp) ? sProp.GetString() : null;
                double? confidence = root.TryGetProperty("confidence", out var cProp) && cProp.TryGetDouble(out var cv) ? cv : null;
                string? summary = root.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() : null;

                var keyDrivers = new List<string>();
                if (root.TryGetProperty("keyDrivers", out var kdProp) && kdProp.ValueKind == JsonValueKind.Array)
                    foreach (var kd in kdProp.EnumerateArray())
                        if (kd.GetString() is { } s) keyDrivers.Add(s);

                var bingNews = root.TryGetProperty("bingNews", out var bnProp)
                    ? JsonSerializer.Deserialize<object[]>(bnProp.GetRawText())
                    : null;

                var newsAnalysisArticles = root.TryGetProperty("newsAnalysisArticles", out var naProp)
                    ? JsonSerializer.Deserialize<object[]>(naProp.GetRawText())
                    : null;

                entry = new
                {
                    filename = name,
                    weekStart = weekStartStr,
                    weekEnd,
                    market = fileMarket,
                    sentiment,
                    confidence,
                    keyDrivers,
                    summary,
                    bingNews,
                    newsAnalysisArticles
                };
            }
            catch (JsonException)
            {
                if (!string.IsNullOrWhiteSpace(market)) continue;
                entry = new { filename = name, weekStart = weekStartStr, weekEnd = (string?)null, market = (string?)null, sentiment = (string?)null, confidence = (double?)null, keyDrivers = new List<string>(), summary = content, bingNews = (object[]?)null, newsAnalysisArticles = (object[]?)null };
            }

            items.Add(entry);
        }

        return JsonSerializer.Serialize(items, JsonOptions);
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

    [McpServerTool(Name = "store_market_insight_for_market"), Description("Store a market insight markdown report for a specific market (e.g. copper, gold, silver, oil) to the market-insight blob container as {date}_{market}_insight.md and to the Fabric Lakehouse market-insight/ folder. Returns JSON with the blob URL, filename, date and market.")]
    public async Task<string> StoreMarketInsightForMarket(
        [Description("Insight report content as markdown")] string content,
        [Description("Market name, e.g. 'copper', 'gold', 'silver', 'oil'")] string market,
        [Description("Report date in yyyy-MM-dd format. Defaults to today's UTC date when omitted.")] string? date = null)
    {
        var resolvedDate = string.IsNullOrWhiteSpace(date)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd")
            : date.Trim();
        var safeMarket = string.IsNullOrWhiteSpace(market) ? "commodity" : market.Trim().ToLowerInvariant();
        var filename = $"{resolvedDate}_{safeMarket}_insight.md";
        var safeContent = content ?? string.Empty;

        await _blobStorageService.WriteTextAsync(MarketInsightContainer, filename, safeContent, "text/markdown");
        await _fabricLakehouseService.WriteFileAsync($"market-insight/{filename}", safeContent);

        return JsonSerializer.Serialize(new
        {
            blobUrl = _blobStorageService.GetBlobUrl(MarketInsightContainer, filename),
            filename,
            date = resolvedDate,
            market = safeMarket
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

    [McpServerTool(Name = "enrich_articles_html"), Description("For each article in a local articles JSON file, download the full HTML page from originalUrl and replace the htmlContent field. Saves the updated file in place. Returns a summary of enriched and skipped articles.")]
    public async Task<string> EnrichArticlesHtml(
        [Description("Optional JSON filename under the app data folder. Defaults to latest articles-*.json.")] string? fileName = null)
    {
        var resolvedPath = ResolveArticlesFilePath(fileName);
        if (resolvedPath is null)
            return "Error: no articles JSON file found in the data folder.";

        var payload = await File.ReadAllTextAsync(resolvedPath);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return $"Error: invalid JSON in '{Path.GetFileName(resolvedPath)}': {ex.Message}";
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return $"Error: '{Path.GetFileName(resolvedPath)}' must contain a JSON array of article objects.";

            var client = CreateClient();
            var enriched = new List<string>();
            var skipped = new List<object>();
            var updatedArticles = new List<JsonElement>();

            foreach (var article in doc.RootElement.EnumerateArray())
            {
                var id = TryGetString(article, "id") ?? "(unknown)";
                var url = TryGetString(article, "originalUrl");
                var title = TryGetString(article, "title") ?? id;

                if (string.IsNullOrWhiteSpace(url))
                {
                    skipped.Add(new { id, title, reason = "no originalUrl" });
                    updatedArticles.Add(article.Clone());
                    continue;
                }

                string html;
                try
                {
                    html = await client.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    skipped.Add(new { id, title, reason = $"download failed: {ex.Message}" });
                    updatedArticles.Add(article.Clone());
                    continue;
                }

                using var ms = new System.IO.MemoryStream();
                using var writer = new System.Text.Json.Utf8JsonWriter(ms);
                writer.WriteStartObject();
                foreach (var prop in article.EnumerateObject())
                {
                    if (prop.NameEquals("htmlContent"))
                        writer.WriteString("htmlContent", html);
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.Flush();

                using var updatedDoc = JsonDocument.Parse(ms.ToArray());
                updatedArticles.Add(updatedDoc.RootElement.Clone());
                enriched.Add(title);
            }

            var outputJson = JsonSerializer.Serialize(updatedArticles, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedPath, outputJson);

            return JsonSerializer.Serialize(new
            {
                sourceFile = Path.GetFileName(resolvedPath),
                enrichedCount = enriched.Count,
                skippedCount = skipped.Count,
                enriched,
                skipped
            }, JsonOptions);
        }
    }

    // =====================================================================
    //  Subscription Report Tools
    // =====================================================================

    private const string SubscriptionReportsContainer = "subscription-reports";

    [McpServerTool(Name = "read_market_insight_for_market"), Description("Read the most recent market insight markdown for a specific market (e.g. copper, gold, silver, oil) from the market-insight blob container. Optionally filters by date. Returns JSON with date, content and filename.")]
    public async Task<string> ReadMarketInsightForMarket(
        [Description("Market name: 'copper', 'gold', 'silver', or 'oil'")] string market,
        [Description("Optional report date in yyyy-MM-dd format. If omitted, returns the most recent insight for this market.")] string? date = null)
    {
        if (string.IsNullOrWhiteSpace(market))
            return "Error: market is required.";

        var safeMarket = market.Trim().ToLowerInvariant();
        var names = await _blobStorageService.ListBlobNamesAsync(MarketInsightContainer);

        // Try exact date match first
        if (!string.IsNullOrWhiteSpace(date))
        {
            var exactFilename = $"{date.Trim()}_{safeMarket}_insight.md";
            var exactContent = await _blobStorageService.ReadTextAsync(MarketInsightContainer, exactFilename);
            if (exactContent is not null)
                return JsonSerializer.Serialize(new { date = date.Trim(), content = exactContent, filename = exactFilename }, JsonOptions);
        }

        // Find latest file for this market
        var match = names
            .Where(n => n.Contains($"_{safeMarket}_insight", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .FirstOrDefault();

        if (match is null)
            return JsonSerializer.Serialize(new { date = string.Empty, content = string.Empty, filename = string.Empty, error = $"No insight found for market '{safeMarket}'." }, JsonOptions);

        var content = await _blobStorageService.ReadTextAsync(MarketInsightContainer, match) ?? string.Empty;
        return JsonSerializer.Serialize(new
        {
            date = ExtractDateFromName(match) ?? string.Empty,
            content,
            filename = match
        }, JsonOptions);
    }

    [McpServerTool(Name = "generate_subscription_report"), Description("Generate a professional branded HTML report for a customer/audience from market insight markdown. Stores the HTML report in the subscription-reports blob container. Returns JSON with filename and base64-encoded HTML content.")]
    public async Task<string> GenerateSubscriptionReport(
        [Description("Market name: 'copper', 'gold', 'silver', or 'oil'")] string market,
        [Description("Customer or audience name, e.g. 'Global Metals Corp'")] string audience,
        [Description("Report period start date in yyyy-MM-dd format")] string fromDate,
        [Description("Report period end date in yyyy-MM-dd format")] string toDate,
        [Description("Full market insight markdown content to include in the report")] string insightMarkdown)
    {
        var safeMarket = string.IsNullOrWhiteSpace(market) ? "commodity" : market.Trim().ToLowerInvariant();
        var safeAudience = string.IsNullOrWhiteSpace(audience) ? "Client" : audience.Trim();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var safeAudienceSlug = new string(safeAudience.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        var filename = $"{today}_{safeMarket}_{safeAudienceSlug}_report.html";

        var htmlContent = BuildReportHtml(safeMarket, safeAudience, fromDate, toDate, today, insightMarkdown ?? string.Empty);

        await _blobStorageService.WriteTextAsync(SubscriptionReportsContainer, filename, htmlContent, "text/html; charset=utf-8");

        return JsonSerializer.Serialize(new
        {
            filename,
            date = today,
            market = safeMarket,
            audience = safeAudience,
            htmlBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(htmlContent))
        }, JsonOptions);
    }

    [McpServerTool(Name = "generate_pdf_report"), Description("Generate a PDF file from a subscription HTML report and save it to the website temp folder. Call this after generate_subscription_report. Returns JSON with pdfFilename and pdfUrl.")]
    public async Task<string> GeneratePdfReport(
        [Description("HTML report filename from generate_subscription_report, e.g. '2026-06-08_copper_global-metals-corp_report.html'")] string htmlFilename)
    {
        var safeFilename = Path.GetFileName(htmlFilename?.Trim() ?? string.Empty);
        if (string.IsNullOrEmpty(safeFilename) || safeFilename != htmlFilename?.Trim())
            return JsonSerializer.Serialize(new { error = "Invalid filename" }, JsonOptions);

        var htmlContent = await _blobStorageService.ReadTextAsync(SubscriptionReportsContainer, safeFilename);
        if (htmlContent is null)
            return JsonSerializer.Serialize(new { error = $"Report '{safeFilename}' not found" }, JsonOptions);

        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
        var tempDir = Path.Combine(webRoot, "temp");
        Directory.CreateDirectory(tempDir);

        var pdfFilename = Path.ChangeExtension(safeFilename, ".pdf");
        var pdfPath = Path.Combine(tempDir, pdfFilename);

        try
        {
            await new BrowserFetcher().DownloadAsync();
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
            });
            await using var page = await browser.NewPageAsync();
            await page.SetContentAsync(htmlContent, new SetContentOptions { WaitUntil = [WaitUntilNavigation.DOMContentLoaded] });
            await page.PdfAsync(pdfPath, new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                MarginOptions = new MarginOptions { Top = "20mm", Bottom = "20mm", Left = "15mm", Right = "15mm" }
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"PDF generation failed: {ex.Message}" }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            pdfFilename,
            pdfUrl = $"/temp/{pdfFilename}",
            success = true
        }, JsonOptions);
    }

    private static string BuildReportHtml(string market, string audience, string fromDate, string toDate, string generatedDate, string markdown)
    {
        var (icon, color, gradientEnd, initials) = market.ToLowerInvariant() switch
        {
            "gold"   => ("🟡", "#b45309", "#92400e", "AU"),
            "silver" => ("⚪", "#475569", "#334155", "AG"),
            "oil"    => ("🛢️", "#1e3a5f", "#0f172a", "OL"),
            _        => ("🟤", "#92400e", "#78350f", "CU")  // copper
        };

        var audienceInitials = string.Join("", audience.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => char.ToUpperInvariant(w[0])));
        if (string.IsNullOrEmpty(audienceInitials)) audienceInitials = "CL";

        var htmlBody = MarkdownToHtml(markdown);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>{{EscapeAttr(market.ToUpperInvariant())}} Market Intelligence — {{EscapeAttr(audience)}}</title>
<style>
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  @page { size: A4; margin: 20mm 15mm; }
  body { font-family: 'Segoe UI', 'Helvetica Neue', Arial, sans-serif; font-size: 13px; line-height: 1.65; color: #1e293b; background: #f8fafc; }
  .page { max-width: 860px; margin: 0 auto; background: #fff; box-shadow: 0 4px 24px rgba(0,0,0,0.08); }
  /* Header */
  .rpt-header { display: flex; align-items: center; justify-content: space-between; padding: 24px 36px; background: #0d1b2e; color: #fff; }
  .rpt-header-left { display: flex; align-items: center; gap: 14px; }
  .rpt-logo-icon { width: 44px; height: 44px; border-radius: 10px; background: rgba(255,255,255,0.12); display: flex; align-items: center; justify-content: center; font-size: 22px; }
  .rpt-brand { color: #fff; }
  .rpt-brand-name { font-size: 16px; font-weight: 700; letter-spacing: -0.3px; }
  .rpt-brand-sub { font-size: 11px; color: #93c5fd; margin-top: 2px; }
  .rpt-header-right { text-align: right; }
  .rpt-gen-date { font-size: 11px; color: #93c5fd; }
  .rpt-period { font-size: 12px; color: #e2e8f0; margin-top: 4px; font-weight: 500; }
  /* Market Banner */
  .rpt-banner { background: linear-gradient(135deg, {{EscapeAttr(color)}} 0%, {{EscapeAttr(gradientEnd)}} 100%); padding: 20px 36px; display: flex; align-items: center; justify-content: space-between; }
  .rpt-market-title { font-size: 22px; font-weight: 700; color: #fff; display: flex; align-items: center; gap: 10px; letter-spacing: -0.5px; }
  .rpt-market-badge { background: rgba(255,255,255,0.18); border: 1px solid rgba(255,255,255,0.3); border-radius: 20px; padding: 3px 12px; font-size: 11px; color: #fff; font-weight: 600; letter-spacing: 1px; text-transform: uppercase; }
  /* Audience Card */
  .rpt-audience { padding: 20px 36px; background: #f1f5f9; border-bottom: 1px solid #e2e8f0; display: flex; align-items: center; gap: 16px; }
  .rpt-audience-avatar { width: 48px; height: 48px; border-radius: 50%; background: {{EscapeAttr(color)}}; color: #fff; font-size: 16px; font-weight: 700; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
  .rpt-audience-label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.8px; color: #64748b; font-weight: 600; }
  .rpt-audience-name { font-size: 17px; font-weight: 700; color: #0f172a; margin-top: 2px; }
  .rpt-confidential { margin-left: auto; background: #fef3c7; border: 1px solid #fcd34d; border-radius: 6px; padding: 4px 10px; font-size: 10px; font-weight: 700; color: #92400e; letter-spacing: 0.5px; text-transform: uppercase; }
  /* Body */
  .rpt-body { padding: 32px 36px; }
  .rpt-body h1 { font-size: 20px; font-weight: 700; color: #0f172a; margin: 0 0 16px; padding-bottom: 8px; border-bottom: 2px solid {{EscapeAttr(color)}}; }
  .rpt-body h2 { font-size: 15px; font-weight: 700; color: {{EscapeAttr(color)}}; margin: 28px 0 10px; padding-left: 10px; border-left: 3px solid {{EscapeAttr(color)}}; }
  .rpt-body h3 { font-size: 13px; font-weight: 600; color: #334155; margin: 18px 0 8px; }
  .rpt-body p { margin: 0 0 12px; color: #334155; }
  .rpt-body ul, .rpt-body ol { margin: 0 0 12px 20px; }
  .rpt-body li { margin-bottom: 4px; color: #334155; }
  .rpt-body strong { color: #0f172a; font-weight: 600; }
  .rpt-body em { color: #475569; }
  .rpt-body table { width: 100%; border-collapse: collapse; margin: 16px 0; font-size: 12px; }
  .rpt-body th { background: {{EscapeAttr(color)}}; color: #fff; padding: 8px 12px; text-align: left; font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; }
  .rpt-body td { padding: 8px 12px; border-bottom: 1px solid #e2e8f0; color: #334155; }
  .rpt-body tr:nth-child(even) td { background: #f8fafc; }
  .rpt-body hr { border: none; border-top: 1px solid #e2e8f0; margin: 24px 0; }
  .rpt-body blockquote { border-left: 3px solid {{EscapeAttr(color)}}; padding: 8px 16px; margin: 12px 0; background: #f8fafc; color: #475569; font-style: italic; }
  /* Footer */
  .rpt-footer { padding: 16px 36px; background: #f1f5f9; border-top: 1px solid #e2e8f0; display: flex; justify-content: space-between; align-items: center; }
  .rpt-footer-left { font-size: 11px; color: #64748b; }
  .rpt-footer-right { font-size: 11px; color: #64748b; text-align: right; }
  @media print {
    body { background: #fff; }
    .page { box-shadow: none; max-width: 100%; }
  }
</style>
</head>
<body>
<div class="page">
  <div class="rpt-header">
    <div class="rpt-header-left">
      <div class="rpt-logo-icon">📈</div>
      <div class="rpt-brand">
        <div class="rpt-brand-name">Market Insight Agent</div>
        <div class="rpt-brand-sub">Professional Market Intelligence Platform</div>
      </div>
    </div>
    <div class="rpt-header-right">
      <div class="rpt-gen-date">Generated: {{EscapeHtml(generatedDate)}}</div>
      <div class="rpt-period">Period: {{EscapeHtml(fromDate)}} – {{EscapeHtml(toDate)}}</div>
    </div>
  </div>

  <div class="rpt-banner">
    <div class="rpt-market-title">
      <span>{{icon}}</span>
      <span>{{EscapeHtml(market.ToUpperInvariant())}} Market Intelligence Report</span>
    </div>
    <span class="rpt-market-badge">{{EscapeHtml(initials)}}</span>
  </div>

  <div class="rpt-audience">
    <div class="rpt-audience-avatar">{{EscapeHtml(audienceInitials)}}</div>
    <div>
      <div class="rpt-audience-label">Prepared exclusively for</div>
      <div class="rpt-audience-name">{{EscapeHtml(audience)}}</div>
    </div>
    <span class="rpt-confidential">Confidential</span>
  </div>

  <div class="rpt-body">
    {{htmlBody}}
  </div>

  <div class="rpt-footer">
    <div class="rpt-footer-left">
      Market Insight Agent · Confidential &amp; Proprietary<br/>
      This report is prepared exclusively for {{EscapeHtml(audience)}} and may not be redistributed.
    </div>
    <div class="rpt-footer-right">
      Report Date: {{EscapeHtml(generatedDate)}}<br/>
      © {{DateTime.UtcNow.Year}} Market Insight Agent
    </div>
  </div>
</div>
</body>
</html>
""";
    }

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string EscapeAttr(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string MarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var lines = markdown.Split('\n');
        var sb = new System.Text.StringBuilder();
        var inUl = false;
        var inOl = false;
        var inTable = false;
        var tableHasHeader = false;
        var pendingParagraph = new System.Text.StringBuilder();

        void FlushParagraph()
        {
            if (pendingParagraph.Length > 0)
            {
                sb.AppendLine($"<p>{InlineMarkdown(pendingParagraph.ToString().Trim())}</p>");
                pendingParagraph.Clear();
            }
        }
        void CloseList()
        {
            if (inUl) { sb.AppendLine("</ul>"); inUl = false; }
            if (inOl) { sb.AppendLine("</ol>"); inOl = false; }
        }
        void CloseTable()
        {
            if (inTable) { sb.AppendLine("</tbody></table>"); inTable = false; tableHasHeader = false; }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var line = raw;

            // Table row
            if (line.TrimStart().StartsWith('|') && line.TrimEnd().EndsWith('|'))
            {
                FlushParagraph();
                CloseList();

                // Separator row (| --- | --- |)
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\|[\s|:\-]+\|$"))
                {
                    if (inTable && !tableHasHeader)
                    {
                        sb.AppendLine("</thead><tbody>");
                        tableHasHeader = true;
                    }
                    continue;
                }

                if (!inTable)
                {
                    sb.AppendLine("<table><thead>");
                    inTable = true;
                    tableHasHeader = false;
                }

                var cells = line.Trim('|').Split('|');
                var tag = tableHasHeader ? "td" : "th";
                sb.Append(tableHasHeader ? "<tr>" : "<tr>");
                foreach (var cell in cells)
                    sb.Append($"<{tag}>{InlineMarkdown(cell.Trim())}</{tag}>");
                sb.AppendLine("</tr>");
                continue;
            }

            CloseTable();

            // Blank line
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            // ATX Headers
            if (line.StartsWith("### ")) { FlushParagraph(); CloseList(); sb.AppendLine($"<h3>{InlineMarkdown(line[4..].Trim())}</h3>"); continue; }
            if (line.StartsWith("## "))  { FlushParagraph(); CloseList(); sb.AppendLine($"<h2>{InlineMarkdown(line[3..].Trim())}</h2>"); continue; }
            if (line.StartsWith("# "))   { FlushParagraph(); CloseList(); sb.AppendLine($"<h1>{InlineMarkdown(line[2..].Trim())}</h1>"); continue; }

            // Horizontal rule
            if (System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^[-*_]{3,}$"))
            { FlushParagraph(); CloseList(); sb.AppendLine("<hr/>"); continue; }

            // Unordered list
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                FlushParagraph();
                if (inOl) { sb.AppendLine("</ol>"); inOl = false; }
                if (!inUl) { sb.AppendLine("<ul>"); inUl = true; }
                var text = line.TrimStart().TrimStart('-', '*').TrimStart();
                sb.AppendLine($"<li>{InlineMarkdown(text)}</li>");
                continue;
            }

            // Ordered list
            var olMatch = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^\d+\.\s+(.+)");
            if (olMatch.Success)
            {
                FlushParagraph();
                if (inUl) { sb.AppendLine("</ul>"); inUl = false; }
                if (!inOl) { sb.AppendLine("<ol>"); inOl = true; }
                sb.AppendLine($"<li>{InlineMarkdown(olMatch.Groups[1].Value)}</li>");
                continue;
            }

            // Blockquote
            if (line.TrimStart().StartsWith("> "))
            {
                FlushParagraph(); CloseList();
                sb.AppendLine($"<blockquote>{InlineMarkdown(line.TrimStart().Substring(2))}</blockquote>");
                continue;
            }

            // Normal text — accumulate as paragraph
            if (pendingParagraph.Length > 0) pendingParagraph.Append(' ');
            pendingParagraph.Append(line.Trim());
        }

        FlushParagraph();
        CloseList();
        CloseTable();

        return sb.ToString();
    }

    private static string InlineMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Escape HTML first
        text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        // Bold+Italic ***text*** or ___text___
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"___(.+?)___", "<strong><em>$1</em></strong>");
        // Bold **text** or __text__
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__", "<strong>$1</strong>");
        // Italic *text* or _text_
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.+?)_", "<em>$1</em>");
        // Code `text`
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
        // Links [text](url)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
        return text;
    }

    private static readonly ReverseMarkdown.Converter _markdownConverter = new(new ReverseMarkdown.Config
    {
        UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    private static string HtmlToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        return _markdownConverter.Convert(html).Trim();
    }

    private static string StripTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        return Regex.Replace(html, @"<[^>]+>", string.Empty);
    }

    private static string StripHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sb = new System.Text.StringBuilder(html.Length);
        var inTag = false;
        foreach (var ch in html)
        {
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(ch);
        }

        // Collapse runs of whitespace to single spaces and trim.
        var raw = sb.ToString();
        var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
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

    private string? ResolveArticlesFilePath(string? fileName)
    {
        var dataRoot = Path.Combine(_environment.ContentRootPath, DataFolderName);
        if (!Directory.Exists(dataRoot))
            return null;

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var candidate = Path.Combine(dataRoot, fileName.Trim());
            return File.Exists(candidate) ? candidate : null;
        }

        var latestMatching = Directory.GetFiles(dataRoot, ArticlesFilePattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return latestMatching;
    }

    private static string? TryGetString(JsonElement article, string propertyName)
    {
        if (!article.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryGetGuidFromArticle(JsonElement article, out Guid guid)
    {
        var guidValue = TryGetString(article, "guid") ?? TryGetString(article, "id");
        return Guid.TryParse(guidValue, out guid);
    }

    private static DateTimeOffset GetArticleDateTime(JsonElement article)
    {
        var iso = TryGetString(article, "publishDateIso")
            ?? TryGetString(article, "publishDate")
            ?? TryGetString(article, "dateTime")
            ?? TryGetString(article, "timestamp");

        if (!string.IsNullOrWhiteSpace(iso)
            && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.UtcNow;
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
