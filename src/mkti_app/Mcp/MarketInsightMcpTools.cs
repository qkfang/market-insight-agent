using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using mkti_app.Services;
using ModelContextProtocol.Server;

namespace mkti_app.Mcp;

[McpServerToolType]
public sealed class MarketInsightMcpTools
{
    private static readonly string[] DefaultRssFeeds =
    [
        "https://feeds.bloomberg.com/markets/news.rss",
        "https://www.mining.com/feed/",
        "https://www.kitco.com/rss/news-kitco-metals.xml"
    ];

    private readonly BlobStorageService _blobStorageService;
    private readonly FabricLakehouseService _fabricLakehouseService;
    private readonly IHttpClientFactory _httpClientFactory;

    public MarketInsightMcpTools(
        BlobStorageService blobStorageService,
        FabricLakehouseService fabricLakehouseService,
        IHttpClientFactory httpClientFactory)
    {
        _blobStorageService = blobStorageService;
        _fabricLakehouseService = fabricLakehouseService;
        _httpClientFactory = httpClientFactory;
    }

    [McpServerTool(Name = "fetch_rss_feed"), Description("Download and parse a copper market RSS/Atom feed. Returns a JSON array of articles with title, url, publishDate and description.")]
    public async Task<string> FetchRssFeed(
        [Description("RSS feed URL. If omitted, the default copper market feeds are used.")] string? feedUrl = null)
    {
        var feeds = string.IsNullOrWhiteSpace(feedUrl) ? DefaultRssFeeds : [feedUrl];
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

    [McpServerTool(Name = "store_news_article"), Description("Store a news article to the Azure Blob 'news-store' container and the Fabric Lakehouse 'news_store' folder. Returns the storage path.")]
    public async Task<string> StoreNewsArticle(
        [Description("Target filename, e.g. mining-com-2026-01-01.html")] string filename,
        [Description("Article content (html or text)")] string content,
        [Description("Content type, e.g. text/html or application/pdf")] string contentType)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "Error: filename is required.";

        var safeContent = content ?? string.Empty;
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "text/html" : contentType;

        await _blobStorageService.WriteTextAsync("news-store", filename, safeContent, resolvedContentType);

        var fabricPath = await _fabricLakehouseService.UploadFileAsync(
            "news_store", filename, System.Text.Encoding.UTF8.GetBytes(safeContent));

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
