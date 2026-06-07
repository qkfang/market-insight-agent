using System.Diagnostics;
using mkti_app.Agents;
using mkti_app.Services;

namespace mkti_app;

public static class Apis
{
    private const string DataFolderName = "data";
    private const string ArticlesFolderName = "articles";
    private const string KnowledgeArticlesFileName = "articles.json";
    private const int KnowledgeTopArticleCount = 3;
    private const int InsightPreviewMaxLength = 500;

    public static void MapAllEndpoints(
        this WebApplication app,
        NewsIngestionAgent newsIngestionAgent,
        NewsAnalysisAgent newsAnalysisAgent,
        MarketResearchAgent marketResearchAgent,
        InsightGenerationAgent insightGenerationAgent,
        SubscriptionAgent subscriptionAgent,
        BlobStorageService blobStorageService,
        FabricLakehouseService fabricLakehouseService,
        ILogger logger)
    {
        app.MapGet("/api/news/ingest", async (string? from, string? to) =>
        {
            logger.LogInformation("/api/news/ingest called, from={From}, to={To}", from, to);
            var sw = Stopwatch.StartNew();
            var before = await blobStorageService.ListBlobNamesAsync("news-store");
            var dateArgs = (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
                ? $" Use dateFrom='{from}' and dateTo='{to}'."
                : string.Empty;
            logger.LogInformation("Invoking NewsIngestionAgent");
            var result = await newsIngestionAgent.RunAsync($"Ingest articles from the data/articles/ folder and store each one in news-store using {{yyyyMMddHHmmssfff}}_{{guid}}.json blob names.{dateArgs}");
            var after = await blobStorageService.ListBlobNamesAsync("news-store");

            var delta = after.Count - before.Count;
            var articlesStored = Math.Max(0, delta);
            sw.Stop();
            logger.LogInformation("/api/news/ingest completed in {ElapsedMs}ms, articlesStored={ArticlesStored}", sw.ElapsedMilliseconds, articlesStored);

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

        app.MapGet("/api/articles/list", (string? from, string? to, IWebHostEnvironment env) =>
        {
            var articlesDir = Path.Combine(env.ContentRootPath, DataFolderName, ArticlesFolderName);
            if (!Directory.Exists(articlesDir))
                return Results.Json(new { success = true, filenames = Array.Empty<string>() });

            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrWhiteSpace(from) && DateOnly.TryParse(from, out var fd)) fromDate = fd;
            if (!string.IsNullOrWhiteSpace(to) && DateOnly.TryParse(to, out var td)) toDate = td;

            var filenames = Directory.GetFiles(articlesDir, "*.json", SearchOption.TopDirectoryOnly)
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
                .Select(Path.GetFileName)
                .ToArray();

            return Results.Json(new { success = true, filenames });
        });

        app.MapGet("/api/knowledge/run", async (HttpContext httpContext, IHttpClientFactory httpClientFactory, string? from, string? to) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(120);

            // Step 1: Ingest (same as Ingest tab)
            var ingestParams = new System.Collections.Specialized.NameValueCollection();
            if (!string.IsNullOrWhiteSpace(from)) ingestParams["from"] = from;
            if (!string.IsNullOrWhiteSpace(to)) ingestParams["to"] = to;
            var ingestQuery = ingestParams.Count > 0
                ? "?" + string.Join("&", ingestParams.AllKeys.Select(k => $"{k}={Uri.EscapeDataString(ingestParams[k]!)}"))
                : string.Empty;
            var ingestResponse = await client.GetAsync($"{baseUrl}/api/news/ingest{ingestQuery}");
            ingestResponse.EnsureSuccessStatusCode();
            var ingestJson = await ingestResponse.Content.ReadAsStringAsync();
            var ingestResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(ingestJson);

            // Step 2: Analyze (same as Analyze tab)
            var analyzeResponse = await client.GetAsync($"{baseUrl}/api/news/analyze");
            analyzeResponse.EnsureSuccessStatusCode();
            var analyzeJson = await analyzeResponse.Content.ReadAsStringAsync();
            var analyzeResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(analyzeJson);

            // Step 3: Research — iterate week by week over the date range, all 4 markets each week
            const string allMarkets = "copper,gold,silver,oil";
            var startDate = DateOnly.TryParse(from, out var sd) ? sd : new DateOnly(2026, 3, 1);
            var endDate   = DateOnly.TryParse(to,   out var ed) ? ed : DateOnly.FromDateTime(DateTime.UtcNow.Date);

            // Align to the Monday of the week containing startDate
            var daysFromMonday = ((int)startDate.DayOfWeek + 6) % 7;
            var weekCursor = startDate.AddDays(-daysFromMonday);

            var researchWeeks = new List<object>();
            while (weekCursor <= endDate)
            {
                var weekEnd = weekCursor.AddDays(6);
                var weekStartStr = weekCursor.ToString("yyyy-MM-dd");
                var weekEndStr   = weekEnd.ToString("yyyy-MM-dd");

                logger.LogInformation("Knowledge pipeline: research week {WeekStart} to {WeekEnd}", weekStartStr, weekEndStr);
                var researchResp = await client.GetAsync(
                    $"{baseUrl}/api/market/research?from={weekStartStr}&to={weekEndStr}&markets={allMarkets}");
                researchResp.EnsureSuccessStatusCode();
                var researchResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                    await researchResp.Content.ReadAsStringAsync());
                researchWeeks.Add(new { weekStart = weekStartStr, weekEnd = weekEndStr, result = researchResult });

                weekCursor = weekCursor.AddDays(7);
            }

            // Step 4: Insight generation — iterate week by week over the date range, all 4 markets each week
            weekCursor = startDate.AddDays(-daysFromMonday);
            var insightWeeks = new List<object>();
            while (weekCursor <= endDate)
            {
                var weekEnd = weekCursor.AddDays(6);
                var weekStartStr = weekCursor.ToString("yyyy-MM-dd");
                var weekEndStr   = weekEnd.ToString("yyyy-MM-dd");

                logger.LogInformation("Knowledge pipeline: insight generation week {WeekStart} to {WeekEnd}", weekStartStr, weekEndStr);
                var insightResp = await client.GetAsync(
                    $"{baseUrl}/api/insight/generate?from={weekStartStr}&to={weekEndStr}&markets={allMarkets}");
                insightResp.EnsureSuccessStatusCode();
                var insightResult = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                    await insightResp.Content.ReadAsStringAsync());
                insightWeeks.Add(new { weekStart = weekStartStr, weekEnd = weekEndStr, result = insightResult });

                weekCursor = weekCursor.AddDays(7);
            }

            return Results.Json(new
            {
                success = true,
                ingest = ingestResult,
                analysis = analyzeResult,
                researchWeeks,
                insightWeeks
            });
        });

