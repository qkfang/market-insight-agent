using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsIngestionAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a news ingestion agent for copper market. Download articles from RSS feeds and store each article as HTML in the news store. Track what has been stored to avoid duplicates. " +
        "Use the fetch_rss_feed tool to discover copper market articles, list_stored_news to check what is already stored, download_article to retrieve full HTML, and store_news_article to persist each new article.";

    public NewsIngestionAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsIngestionAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-ingestion", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
