using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsIngestionAgent : BaseAgent
{
    private const string AgentInstructions =
        "You are a news ingestion agent for copper market. Ingest from the local mock dataset file articles-june.json and store each article object unchanged in the news store. " +
        "Use the ingest_articles_json_to_news_store tool once per run. " +
        "The blob filename must be {yyyyMMddHHmmssfff}_{guid}.json, where the timestamp is derived from the article datetime in JSON and guid is from the JSON payload. " +
        "Do not download RSS feeds and do not transform article content into HTML.";

    public NewsIngestionAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsIngestionAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-ingestion", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