        app.MapGet("/api/news/analyze", async () =>
        {
            logger.LogInformation("/api/news/analyze called");
            var sw = Stopwatch.StartNew();
            var before = new HashSet<string>(
                await blobStorageService.ListBlobNamesAsync("news-analysis"),
                StringComparer.OrdinalIgnoreCase);

            logger.LogInformation("Invoking NewsAnalysisAgent");
            var result = await newsAnalysisAgent.RunAsync(
                "Analyze all unprocessed news-store JSON articles and store structured analysis in news-analysis using {yyyyMMddHHmmssfff}_{guid}.json blob names.");

            var afterNames = await blobStorageService.ListBlobNamesAsync("news-analysis");
            var results = new List<object>();
            foreach (var name in afterNames.Where(n => !before.Contains(n)))
            {
                var content = await blobStorageService.ReadTextAsync("news-analysis", name);
                int? wordCount = TryGetWordCount(content);
                results.Add(new { filename = name, wordCount });
            }

            sw.Stop();
            logger.LogInformation("/api/news/analyze completed in {ElapsedMs}ms, articlesAnalyzed={Count}", sw.ElapsedMilliseconds, results.Count);

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
                    else if (root.TryGetProperty("publishDateIso", out var di)) date = di.GetString();
                    else if (root.TryGetProperty("publishDate", out var dp)) date = dp.GetString();
                    if (root.TryGetProperty("source", out var s)) source = s.GetString();
                    else if (root.TryGetProperty("domain", out var dom)) source = dom.GetString();
                    if (root.TryGetProperty("wordCount", out var w) && w.TryGetInt32(out var wc)) wordCount = wc;
                    else if (root.TryGetProperty("textContent", out var tc) && tc.GetString() is { } tcStr)
                        wordCount = tcStr.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length;
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

        app.MapGet("/api/market/research", async (string? from, string? to, string? markets) =>
        {
            logger.LogInformation("/api/market/research called, from={From}, to={To}, markets={Markets}", from, to, markets);
            var allMarkets = new[] { "copper", "gold", "silver", "oil" };
            var selectedMarkets = string.IsNullOrWhiteSpace(markets)
                ? allMarkets
                : markets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(m => allMarkets.Contains(m, StringComparer.OrdinalIgnoreCase))
                         .ToArray();
            if (selectedMarkets.Length == 0) selectedMarkets = allMarkets;

            // Use provided from/to, or fall back to the Monday–Sunday of the current UTC week.
            var today = DateTime.UtcNow.Date;
            var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekStart = today.AddDays(-daysFromMonday);
            var weekEnd = weekStart.AddDays(6);
            var weekStartStr = !string.IsNullOrWhiteSpace(from) ? from : weekStart.ToString("yyyy-MM-dd");
            var weekEndStr   = !string.IsNullOrWhiteSpace(to)   ? to   : weekEnd.ToString("yyyy-MM-dd");

            var results = new List<object>();
            foreach (var market in selectedMarkets)
            {
                var message =
                    $"Research the {market} market for the week {weekStartStr} to {weekEndStr}. " +
                    $"market={market}, weekStart={weekStartStr}, weekEnd={weekEndStr}. " +
                    $"Use read_news_analysis_by_market for market '{market}', " +
                    $"use bing_search_market for market '{market}' with week range '{weekStartStr} to {weekEndStr}', " +
                    $"then call store_weekly_market_research with market='{market}', weekStart='{weekStartStr}'.";

                logger.LogInformation("Invoking MarketResearchAgent for market={Market}, week={WeekStart}..{WeekEnd}", market, weekStartStr, weekEndStr);
                var marketSw = Stopwatch.StartNew();
                var result = await marketResearchAgent.RunAsync(message);
                marketSw.Stop();
                logger.LogInformation("MarketResearchAgent completed for market={Market} in {ElapsedMs}ms", market, marketSw.ElapsedMilliseconds);

                string? sentiment = null, summary = null;
                double? confidence = null;
                string[]? keyDrivers = null;
                if (!string.IsNullOrWhiteSpace(result))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(result);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("sentiment", out var s)) sentiment = s.GetString();
                        if (root.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var cv)) confidence = cv;
                        if (root.TryGetProperty("summary", out var sum)) summary = sum.GetString();
                        if (root.TryGetProperty("keyDrivers", out var kd) && kd.ValueKind == System.Text.Json.JsonValueKind.Array)
                            keyDrivers = kd.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
                    }
                    catch { /* leave nulls if JSON can't be parsed */ }
                }

                results.Add(new { market, weekStart = weekStartStr, weekEnd = weekEndStr, sentiment, confidence, keyDrivers, summary });
            }

