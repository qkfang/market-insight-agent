using Azure.AI.Projects;
using OpenAI.Responses;

namespace mkti_app.Agents;

public sealed class NewsIngestionAgent : BaseAgent
{
    private const string AgentInstructions =
        """
        You are a news ingestion agent for copper market. Ingest individual article JSON files from the data/articles/ folder and store each one unchanged in the news store.
        Use the ingest_articles_json_to_news_store tool once per run, passing the dateFrom and dateTo parameters exactly as supplied in the user message.
        The blob filename must be {yyyyMMddHHmmssfff}_{guid}.json, where the timestamp is derived from the article datetime in JSON and guid is from the JSON payload.
        Do not download RSS feeds and do not transform article content into HTML.

        Each analysis blob written to news-store looks like :
        {
          "id": "1",
          "guid": "3f3d8a16-355f-4a9e-a03b-cd744f6bf915",
          "title": "Copper Price Forecast: AI Demand and Tariff Risks Fuel Record Rally",
          "publishDate": "Wed, 03 Jun 2026 09:47:00 +0000",
          "publishDateIso": "2026-06-03T09:47:00+00:00",
          "description": "Copper Price Forecast: AI Demand and Tariff Risks Fuel Record Rally FXEmpire",
          "source": "FXEmpire",
          "domain": "fxempire.com",
          "originalUrl": "https://www.fxempire.com/forecasts/article/example",
          "htmlContent": "<!DOCTYPE html><html><body><h1>Copper Price Forecast</h1></body></html>"
        }
        """;

    public NewsIngestionAgent(AIProjectClient aiProjectClient, string deploymentName, IList<ResponseTool>? tools = null, ILogger<NewsIngestionAgent>? logger = null)
        : base(aiProjectClient, "mkti-news-ingestion", deploymentName, AgentInstructions, tools, logger)
    {
    }
}