            return Results.Json(new
            {
                status = "ok",
                weekStart = weekStartStr,
                weekEnd = weekEndStr,
                markets = results
            });
        });

        app.MapGet("/api/insight/generate", async (string? from, string? to, string? markets) =>
        {
            logger.LogInformation("/api/insight/generate called, from={From}, to={To}, markets={Markets}", from, to, markets);
            var allMarkets = new[] { "copper", "gold", "silver", "oil" };
            var selectedMarkets = string.IsNullOrWhiteSpace(markets)
                ? allMarkets
                : markets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(m => allMarkets.Contains(m, StringComparer.OrdinalIgnoreCase))
                         .ToArray();
            if (selectedMarkets.Length == 0) selectedMarkets = allMarkets;

            var today = DateTime.UtcNow.Date;
            // Default: current week end + 6 weeks lookback
            var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekEnd = today.AddDays(6 - daysFromMonday);
            var weekStart = weekEnd.AddDays(-41); // ~6 weeks back
            var fromStr = !string.IsNullOrWhiteSpace(from) ? from : weekStart.ToString("yyyy-MM-dd");
            var toStr   = !string.IsNullOrWhiteSpace(to)   ? to   : weekEnd.ToString("yyyy-MM-dd");
            var todayStr = today.ToString("yyyy-MM-dd");

            var results = new List<object>();
            foreach (var market in selectedMarkets)
            {
                var message =
                    $"Generate a professional market insight report for the {market} market. " +
                    $"Focus ONLY on the {market} market. " +
                    $"Use list_market_research_history with market='{market}' to retrieve all historical weekly snapshots (look back as many weeks as available, at least 6 weeks). " +
                    $"Date range for context: {fromStr} to {toStr}. " +
                    $"Store the result by calling store_market_insight_for_market with market='{market}' and date='{todayStr}'.";

                logger.LogInformation("Invoking InsightGenerationAgent for market={Market}", market);
                var insightSw = Stopwatch.StartNew();
                await insightGenerationAgent.RunAsync(message);
                insightSw.Stop();
                logger.LogInformation("InsightGenerationAgent completed for market={Market} in {ElapsedMs}ms", market, insightSw.ElapsedMilliseconds);

                // Read back the stored insight for this market
                var filename = $"{todayStr}_{market}_insight.md";
                var content = await blobStorageService.ReadTextAsync("market-insight", filename) ?? string.Empty;
                if (string.IsNullOrEmpty(content))
                {
                    // Fallback: find any recently stored file for this market
                    var names = await blobStorageService.ListBlobNamesAsync("market-insight");
                    var match = names.OrderByDescending(n => n, StringComparer.Ordinal)
                        .FirstOrDefault(n => n.Contains($"_{market}_insight", StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        filename = match;
                        content = await blobStorageService.ReadTextAsync("market-insight", match) ?? string.Empty;
                    }
                }

                var preview = content.Length > InsightPreviewMaxLength
                    ? content[..InsightPreviewMaxLength]
                    : content;

                results.Add(new { market, date = todayStr, filename, preview });
            }

            return Results.Json(new
            {
                status = "ok",
                weekStart = fromStr,
                weekEnd = toStr,
                markets = results
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
                .Select(n =>
                {
                    var market = ExtractInsightMarket(n);
                    return new { filename = n, date = ExtractInsightDate(n), market };
                })
                .ToArray();
            return Results.Json(new { reports });
        });

        app.MapGet("/api/insight/content", async (string name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.Json(new { status = "error", error = "name is required." });
            var content = await blobStorageService.ReadTextAsync("market-insight", name);
            if (content is null)
                return Results.Json(new { status = "error", error = "Insight not found." });
            return Results.Json(new { status = "ok", content });
        });

        app.MapGet("/api/market/research/list", async () =>
        {
            var names = await blobStorageService.ListBlobNamesAsync("market-research");
            var reports = names
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .Select(n =>
                {
                    // filename format: {weekStart}-{market}_research.json
                    var stem = System.IO.Path.GetFileNameWithoutExtension(n); // e.g. 2026-06-02-copper_research
                    string? weekStart = null, market = null;
                    var suffixIdx = stem.LastIndexOf("_research", StringComparison.OrdinalIgnoreCase);
                    if (suffixIdx > 10)
                    {
                        var datePart = stem[..10]; // yyyy-MM-dd
                        var marketPart = stem[(datePart.Length + 1)..suffixIdx]; // skip the dash
                        weekStart = datePart;
                        market = marketPart;
                    }
                    return new { filename = n, weekStart, market };
                })
                .ToArray();
            return Results.Json(new { reports });
        });

        app.MapGet("/api/market/research/content", async (string name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.Json(new { status = "error", error = "name is required." });
            var content = await blobStorageService.ReadTextAsync("market-research", name);
            if (content is null)
                return Results.Json(new { status = "error", error = "Research not found." });
            return Results.Json(new { status = "ok", content });
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

        app.MapPost("/api/subscription/generate", async (SubscriptionGenerateRequest request) =>
        {
            logger.LogInformation("/api/subscription/generate called, audience={Audience}, markets={Markets}, from={From}, to={To}",
                request.Audience, request.Markets is null ? null : string.Join(",", request.Markets), request.From, request.To);
            var allMarkets = new[] { "copper", "gold", "silver", "oil" };
            var selectedMarkets = (request.Markets ?? [])
                .Select(m => m.Trim().ToLowerInvariant())
                .Where(m => allMarkets.Contains(m))
                .Distinct()
                .ToArray();
            if (selectedMarkets.Length == 0) selectedMarkets = ["copper"];

            var audience = string.IsNullOrWhiteSpace(request.Audience) ? "Client" : request.Audience.Trim();
            var fromDate = request.From ?? DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
            var toDate   = request.To   ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

            var results = new List<object>();
            foreach (var market in selectedMarkets)
            {
                var message =
                    $"Generate a subscription report for the {market} market for customer '{audience}'. " +
                    $"Date range: {fromDate} to {toDate}. " +
                    $"Use read_market_insight_for_market with market='{market}' to get the insight, " +
                    $"then call generate_subscription_report with market='{market}', audience='{audience}', " +
                    $"fromDate='{fromDate}', toDate='{toDate}', and the retrieved insight markdown. " +
                    $"Return the filename from generate_subscription_report.";

                logger.LogInformation("Invoking SubscriptionAgent for market={Market}, audience={Audience}", market, audience);
                var subSw = Stopwatch.StartNew();
                var agentResult = await subscriptionAgent.RunAsync(message);
                subSw.Stop();
                logger.LogInformation("SubscriptionAgent completed for market={Market} in {ElapsedMs}ms", market, subSw.ElapsedMilliseconds);

                // Try to parse the filename from the agent result
                string? filename = null;
                string? htmlBase64 = null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(agentResult);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("filename", out var fn)) filename = fn.GetString();
                    if (root.TryGetProperty("htmlBase64", out var hb)) htmlBase64 = hb.GetString();
                }
                catch
                {
                    // Try to extract filename from free-text
                    var fnMatch = System.Text.RegularExpressions.Regex.Match(agentResult, @"\d{4}-\d{2}-\d{2}_\w+_[\w-]+_report\.html");
                    if (fnMatch.Success) filename = fnMatch.Value;
                }

                // If agent did not produce base64, read the stored HTML from blob
                if (string.IsNullOrEmpty(htmlBase64) && !string.IsNullOrEmpty(filename))
                {
                    var htmlContent = await blobStorageService.ReadTextAsync("market-subscription", filename);
                    if (!string.IsNullOrEmpty(htmlContent))
                        htmlBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(htmlContent));
                }

                results.Add(new
                {
                    market,
                    audience,
                    filename = filename ?? string.Empty,
                    reportUrl = string.IsNullOrEmpty(filename) ? string.Empty : $"/api/subscription/report/{Uri.EscapeDataString(filename)}",
                    htmlBase64 = htmlBase64 ?? string.Empty
                });
            }

            return Results.Json(new { status = "ok", reports = results });
        });

        app.MapGet("/api/subscription/report/{filename}", async (string filename) =>
        {
            if (string.IsNullOrWhiteSpace(filename))
                return Results.BadRequest("filename is required");

            // Prevent path traversal
            var safeName = Path.GetFileName(filename);
            if (string.IsNullOrEmpty(safeName) || safeName != filename)
                return Results.BadRequest("Invalid filename");

            var content = await blobStorageService.ReadTextAsync("market-subscription", safeName);
            if (content is null)
                return Results.NotFound();

            return Results.Content(content, "text/html; charset=utf-8");
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

    private static string? ExtractInsightMarket(string filename)
    {
        // filename format: {date}_{market}_insight.md
        var stem = Path.GetFileNameWithoutExtension(filename);
        var parts = stem.Split('_');
        return parts.Length >= 2 ? parts[1] : null;
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

}

public sealed record SubscriptionGenerateRequest(
    IReadOnlyList<string>? Markets,
    string? Audience,
    string? From,
    string? To);

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
